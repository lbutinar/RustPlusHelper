using RustPlusHelper.Application.RustPlus;

namespace RustPlusHelper.Application.Map;

public enum DashboardConnectionState
{
    NotStarted,
    Loading,
    Ready,
    Failed
}

public enum MapDashboardDataSource
{
    None,
    Fake,
    Live,
    Cache
}

public enum MapLayerKind
{
    BaseMap,
    Team,
    TeamNotes,
    VendingMachines,
    Monuments,
    Events,
    SmartDevices,
    Cameras
}

public sealed record MapLayerState(
    MapLayerKind Kind,
    string DisplayName,
    bool IsVisible,
    bool IsAvailable,
    string SourceLabel,
    string? UnavailableReason = null);

public sealed record MapDashboardState(
    DashboardConnectionState ConnectionState,
    string ConnectionLabel,
    MapDashboardDataSource DataSource,
    Guid? ServerId,
    DateTimeOffset? MapRetrievedAtUtc,
    ServerInfoSnapshot? Server,
    ServerMapSnapshot? Map,
    TeamSnapshot? Team,
    TeamChatSnapshot? Chat,
    MapMarkersSnapshot? Markers,
    IReadOnlyList<MapLayerState> Layers,
    string? ErrorMessage)
{
    public static MapDashboardState NotStarted { get; } = new(
        DashboardConnectionState.NotStarted,
        "Not started",
        MapDashboardDataSource.None,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        CreateDefaultLayers(),
        null);

    public static IReadOnlyList<MapLayerState> CreateDefaultLayers() =>
    [
        new(MapLayerKind.BaseMap, "Base map", true, true, "DIRECT"),
        new(MapLayerKind.Team, "Team", true, true, "DIRECT"),
        new(MapLayerKind.TeamNotes, "Team notes", true, true, "DIRECT"),
        new(MapLayerKind.VendingMachines, "Vending", true, true, "DIRECT"),
        new(MapLayerKind.Monuments, "Monuments", true, true, "DIRECT"),
        new(MapLayerKind.Events, "World events", true, true, "DIRECT + DIFF"),
        new(
            MapLayerKind.SmartDevices,
            "Smart devices",
            false,
            false,
            "MANUAL",
            "Rust+ does not provide device positions."),
        new(
            MapLayerKind.Cameras,
            "CCTV",
            false,
            false,
            "EXTERNAL",
            "Camera codes and positions require user or catalogue data.")
    ];

    public static IReadOnlyList<MapLayerState> CreateLiveMapLayers() =>
    [
        new(MapLayerKind.BaseMap, "Base map", true, true, "DIRECT RUST+"),
        new(
            MapLayerKind.Team,
            "Team",
            false,
            false,
            "PHASE 5",
            "Live team polling is not enabled yet."),
        new(
            MapLayerKind.TeamNotes,
            "Team notes",
            false,
            false,
            "PHASE 5",
            "Live team notes are not enabled yet."),
        new(
            MapLayerKind.VendingMachines,
            "Vending",
            false,
            false,
            "PHASE 7",
            "Live map-marker polling is not enabled yet."),
        new(MapLayerKind.Monuments, "Monuments", true, true, "DIRECT RUST+"),
        new(
            MapLayerKind.Events,
            "World events",
            false,
            false,
            "PHASE 6",
            "Live map-marker polling is not enabled yet."),
        new(
            MapLayerKind.SmartDevices,
            "Smart devices",
            false,
            false,
            "MANUAL",
            "Rust+ does not provide device positions."),
        new(
            MapLayerKind.Cameras,
            "CCTV",
            false,
            false,
            "EXTERNAL",
            "Camera codes and positions require user or catalogue data.")
    ];
}
