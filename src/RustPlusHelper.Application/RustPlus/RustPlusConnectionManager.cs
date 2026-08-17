using System.Buffers.Text;
using System.Security.Cryptography;
using RustPlusHelper.Application.Security;
using RustPlusHelper.Application.Servers;

namespace RustPlusHelper.Application.RustPlus;

/// <summary>
/// Builds a short-lived, authenticated Rust+ lifecycle from a saved server profile. A successful
/// socket handshake is validated with the low-cost read-only server-information request before the
/// test is reported as successful.
/// </summary>
public sealed class RustPlusConnectionManager(
    ServerManager servers,
    ISecretStore secretStore,
    IRustPlusClientFactory clientFactory,
    TimeProvider timeProvider) : IDisposable
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);
    private readonly Lock _stateLock = new();
    private readonly Dictionary<Guid, RustPlusConnectionState> _states = [];
    private readonly SemaphoreSlim _testLock = new(1, 1);
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

    public async Task<RustPlusConnectionState> TestConnectionAsync(
        Guid serverId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _testLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var profile = servers.Profiles.FirstOrDefault(candidate => candidate.Id == serverId);
            if (profile is null)
            {
                return SetState(new RustPlusConnectionState(
                    serverId,
                    RustPlusConnectionStatus.Failed,
                    "Server not found",
                    "The saved server profile no longer exists.",
                    CheckedAtUtc: timeProvider.GetUtcNow()));
            }

            if (profile.PlayerId is null or 0)
            {
                return PairingRequired(serverId, "Save your Steam64 identity before testing this server.");
            }

            var tokenBytes = secretStore.Retrieve(serverId, SecretKind.RustPlusPlayerToken);
            if (tokenBytes is null)
            {
                return PairingRequired(serverId, "Enter the player token supplied by this server pairing.");
            }

            try
            {
                if (!Utf8Parser.TryParse(tokenBytes, out int playerToken, out var consumed)
                    || consumed != tokenBytes.Length)
                {
                    return PairingRequired(serverId, "The protected pairing token is invalid. Re-pair this server.");
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
                timeout.CancelAfter(TestTimeout);

                try
                {
                    await using var client = clientFactory.Create();
                    await client.ConnectAsync(options, timeout.Token).ConfigureAwait(false);

                    SetState(new RustPlusConnectionState(
                        serverId,
                        RustPlusConnectionStatus.Authenticating,
                        "Authenticating",
                        "The socket is open; validating the saved pairing."));

                    var serverInfo = await client.GetServerInfoAsync(timeout.Token).ConfigureAwait(false);
                    if (serverInfo.IsSuccess && serverInfo.Data is not null)
                    {
                        return SetState(new RustPlusConnectionState(
                            serverId,
                            RustPlusConnectionStatus.Succeeded,
                            "Connection verified",
                            "Authenticated server information was received; the test socket was then closed.",
                            serverInfo.Data,
                            timeProvider.GetUtcNow()));
                    }

                    return SetState(ClassifyProtocolFailure(serverId, serverInfo.Error));
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return SetState(new RustPlusConnectionState(
                        serverId,
                        RustPlusConnectionStatus.TimedOut,
                        "Connection timed out",
                        "The Rust+ companion server did not complete the test within 30 seconds.",
                        CheckedAtUtc: timeProvider.GetUtcNow()));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (RustPlusConnectionException exception)
                {
                    return SetState(new RustPlusConnectionState(
                        serverId,
                        RustPlusConnectionStatus.Failed,
                        "Connection failed",
                        exception.Message,
                        CheckedAtUtc: timeProvider.GetUtcNow()));
                }
                catch (Exception exception)
                {
                    return SetState(new RustPlusConnectionState(
                        serverId,
                        RustPlusConnectionStatus.Failed,
                        "Connection failed",
                        $"The Rust+ companion endpoint could not be reached ({exception.GetType().Name}).",
                        CheckedAtUtc: timeProvider.GetUtcNow()));
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(tokenBytes);
            }
        }
        finally
        {
            _testLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
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
}
