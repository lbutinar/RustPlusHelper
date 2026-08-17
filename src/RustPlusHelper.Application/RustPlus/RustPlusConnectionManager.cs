using System.Buffers.Text;
using System.Security.Cryptography;
using RustPlusHelper.Application.Security;
using RustPlusHelper.Application.Servers;

namespace RustPlusHelper.Application.RustPlus;

/// <summary>
/// Owns serialized, short-lived authenticated Rust+ operations for saved server profiles. Socket
/// lifetime and cleartext pairing data never cross into UI components.
/// </summary>
public sealed class RustPlusConnectionManager(
    ServerManager servers,
    ISecretStore secretStore,
    IRustPlusClientFactory clientFactory,
    TimeProvider timeProvider) : IDisposable
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DashboardLoadTimeout = TimeSpan.FromSeconds(60);
    private readonly Lock _stateLock = new();
    private readonly Dictionary<Guid, RustPlusConnectionState> _states = [];
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private bool _disposed;

    public event EventHandler? StateChanged;

    public RustPlusConnectionState GetState(Guid serverId)
    {
        lock (_stateLock)
        {
            return _states.TryGetValue(serverId, out var state)
                ? state
                : RustPlusConnectionState.NotTested(serverId);
        }
    }

    public void Reset(Guid serverId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        bool removed;
        lock (_stateLock)
        {
            removed = _states.Remove(serverId);
        }

        if (removed)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Task<RustPlusConnectionState> TestConnectionAsync(
        Guid serverId,
        CancellationToken cancellationToken = default) =>
        ExecuteAuthenticatedAsync(
            serverId,
            TestTimeout,
            "Authenticating",
            "The socket is open; validating the saved pairing.",
            (client, serverInfo, profile, token) => Task.FromResult(SetState(new RustPlusConnectionState(
                serverId,
                RustPlusConnectionStatus.Succeeded,
                "Connection verified",
                "Authenticated server information was received; the test socket was then closed.",
                serverInfo,
                timeProvider.GetUtcNow()))),
            state => state,
            "Connection timed out",
            "The Rust+ companion server did not complete the test within 30 seconds.",
            cancellationToken);

    /// <summary>
    /// Loads server information, optional map data, team state, recent team chat, and map markers on
    /// one connection. Info authenticates the session; later request failures are returned separately.
    /// </summary>
    public Task<RustPlusDashboardLoadResult> LoadDashboardAsync(
        Guid serverId,
        bool includeMap,
        CancellationToken cancellationToken = default) =>
        ExecuteAuthenticatedAsync(
            serverId,
            DashboardLoadTimeout,
            "Refreshing Rust+ data",
            includeMap
                ? "Requesting the current map, team, chat, and map-marker snapshots."
                : "Requesting current team, chat, and map-marker snapshots.",
            async (client, serverInfo, profile, token) =>
            {
                RustPlusResult<ServerMapSnapshot>? map = null;
                if (includeMap)
                {
                    map = ValidateMap(serverInfo, await client.GetMapAsync(token).ConfigureAwait(false));
                }

                var team = await client.GetTeamAsync(token).ConfigureAwait(false);
                var chat = await client.GetTeamChatAsync(token).ConfigureAwait(false);
                var markers = await client.GetMapMarkersAsync(token).ConfigureAwait(false);
                var unavailable = DescribeUnavailable(map, team, chat, markers, includeMap);
                var state = SetState(new RustPlusConnectionState(
                    serverId,
                    RustPlusConnectionStatus.Succeeded,
                    unavailable.Count == 0 ? "Live data refreshed" : "Live data partially available",
                    unavailable.Count == 0
                        ? "The requested Rust+ snapshots were received; the socket was then closed."
                        : $"Unavailable requests: {string.Join(", ", unavailable)}. The socket was then closed.",
                    serverInfo,
                    timeProvider.GetUtcNow()));

                return new RustPlusDashboardLoadResult(state, serverInfo, map, team, chat, markers);
            },
            state => new RustPlusDashboardLoadResult(state),
            "Live refresh timed out",
            "The Rust+ companion server did not complete the refresh within 60 seconds.",
            cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _operationLock.Dispose();
    }

    private async Task<T> ExecuteAuthenticatedAsync<T>(
        Guid serverId,
        TimeSpan timeoutDuration,
        string authenticatedLabel,
        string authenticatedDetail,
        Func<IRustPlusClient, ServerInfoSnapshot, ServerProfile, CancellationToken, Task<T>> operation,
        Func<RustPlusConnectionState, T> failure,
        string timeoutLabel,
        string timeoutDetail,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var profile = servers.Profiles.FirstOrDefault(candidate => candidate.Id == serverId);
            if (profile is null)
            {
                return failure(SetState(new RustPlusConnectionState(
                    serverId,
                    RustPlusConnectionStatus.Failed,
                    "Server not found",
                    "The saved server profile no longer exists.",
                    CheckedAtUtc: timeProvider.GetUtcNow())));
            }

            if (profile.PlayerId is null or 0)
            {
                return failure(PairingRequired(
                    serverId,
                    "Save your Steam64 identity before connecting to this server."));
            }

            var tokenBytes = secretStore.Retrieve(serverId, SecretKind.RustPlusPlayerToken);
            if (tokenBytes is null)
            {
                return failure(PairingRequired(
                    serverId,
                    "Enter the player token supplied by this server pairing."));
            }

            try
            {
                if (!Utf8Parser.TryParse(tokenBytes, out int playerToken, out var consumed)
                    || consumed != tokenBytes.Length)
                {
                    return failure(PairingRequired(
                        serverId,
                        "The protected pairing token is invalid. Re-pair this server."));
                }

                var options = new RustPlusConnectionOptions(
                    profile.Host,
                    profile.Port,
                    profile.PlayerId.Value,
                    playerToken,
                    profile.UseFacepunchProxy);

                SetState(new RustPlusConnectionState(
                    serverId,
                    RustPlusConnectionStatus.Connecting,
                    "Connecting",
                    profile.UseFacepunchProxy
                        ? "Opening the Facepunch secure-proxy WebSocket."
                        : "Opening the explicitly enabled direct plaintext WebSocket."));

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(timeoutDuration);

                try
                {
                    await using var client = clientFactory.Create();
                    await client.ConnectAsync(options, timeout.Token).ConfigureAwait(false);

                    SetState(new RustPlusConnectionState(
                        serverId,
                        RustPlusConnectionStatus.Authenticating,
                        authenticatedLabel,
                        authenticatedDetail));

                    var serverInfo = await client.GetServerInfoAsync(timeout.Token).ConfigureAwait(false);
                    if (!serverInfo.IsSuccess || serverInfo.Data is null)
                    {
                        return failure(SetState(ClassifyProtocolFailure(serverId, serverInfo.Error)));
                    }

                    return await operation(client, serverInfo.Data, profile, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return failure(SetState(new RustPlusConnectionState(
                        serverId,
                        RustPlusConnectionStatus.TimedOut,
                        timeoutLabel,
                        timeoutDetail,
                        CheckedAtUtc: timeProvider.GetUtcNow())));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (RustPlusConnectionException exception)
                {
                    return failure(SetState(new RustPlusConnectionState(
                        serverId,
                        RustPlusConnectionStatus.Failed,
                        "Connection failed",
                        exception.Message,
                        CheckedAtUtc: timeProvider.GetUtcNow())));
                }
                catch (Exception exception)
                {
                    return failure(SetState(new RustPlusConnectionState(
                        serverId,
                        RustPlusConnectionStatus.Failed,
                        "Rust+ refresh failed",
                        $"The Rust+ data could not be loaded ({exception.GetType().Name}).",
                        CheckedAtUtc: timeProvider.GetUtcNow())));
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(tokenBytes);
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private RustPlusConnectionState PairingRequired(Guid serverId, string detail) =>
        SetState(new RustPlusConnectionState(
            serverId,
            RustPlusConnectionStatus.PairingRequired,
            "Pairing required",
            detail,
            CheckedAtUtc: timeProvider.GetUtcNow()));

    private RustPlusConnectionState ClassifyProtocolFailure(Guid serverId, RustPlusError? error)
    {
        var code = error?.Code ?? "unknown_error";
        if (code.Equals("AccessDenied", StringComparison.OrdinalIgnoreCase)
            || code.Equals("access_denied", StringComparison.OrdinalIgnoreCase))
        {
            return new RustPlusConnectionState(
                serverId,
                RustPlusConnectionStatus.AuthenticationRejected,
                "Pairing rejected",
                "The server rejected this player token. Re-pair the server and replace its saved token.",
                CheckedAtUtc: timeProvider.GetUtcNow());
        }

        return new RustPlusConnectionState(
            serverId,
            RustPlusConnectionStatus.Failed,
            "Rust+ request failed",
            $"The authenticated information request failed ({code}).",
            CheckedAtUtc: timeProvider.GetUtcNow());
    }

    private RustPlusConnectionState SetState(RustPlusConnectionState state)
    {
        lock (_stateLock)
        {
            _states[state.ServerId] = state;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        return state;
    }

    private static RustPlusResult<ServerMapSnapshot> ValidateMap(
        ServerInfoSnapshot serverInfo,
        RustPlusResult<ServerMapSnapshot> map)
    {
        if (!map.IsSuccess || map.Data is null)
        {
            return map;
        }

        if (map.Data.JpegImage.Length == 0)
        {
            return RustPlusResult<ServerMapSnapshot>.Failure("empty_map_image", "Rust+ returned an empty map image.");
        }

        if (serverInfo.MapSize is null or 0
            || map.Data.Width is null or 0
            || map.Data.Height is null or 0
            || map.Data.OceanMargin is null)
        {
            return RustPlusResult<ServerMapSnapshot>.Failure(
                "incomplete_map_metadata",
                "Rust+ returned the image without the dimensions required to render it.");
        }

        return map;
    }

    private static IReadOnlyList<string> DescribeUnavailable(
        RustPlusResult<ServerMapSnapshot>? map,
        RustPlusResult<TeamSnapshot> team,
        RustPlusResult<TeamChatSnapshot> chat,
        RustPlusResult<MapMarkersSnapshot> markers,
        bool includeMap)
    {
        var unavailable = new List<string>();
        AddFailure(unavailable, "map", map, includeMap);
        AddFailure(unavailable, "team", team, required: true);
        AddFailure(unavailable, "chat", chat, required: true);
        AddFailure(unavailable, "markers", markers, required: true);
        return unavailable;
    }

    private static void AddFailure<T>(
        ICollection<string> unavailable,
        string name,
        RustPlusResult<T>? result,
        bool required)
    {
        if (!required || result?.IsSuccess == true)
        {
            return;
        }

        unavailable.Add($"{name} ({result?.Error?.Code ?? "unknown_error"})");
    }
}
