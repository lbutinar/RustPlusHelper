using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Application.Security;
using RustPlusHelper.Application.Servers;

namespace RustPlusHelper.Application.Map;

/// <summary>
/// Owns the map-first read model. Saved servers use a short-lived production Rust+ connection and
/// an offline cache; the deterministic source remains available when no server has been saved.
/// </summary>
public sealed class MapDashboardService(
    IRustPlusClient demoClient,
    RustPlusConnectionOptions demoConnectionOptions,
    ServerManager servers,
    RustPlusConnectionManager connections,
    IMapCacheRepository mapCache,
    TimeProvider timeProvider) : IDisposable, IAsyncDisposable
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

            if (servers.SelectedServerId is { } serverId)
            {
                await LoadServerCoreAsync(serverId, forceRefresh: false, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await LoadDemoAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task LoadServerAsync(
        Guid serverId,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LoadServerCoreAsync(serverId, forceRefresh, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public Task RefreshSelectedServerAsync(CancellationToken cancellationToken = default)
    {
        var serverId = Current.ServerId ?? servers.SelectedServerId;
        return serverId is { } selected
            ? LoadServerAsync(selected, forceRefresh: true, cancellationToken)
            : Task.CompletedTask;
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
            await demoClient.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _initializationLock.Dispose();
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private async Task LoadServerCoreAsync(
        Guid serverId,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (!servers.Select(serverId))
        {
            SetState(MapDashboardState.NotStarted with
            {
                ConnectionState = DashboardConnectionState.Failed,
                ConnectionLabel = "Server unavailable",
                ErrorMessage = "The selected saved server no longer exists."
            });
            return;
        }

        CachedServerMap? cached = null;
        string? cacheWarning = null;
        try
        {
            cached = mapCache.Get(serverId);
        }
        catch (InvalidDataException)
        {
            cacheWarning = "The previous cached map was invalid and will be replaced by a live download.";
        }
        if (!forceRefresh && cached is not null)
        {
            SetCachedState(cached, null);
            return;
        }

        var profile = servers.Profiles.First(profile => profile.Id == serverId);
        SetState(MapDashboardState.NotStarted with
        {
            ConnectionState = DashboardConnectionState.Loading,
            ConnectionLabel = "Loading live map",
            ServerId = serverId,
            ErrorMessage = null
        });

        RustPlusMapLoadResult result;
        try
        {
            result = await connections.LoadMapAsync(serverId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var safeMessage = SecretRedactor.Redact(exception.Message);
            if (cached is not null)
            {
                SetCachedState(cached, $"Live refresh failed; showing the cached map. {safeMessage}");
                return;
            }

            SetFailedState(serverId, "Live map failed", safeMessage);
            return;
        }

        if (!result.IsSuccess || result.ServerInfo is null || result.Map is null)
        {
            var failure = result.ConnectionState.Detail ?? result.ConnectionState.Label;
            if (cached is not null)
            {
                SetCachedState(cached, $"Live refresh failed; showing the cached map. {failure}");
                return;
            }

            SetFailedState(serverId, result.ConnectionState.Label, failure);
            return;
        }

        var retrievedAtUtc = timeProvider.GetUtcNow();
        var saved = new CachedServerMap(serverId, retrievedAtUtc, result.ServerInfo, result.Map);
        try
        {
            mapCache.Upsert(saved);
        }
        catch (Exception exception)
        {
            cacheWarning = $"The live map loaded, but its offline cache could not be updated ({exception.GetType().Name}).";
        }
        SetState(new MapDashboardState(
            DashboardConnectionState.Ready,
            profile.UseFacepunchProxy ? "Live map · secure proxy" : "Live map · direct",
            MapDashboardDataSource.Live,
            serverId,
            retrievedAtUtc,
            result.ServerInfo,
            result.Map,
            null,
            null,
            null,
            MapDashboardState.CreateLiveMapLayers(),
            cacheWarning));
    }

    private async Task LoadDemoAsync(CancellationToken cancellationToken)
    {
        SetState(Current with
        {
            ConnectionState = DashboardConnectionState.Loading,
            ConnectionLabel = "Loading demo session",
            DataSource = MapDashboardDataSource.Fake,
            ErrorMessage = null
        });

        try
        {
            await demoClient.ConnectAsync(demoConnectionOptions, cancellationToken).ConfigureAwait(false);

            var server = Require(
                await demoClient.GetServerInfoAsync(cancellationToken).ConfigureAwait(false),
                "server information");
            var map = Require(
                await demoClient.GetMapAsync(cancellationToken).ConfigureAwait(false),
                "map");
            var team = Require(
                await demoClient.GetTeamAsync(cancellationToken).ConfigureAwait(false),
                "team information");
            var chat = Require(
                await demoClient.GetTeamChatAsync(cancellationToken).ConfigureAwait(false),
                "team chat");
            var markers = Require(
                await demoClient.GetMapMarkersAsync(cancellationToken).ConfigureAwait(false),
                "map markers");

            SetState(new MapDashboardState(
                DashboardConnectionState.Ready,
                "Demo connected",
                MapDashboardDataSource.Fake,
                null,
                timeProvider.GetUtcNow(),
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

    private void SetCachedState(CachedServerMap cached, string? warning)
    {
        SetState(new MapDashboardState(
            DashboardConnectionState.Ready,
            "Cached map · offline ready",
            MapDashboardDataSource.Cache,
            cached.ServerId,
            cached.RetrievedAtUtc,
            cached.Server,
            cached.Map,
            null,
            null,
            null,
            MapDashboardState.CreateLiveMapLayers(),
            warning));
    }

    private void SetFailedState(Guid serverId, string label, string detail)
    {
        SetState(MapDashboardState.NotStarted with
        {
            ConnectionState = DashboardConnectionState.Failed,
            ConnectionLabel = label,
            ServerId = serverId,
            ErrorMessage = SecretRedactor.Redact(detail)
        });
    }

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
