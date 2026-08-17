using RustPlusHelper.Application.RustPlus;

namespace RustPlusHelper.Application.Map;

public enum DashboardConnectionState
{
    NotStarted,
    Loading,
    Ready,
    Failed
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
}
