namespace RustPlusHelper.Application.Pairing;

/// <summary>
/// <paramref name="RustPlusServerId"/> is Rust+'s own server GUID (confirmed via
/// <c>RustPlusApi.Fcm.Data.Events.ServerEvent.Id</c>, doc-commented "The server's unique Rust+ ID")
/// — distinct from this app's own <c>ServerProfile.Id</c>. It's the only way to attribute a later
/// alarm-triggered push to the right saved server (see docs/protocol-evidence.md).
/// </summary>
public sealed record CapturedRustPlusPairing(
    string Host,
    int Port,
    ulong PlayerId,
    int PlayerToken,
    string? ServerName,
    Guid RustPlusServerId);

/// <summary>Mirrors the verified Rust+ FCM entity-pairing notification's <c>entityType</c>
/// (Switch/Alarm/StorageMonitor) — see docs/protocol-evidence.md.</summary>
public enum PairedEntityKind
{
    Switch = 1,
    Alarm = 2,
    StorageMonitor = 3
}

public sealed record CapturedEntityPairing(
    ulong PlayerId,
    int PlayerToken,
    ulong EntityId,
    PairedEntityKind Kind,
    string? EntityName);

/// <summary>Application-owned boundary for Rust+ device registration and server/entity pairing.</summary>
public interface IRustPlusPairingProvider
{
    /// <summary>Returns caller-owned serialized credentials that must be protected and zeroed.</summary>
    Task<byte[]> RegisterAsync(CancellationToken cancellationToken = default);

    Task<CapturedRustPlusPairing> WaitForServerPairingAsync(
        ReadOnlyMemory<byte> credentials,
        CancellationToken cancellationToken = default);

    /// <summary>Waits for the next Smart Switch/Alarm/Storage Monitor pairing notification. Unlike
    /// server pairing, the notification carries no host/port — the caller already knows which saved
    /// server this pairing belongs to (the one currently open).</summary>
    Task<CapturedEntityPairing> WaitForEntityPairingAsync(
        ReadOnlyMemory<byte> credentials,
        CancellationToken cancellationToken = default);
}
