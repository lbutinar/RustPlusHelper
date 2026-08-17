namespace RustPlusHelper.Application.RustPlus;

public enum RustPlusConnectionStatus
{
    NotTested,
    Connecting,
    Authenticating,
    Succeeded,
    PairingRequired,
    AuthenticationRejected,
    TimedOut,
    Failed
}

public sealed record RustPlusConnectionState(
    Guid ServerId,
    RustPlusConnectionStatus Status,
    string Label,
    string? Detail = null,
    ServerInfoSnapshot? ServerInfo = null,
    DateTimeOffset? CheckedAtUtc = null)
{
    public bool IsInProgress => Status is RustPlusConnectionStatus.Connecting
        or RustPlusConnectionStatus.Authenticating;

    public static RustPlusConnectionState NotTested(Guid serverId) =>
        new(serverId, RustPlusConnectionStatus.NotTested, "Not tested");
}
