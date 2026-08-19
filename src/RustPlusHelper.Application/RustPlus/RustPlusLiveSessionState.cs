using RustPlusHelper.Application.Pairing;

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
    MarkerDisappeared,
    VendingPriceChanged,
    VendingStockChanged,
    VendingOfferAdded,
    VendingOfferRemoved,
    AlarmTriggered
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
    string? Detail = null,
    MapPositionSnapshot? Position = null);

public enum CameraSessionStatus
{
    Inactive,
    Subscribing,
    Active,
    Failed
}

/// <summary>Camera-viewing state, independent of team/chat/marker polling above — a camera is
/// only ever viewed on explicit user action, never on a background timer.</summary>
public sealed record CameraSessionState(
    CameraSessionStatus Status,
    string? CameraCode,
    CameraInfoSnapshot? Info,
    CameraFrameSnapshot? LatestFrame,
    string? Error)
{
    public static CameraSessionState Inactive { get; } = new(CameraSessionStatus.Inactive, null, null, null, null);
}

/// <summary>
/// Live state for one paired Smart Switch/Alarm/Storage Monitor. <see cref="Kind"/> comes from how
/// the entity was paired, not from the broadcast payload — Rust+ broadcasts carry no entity type of
/// their own, so this is the only reliable way to know whether to read <see cref="Value"/>
/// (Switch/Alarm) or <see cref="Capacity"/>/<see cref="Items"/> (Storage Monitor).
/// </summary>
public sealed record PairedEntityLiveState(
    ulong EntityId,
    PairedEntityKind Kind,
    bool? Value,
    int? Capacity,
    bool? HasProtection,
    IReadOnlyList<StorageItemSnapshot> Items,
    string? Error);

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
