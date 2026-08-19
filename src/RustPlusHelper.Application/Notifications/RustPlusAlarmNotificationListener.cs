using System.Security.Cryptography;
using System.Text;
using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Application.Security;
using RustPlusHelper.Application.Servers;

namespace RustPlusHelper.Application.Notifications;

/// <summary>A triggered-alarm push, ready for a notification dispatcher to show. <see cref="ServerId"/>
/// is this app's own saved <c>ServerProfile.Id</c> when the push could be attributed to a saved
/// server (matched by <see cref="Servers.ServerProfile.RustPlusServerId"/>); <see langword="null"/>
/// when it couldn't (e.g. the server was paired before that field was captured) — the user still gets
/// a toast with the raw title/message, just without a server link.</summary>
public sealed record AlarmToastNotification(Guid? ServerId, string Title, string Message);

/// <summary>
/// Owns one persistent FCM connection for the app's lifetime to receive Smart Alarm "triggered"
/// pushes, independent of which saved server's live session is currently active — an alarm can fire
/// for any paired server. See docs/protocol-evidence.md's "Smart devices" section for the verified
/// wire behavior this is built on.
/// </summary>
public sealed class RustPlusAlarmNotificationListener(
    IRustPlusAlarmListenerProvider provider,
    IApplicationSecretStore applicationSecrets,
    ServerManager servers,
    RustPlusLiveSessionManager liveSession,
    RustPlusPollingOptions pollingOptions) : IAsyncDisposable, IDisposable
{
    // Ids have no visible timestamp (opaque strings) and the library itself says pruning the
    // persisted copy is the caller's responsibility — cap by count, evict oldest-by-insertion-order.
    private const int MaxPersistedIds = 500;
    private static readonly char[] IdSeparator = ['\n'];

    private readonly Lock _idLock = new();
    private readonly List<string> _persistedOrder = [];
    private readonly HashSet<string> _persistedLookup = [];
    private readonly SemaphoreSlim _wakeSignal = new(0, 1);
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private bool _disposed;

    /// <summary>Raised for every triggered-alarm push, whether or not it could be matched to a saved
    /// server — the notification dispatcher decides how to present it either way.</summary>
    public event EventHandler<AlarmToastNotification>? AlarmTriggered;

    /// <summary>Starts the listener if FCM credentials are already registered; a no-op otherwise
    /// (mirrors <see cref="Pairing.RustPlusEntityPairingManager.Load"/>'s "not configured" shape,
    /// except this listener has no user-visible state to report — it either runs silently in the
    /// background or doesn't).</summary>
    public void Load()
    {
        if (_runTask is not null || !applicationSecrets.Contains(ApplicationSecretKind.RustPlusFcmCredentials))
        {
            return;
        }

        LoadPersistedIds();
        _runCancellation = new CancellationTokenSource();
        _runTask = RunAsync(_runCancellation.Token);
    }

    /// <summary>Interrupts the current backoff wait so a reconnect is attempted immediately — used on
    /// OS resume-from-sleep, mirroring <see cref="RustPlusLiveSessionManager.RequestRefresh"/> for the
    /// main connection (which this listener does not share, so it needs its own wake).</summary>
    public void RequestReconnect()
    {
        if (_wakeSignal.CurrentCount == 0)
        {
            _wakeSignal.Release();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var credentials = applicationSecrets.Retrieve(ApplicationSecretKind.RustPlusFcmCredentials);
            if (credentials is null)
            {
                return;
            }

            try
            {
                IReadOnlyCollection<string> seed;
                lock (_idLock)
                {
                    seed = _persistedOrder.ToArray();
                }

                await provider.RunAsync(
                    credentials,
                    seed,
                    HandleAlarmTriggered,
                    HandlePersistentIdReceived,
                    cancellationToken).ConfigureAwait(false);
                attempt = 0;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // Swallowed: the connection attempt failed or ended in error. The bounded backoff
                // below retries — there is no user-facing surface to report this failure to (unlike
                // the main live session, this listener has no dedicated status UI).
            }
            finally
            {
                CryptographicOperations.ZeroMemory(credentials);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var delays = pollingOptions.ReconnectDelays;
            var delay = delays[Math.Min(attempt, delays.Count - 1)];
            attempt++;
            await WaitForRetryAsync(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WaitForRetryAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await _wakeSignal.WaitAsync(delay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is checked by the caller's loop condition immediately after.
        }
    }

    private void HandleAlarmTriggered(AlarmTriggeredCapture capture)
    {
        var serverId = servers.Profiles
            .FirstOrDefault(profile => profile.RustPlusServerId == capture.RustPlusServerId)?.Id;
        if (serverId is { } matchedServerId)
        {
            liveSession.RecordExternalEvent(
                matchedServerId,
                CompanionEventKind.AlarmTriggered,
                capture.Title,
                capture.Message);
        }

        AlarmTriggered?.Invoke(this, new AlarmToastNotification(serverId, capture.Title, capture.Message));
    }

    private void HandlePersistentIdReceived(string id)
    {
        lock (_idLock)
        {
            if (!_persistedLookup.Add(id))
            {
                return;
            }

            _persistedOrder.Add(id);
            while (_persistedOrder.Count > MaxPersistedIds)
            {
                var oldest = _persistedOrder[0];
                _persistedOrder.RemoveAt(0);
                _persistedLookup.Remove(oldest);
            }

            SavePersistedIds();
        }
    }

    private void LoadPersistedIds()
    {
        var stored = applicationSecrets.Retrieve(ApplicationSecretKind.AlarmFcmPersistentIds);
        if (stored is null || stored.Length == 0)
        {
            return;
        }

        var ids = Encoding.UTF8.GetString(stored).Split(IdSeparator, StringSplitOptions.RemoveEmptyEntries);
        lock (_idLock)
        {
            foreach (var id in ids)
            {
                if (_persistedLookup.Add(id))
                {
                    _persistedOrder.Add(id);
                }
            }
        }
    }

    /// <summary>Called with <see cref="_idLock"/> already held.</summary>
    private void SavePersistedIds()
    {
        var encoded = Encoding.UTF8.GetBytes(string.Join('\n', _persistedOrder));
        applicationSecrets.Store(ApplicationSecretKind.AlarmFcmPersistentIds, encoded);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _runCancellation?.Cancel();
        try
        {
            _runTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }

        _runCancellation?.Dispose();
        _wakeSignal.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _runCancellation?.Cancel();
        if (_runTask is not null)
        {
            try
            {
                await _runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _runCancellation?.Dispose();
        _wakeSignal.Dispose();
    }
}
