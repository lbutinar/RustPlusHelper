namespace RustPlusHelper.Application.RustPlus;

public enum RustPlusLiveSessionStatus
{
    Stopped,
    Connecting,
    Connected,
    Reconnecting,
    PairingRequired,
    AuthenticationRejected
}

public enum CompanionEventKind
{
    ConnectionEstablished,
    ConnectionLost,
    ConnectionRestored,
    TeamMemberConnected,
    TeamMemberDisconnected,
    TeamMemberDied,
    TeamMemberRespawned,
    TeamMemberChangedGrid,
    MarkerAppeared,
    MarkerDisappeared
}

public enum CompanionEventSource
{
    Transport,
    SnapshotDiff
}

public sealed record CompanionEvent(
    Guid Id,
    Guid ServerId,
    DateTimeOffset OccurredAtUtc,
    CompanionEventKind Kind,
    CompanionEventSource Source,
    string Title,
    string? Detail = null);

public sealed record RustPlusLiveSessionSeed(
    ServerInfoSnapshot? Server = null,
    TeamSnapshot? Team = null,
    TeamChatSnapshot? Chat = null,
    MapMarkersSnapshot? Markers = null,
    DateTimeOffset? RetrievedAtUtc = null);

public sealed record RustPlusLiveSessionState(
    Guid? ServerId,
    RustPlusLiveSessionStatus Status,
    string Label,
    ServerInfoSnapshot? Server,
    TeamSnapshot? Team,
    TeamChatSnapshot? Chat,
    MapMarkersSnapshot? Markers,
    DateTimeOffset? LastRefreshUtc,
    string? Error,
    IReadOnlyList<CompanionEvent> Events)
{
    public static RustPlusLiveSessionState Stopped { get; } = new(
        null,
        RustPlusLiveSessionStatus.Stopped,
        "Background monitoring stopped",
        null,
        null,
        null,
        null,
        null,
        null,
        []);
}
