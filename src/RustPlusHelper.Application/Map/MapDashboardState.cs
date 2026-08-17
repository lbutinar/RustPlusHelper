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
    DateTimeOffset? LiveDataRetrievedAtUtc,
    bool IsLiveDataRefreshing,
    string? LiveDataStatus,
    string? LiveDataError,
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
        false,
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

    public static IReadOnlyList<MapLayerState> CreateLiveMapLayers(
        bool teamAvailable = false,
        bool markersAvailable = false) =>
    [
        new(MapLayerKind.BaseMap, "Base map", true, true, "DIRECT RUST+"),
        new(
            MapLayerKind.Team,
            "Team",
            teamAvailable,
            teamAvailable,
            teamAvailable ? "DIRECT RUST+" : "UNAVAILABLE",
            teamAvailable ? null : "The latest Rust+ team request did not return data."),
        new(
            MapLayerKind.TeamNotes,
            "Team notes",
            teamAvailable,
            teamAvailable,
            teamAvailable ? "DIRECT RUST+" : "UNAVAILABLE",
            teamAvailable ? null : "The latest Rust+ team request did not return data."),
        new(
            MapLayerKind.VendingMachines,
            "Vending",
            markersAvailable,
            markersAvailable,
            markersAvailable ? "DIRECT RUST+" : "UNAVAILABLE",
            markersAvailable ? null : "The latest Rust+ map-marker request did not return data."),
        new(MapLayerKind.Monuments, "Monuments", true, true, "DIRECT RUST+"),
        new(
            MapLayerKind.Events,
            "World events",
            markersAvailable,
            markersAvailable,
            markersAvailable ? "DIRECT RUST+" : "UNAVAILABLE",
            markersAvailable ? null : "The latest Rust+ map-marker request did not return data."),
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
