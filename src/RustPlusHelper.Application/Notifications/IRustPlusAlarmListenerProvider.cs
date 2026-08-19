namespace RustPlusHelper.Application.Notifications;

/// <summary>A Smart Alarm "triggered" FCM push. <see cref="RustPlusServerId"/> is Rust+'s own server
/// GUID (see <see cref="Pairing.CapturedRustPlusPairing.RustPlusServerId"/>) — match it against a
/// saved <c>ServerProfile.RustPlusServerId</c> to attribute the alarm to a specific server.
/// <see cref="PersistentId"/> is the underlying FCM message's de-duplication id.</summary>
public sealed record AlarmTriggeredCapture(
    Guid RustPlusServerId,
    string Title,
    string Message,
    string? PersistentId);

/// <summary>Application-owned boundary for the persistent Smart Alarm push-notification connection.
/// Unlike server/entity pairing (one notification, then disconnect), this stays connected for the
/// app's lifetime — <see cref="RunAsync"/> represents ONE connection attempt: it connects, then waits
/// until the connection ends for any reason (disconnect, error, or cancellation) before returning.
/// The caller owns retrying with backoff across attempts, mirroring how
/// <c>RustPlusLiveSessionManager</c> owns reconnect for the main Rust+ connection.</summary>
public interface IRustPlusAlarmListenerProvider
{
    /// <summary>Runs one FCM connection attempt for alarm-triggered pushes.</summary>
    /// <param name="credentials">Serialized FCM credentials (see <c>ApplicationSecretKind.RustPlusFcmCredentials</c>).</param>
    /// <param name="seedPersistentIds">Already-seen FCM persistent ids from a prior run, used for
    /// this connection's built-in de-duplication. Never mutated by the caller after this call starts —
    /// the provider owns a private copy.</param>
    /// <param name="onAlarmTriggered">Invoked for each triggered-alarm push received.</param>
    /// <param name="onPersistentIdReceived">Invoked with each newly harvested persistent id as it
    /// arrives, so the caller can persist it for future de-duplication.</param>
    Task RunAsync(
        ReadOnlyMemory<byte> credentials,
        IReadOnlyCollection<string> seedPersistentIds,
        Action<AlarmTriggeredCapture> onAlarmTriggered,
        Action<string> onPersistentIdReceived,
        CancellationToken cancellationToken = default);
}
