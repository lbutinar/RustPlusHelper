namespace RustPlusHelper.Application.RustPlus;

public sealed record ServerInfoSnapshot(
    string? Name,
    string? HeaderImageUrl,
    string? WebsiteUrl,
    string? MapName,
    uint? MapSize,
    DateTimeOffset? WipeTimeUtc,
    uint? PlayerCount,
    uint? MaxPlayerCount,
    uint? QueuedPlayerCount,
    uint? Seed,
    uint? Salt,
    string? LogoImageUrl,
    string? Nexus,
    int? NexusId,
    string? NexusZone);

public sealed record MapMonumentSnapshot(string? TokenOrName, float? X, float? Y);

public sealed record ServerMapSnapshot(
    uint? Width,
    uint? Height,
    int? OceanMargin,
    string BackgroundArgb,
    IReadOnlyList<MapMonumentSnapshot> Monuments,
    byte[] JpegImage);

public sealed record TeamMemberSnapshot(
    ulong SteamId,
    string? Name,
    float X,
    float Y,
    bool IsOnline,
    bool IsAlive,
    DateTimeOffset LastSpawnTimeUtc,
    DateTimeOffset LastDeathTimeUtc);

public sealed record TeamNoteSnapshot(float X, float Y, string? Text, int Icon, int Color);

public sealed record TeamSnapshot(
    ulong LeaderSteamId,
    IReadOnlyList<TeamMemberSnapshot> Members,
    IReadOnlyList<TeamNoteSnapshot> Notes,
    IReadOnlyList<TeamNoteSnapshot> LeaderNotes,
    MapPositionSnapshot? LeaderDeathPosition);

public sealed record TeamChatMessageSnapshot(
    ulong SteamId,
    string Name,
    string Message,
    string ColorArgb,
    DateTimeOffset SentAtUtc);

public sealed record TeamChatSnapshot(IReadOnlyList<TeamChatMessageSnapshot> Messages);

public sealed record MapPositionSnapshot(float X, float Y);

public enum MapMarkerKind
{
    Player,
    Explosion,
    VendingMachine,
    Ch47,
    CargoShip,
    Crate,
    GenericRadius,
    PatrolHelicopter,
    TravellingVendor,
    Unknown
}

public sealed record VendingOrderSnapshot(
    int ItemId,
    int Quantity,
    int CurrencyId,
    int Cost,
    int Stock,
    bool IsItemBlueprint,
    bool IsCurrencyBlueprint,
    float ItemCondition,
    float ItemMaxCondition,
    float? PriceMultiplier,
    float? ReceivedQuantityMultiplier);

public sealed record MapMarkerSnapshot(
    ulong? Id,
    MapMarkerKind Kind,
    float? X,
    float? Y,
    int? RawType = null,
    string? Name = null,
    ulong? SteamId = null,
    float? Rotation = null,
    float? Radius = null,
    bool? IsOutOfStock = null,
    IReadOnlyList<VendingOrderSnapshot>? VendingOrders = null);

public sealed record MapMarkersSnapshot(IReadOnlyList<MapMarkerSnapshot> Markers);
