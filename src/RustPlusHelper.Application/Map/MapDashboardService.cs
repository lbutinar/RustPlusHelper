using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Application.Security;

namespace RustPlusHelper.Application.Map;

/// <summary>
/// Loads the map-first read model through the application-owned Rust+ boundary. Connection
/// supervision and polling will replace this one-shot Phase 1 loader in later phases.
/// </summary>
public sealed class MapDashboardService(
    IRustPlusClient client,
    RustPlusConnectionOptions connectionOptions) : IDisposable, IAsyncDisposable
{
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _disposed;

    public event EventHandler? StateChanged;

    public MapDashboardState Current { get; private set; } = MapDashboardState.NotStarted;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Current.ConnectionState is DashboardConnectionState.Loading or DashboardConnectionState.Ready)
            {
                return;
            }

            SetState(Current with
            {
                ConnectionState = DashboardConnectionState.Loading,
                ConnectionLabel = "Loading demo session",
                ErrorMessage = null
            });

            try
            {
                await client.ConnectAsync(connectionOptions, cancellationToken).ConfigureAwait(false);

                var server = Require(
                    await client.GetServerInfoAsync(cancellationToken).ConfigureAwait(false),
                    "server information");
                var map = Require(
                    await client.GetMapAsync(cancellationToken).ConfigureAwait(false),
                    "map");
                var team = Require(
                    await client.GetTeamAsync(cancellationToken).ConfigureAwait(false),
                    "team information");
                var chat = Require(
                    await client.GetTeamChatAsync(cancellationToken).ConfigureAwait(false),
                    "team chat");
                var markers = Require(
                    await client.GetMapMarkersAsync(cancellationToken).ConfigureAwait(false),
                    "map markers");

                SetState(new MapDashboardState(
                    DashboardConnectionState.Ready,
                    "Demo connected",
                    server,
                    map,
                    team,
                    chat,
                    markers,
                    Current.Layers,
                    null));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                SetState(Current with
                {
                    ConnectionState = DashboardConnectionState.Failed,
                    ConnectionLabel = "Demo unavailable",
                    ErrorMessage = SecretRedactor.Redact(exception.Message)
                });
            }
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public void SetLayerVisibility(MapLayerKind kind, bool isVisible)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var layers = Current.Layers
            .Select(layer => layer.Kind == kind && layer.IsAvailable
                ? layer with { IsVisible = isVisible }
                : layer)
            .ToArray();

        SetState(Current with { Layers = layers });
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            await client.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _initializationLock.Dispose();
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private void SetState(MapDashboardState state)
    {
        Current = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static T Require<T>(RustPlusResult<T> result, string operation)
    {
        if (result.IsSuccess && result.Data is not null)
        {
            return result.Data;
        }

        var code = result.Error?.Code ?? "unknown_error";
        var message = result.Error?.Message ?? "No response data was returned.";
        throw new InvalidOperationException($"Failed to load {operation} ({code}): {message}");
    }
}
