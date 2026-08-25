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
    AlarmTriggered,
    OilRigActivated
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

/// <summary>One persisted, timestamped position sample on a team member's movement trail. The
/// timestamp lets rendering filter a member's trail to positions at or after the server's last wipe,
/// the same convention already used for the team-death hotspot layer.</summary>
public sealed record MovementTrailPoint(float X, float Y, DateTimeOffset SampledAtUtc);

public sealed record RustPlusLiveSessionSeed(
    ServerInfoSnapshot? Server = null,
    TeamSnapshot? Team = null,
    TeamChatSnapshot? Chat = null,
    MapMarkersSnapshot? Markers = null,
    DateTimeOffset? RetrievedAtUtc = null,
    /// <summary>The cached map's monuments, if already known — used only to power the oil-rig
    /// activation heuristic in <see cref="RustPlusLiveSessionManager"/>. Rust+'s live polling never
    /// re-fetches the map, so this is seeded once at session start rather than kept live.</summary>
    IReadOnlyList<MapMonumentSnapshot>? Monuments = null);

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
    IReadOnlyList<CompanionEvent> Events,
    /// <summary>Persisted, downsampled positions per team member seen this server, oldest first —
    /// loaded from <see cref="IMovementTrailRepository"/> at session start and appended to live as new
    /// samples are recorded. Survives app restarts and a member going offline; rendering is expected
    /// to filter to positions at or after the server's last wipe, like the death-hotspot layer.</summary>
    IReadOnlyDictionary<ulong, IReadOnlyList<MovementTrailPoint>> MovementTrails,
    /// <summary>Seeded once at session start (see <see cref="RustPlusLiveSessionSeed.Monuments"/>);
    /// used only to detect a crate appearing near a known oil rig.</summary>
    IReadOnlyList<MapMonumentSnapshot> Monuments,
    /// <summary>Populated only by an explicit <see cref="RustPlusLiveSessionManager.RefreshClanChatAsync"/>
    /// call, never by background polling — most players are not in a clan, so continuously polling
    /// clan chat the way team chat is polled would spend request budget on an empty result for the
    /// common case.</summary>
    ClanChatSnapshot? ClanChat = null)
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
        [],
        new Dictionary<ulong, IReadOnlyList<MovementTrailPoint>>(),
        []);
}
