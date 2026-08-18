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
    Grid,
    Biomes,
    Topology,
    ResourcePotential,
    Roads,
    Railways,
    Rivers,
    NoBuildZones,
    Team,
    TeamNotes,
    VendingMachines,
    Monuments,
    Events,
    DeathHistory,
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
    IReadOnlyList<CompanionEvent> Events,
    IReadOnlyList<MapLayerState> Layers,
    string? ErrorMessage,
    SavedMapTopology? Topology = null,
    bool IsTopologyImporting = false,
    string? TopologyStatus = null,
    string? TopologyError = null)
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
        [],
        CreateDefaultLayers(),
        null);

    public static IReadOnlyList<MapLayerState> CreateDefaultLayers() =>
    [
        new(MapLayerKind.BaseMap, "Base map", true, true, "DIRECT"),
        new(MapLayerKind.Grid, "Map grid", true, true, "DERIVED"),
        new(MapLayerKind.Biomes, "Biomes", false, false, "EXTERNAL .MAP", "Import the selected server's Rust .map file."),
        new(MapLayerKind.Topology, "Terrain topology", false, false, "EXTERNAL .MAP", "Import the selected server's Rust .map file."),
        new(MapLayerKind.ResourcePotential, "Ore potential", false, false, "DERIVED FROM .MAP", "Import a .map file; this never shows live nodes."),
        new(MapLayerKind.Roads, "Road paths", false, false, "EXTERNAL .MAP", "Import the selected server's Rust .map file."),
        new(MapLayerKind.Railways, "Rail paths", false, false, "EXTERNAL .MAP", "Import the selected server's Rust .map file."),
        new(MapLayerKind.Rivers, "River paths", false, false, "EXTERNAL .MAP", "Import the selected server's Rust .map file."),
        new(MapLayerKind.NoBuildZones, "No-build zones", false, false, "EXTERNAL BUILD SNAPSHOT", "Import the selected server's Rust .map file."),
        new(MapLayerKind.Team, "Team", true, true, "DIRECT"),
        new(MapLayerKind.TeamNotes, "Team notes", true, true, "DIRECT"),
        new(MapLayerKind.VendingMachines, "Vending", true, true, "DIRECT"),
        new(MapLayerKind.Monuments, "Monuments", true, true, "DIRECT"),
        new(MapLayerKind.Events, "World events", true, true, "DIRECT + DIFF"),
        new(MapLayerKind.DeathHistory, "Team death hotspots", false, false, "DERIVED · LOCAL HISTORY", "No team death positions have been recorded yet."),
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
        bool markersAvailable = false,
        bool deathHistoryAvailable = false,
        SavedMapTopology? topology = null) =>
    [
        new(MapLayerKind.BaseMap, "Base map", true, true, "DIRECT RUST+"),
        new(MapLayerKind.Grid, "Map grid", true, true, "DERIVED FROM MAP SIZE"),
        new(
            MapLayerKind.Biomes,
            "Biomes",
            false,
            topology?.Data.BiomeRaster is not null,
            topology?.Data.BiomeRaster is not null ? "EXTERNAL .MAP" : "UNAVAILABLE",
            topology?.Data.BiomeRaster is not null ? null : "Import the selected server's Rust .map file."),
        new(
            MapLayerKind.Topology,
            "Terrain topology",
            false,
            topology?.Data.TopologyRaster is not null,
            topology?.Data.TopologyRaster is not null ? "EXTERNAL .MAP" : "UNAVAILABLE",
            topology?.Data.TopologyRaster is not null ? null : "Import the selected server's Rust .map file."),
        new(
            MapLayerKind.ResourcePotential,
            "Ore potential",
            false,
            topology?.Data.ResourcePotentialRaster is not null,
            topology?.Data.ResourcePotentialRaster is not null ? "DERIVED · NOT LIVE NODES" : "UNAVAILABLE",
            topology?.Data.ResourcePotentialRaster is not null
                ? "Potential comes from topology only; exact spawned nodes require server access."
                : "Import a .map file; this never shows live nodes."),
        PathLayer(MapLayerKind.Roads, "Road paths", MapPathKind.Road, topology),
        PathLayer(MapLayerKind.Railways, "Rail paths", MapPathKind.Railway, topology),
        PathLayer(MapLayerKind.Rivers, "River paths", MapPathKind.River, topology),
        new(
            MapLayerKind.NoBuildZones,
            "No-build zones",
            false,
            topology?.Data.NoBuildZones?.Count > 0,
            topology?.Data.NoBuildZoneEvidence?.SourceLabel ?? "UNAVAILABLE",
            topology?.Data.NoBuildZones?.Count > 0
                ? topology.Data.NoBuildZoneEvidence?.Warning
                : "Import a .map file containing prefabs recognized by the bundled Rust-build snapshot."),
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
            MapLayerKind.DeathHistory,
            "Team death hotspots",
            deathHistoryAvailable,
            deathHistoryAvailable,
            "DERIVED · LOCAL HISTORY",
            deathHistoryAvailable ? null : "No team death positions have been recorded on this server yet."),
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

    private static MapLayerState PathLayer(
        MapLayerKind layer,
        string name,
        MapPathKind pathKind,
        SavedMapTopology? topology)
    {
        var available = topology?.Data.Paths.Any(path => path.Kind == pathKind) == true;
        return new MapLayerState(
            layer,
            name,
            false,
            available,
            available ? "EXTERNAL .MAP" : "UNAVAILABLE",
            available ? null : "The imported .map file does not contain this path type.");
    }
}
