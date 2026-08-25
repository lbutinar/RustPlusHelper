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
    TerrainSlope,
    BuildPlanning,
    Elevation,
    WaterDepth,
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
    MovementTrails,
    SmartDevices,
    Cameras,
    PersonalPins
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
    string? TopologyError = null,
    // In-memory-only recent positions per currently-online team member, copied from
    // RustPlusLiveSessionState.MovementTrails. Null and an empty dictionary are equivalent.
    IReadOnlyDictionary<ulong, IReadOnlyList<MovementTrailPoint>>? MovementTrails = null,
    // Loaded from IPersonalMapPinRepository for the current ServerId. Null and an empty list are
    // equivalent.
    IReadOnlyList<PersonalMapPin>? PersonalPins = null)
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
        new(MapLayerKind.TerrainSlope, "Terrain slope", false, false, "DERIVED FROM .MAP HEIGHTS", "Import the selected server's Rust .map file."),
        new(MapLayerKind.BuildPlanning, "Build planning", false, false, "DERIVED · MIXED EXTERNAL SOURCES", "Import the selected server's Rust .map file."),
        new(MapLayerKind.Elevation, "Elevation + contours", false, false, "DERIVED FROM .MAP HEIGHTS", "Import the selected server's Rust .map file."),
        new(MapLayerKind.WaterDepth, "Water depth + shoreline", false, false, "DERIVED FROM .MAP TERRAIN/WATER", "Import the selected server's Rust .map file."),
        new(MapLayerKind.ResourcePotential, "Resource potential", false, false, "DERIVED FROM .MAP", "Import a .map file; this never shows live nodes."),
        new(MapLayerKind.Roads, "Road paths", false, false, "EXTERNAL .MAP", "Import the selected server's Rust .map file."),
        new(MapLayerKind.Railways, "Rail paths", false, false, "EXTERNAL .MAP", "Import the selected server's Rust .map file."),
        new(MapLayerKind.Rivers, "River channels", false, false, "EXTERNAL .MAP WIDTHS", "Import the selected server's Rust .map file."),
        new(MapLayerKind.NoBuildZones, "No-build zones", false, false, "EXTERNAL BUILD SNAPSHOT", "Import the selected server's Rust .map file."),
        new(MapLayerKind.Team, "Team", true, true, "DIRECT"),
        new(MapLayerKind.TeamNotes, "Team notes", true, true, "DIRECT"),
        new(MapLayerKind.VendingMachines, "Vending", true, true, "DIRECT"),
        new(MapLayerKind.Monuments, "Monuments", true, true, "DIRECT"),
        new(MapLayerKind.Events, "World events", true, true, "DIRECT + DIFF"),
        new(MapLayerKind.DeathHistory, "Team death hotspots", false, false, "DERIVED · LOCAL HISTORY", "No team death positions have been recorded yet."),
        new(MapLayerKind.MovementTrails, "Movement trails", false, false, "DERIVED · LOCAL HISTORY", "No recent team movement has been recorded yet."),
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
            "Camera codes and positions require user or catalogue data."),
        new(MapLayerKind.PersonalPins, "Personal pins", true, true, "MANUAL")
    ];

    public static IReadOnlyList<MapLayerState> CreateLiveMapLayers(
        bool teamAvailable = false,
        bool markersAvailable = false,
        bool deathHistoryAvailable = false,
        bool movementTrailsAvailable = false,
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
            MapLayerKind.TerrainSlope,
            "Terrain slope",
            false,
            topology?.Data.TerrainSlopeRaster is not null,
            topology?.Data.TerrainSlopeRaster is not null ? "DERIVED FROM .MAP HEIGHTS" : "UNAVAILABLE",
            topology?.Data.TerrainSlopeRaster is not null
                ? "Flat ≤ 5°, gentle ≤ 12°, moderate ≤ 25°, steep > 25°. This is a terrain-planning aid, not proof that building is allowed."
                : "Import a .map file containing a height layer."),
        RasterLayer(
            MapLayerKind.BuildPlanning,
            "Build planning",
            topology?.Data.BuildPlanningRaster,
            "DERIVED · MIXED EXTERNAL SOURCES",
            "Green is a good flat candidate, yellow means sloped/caution, red means known blocked or steep, and blue means water. Candidate land is not guaranteed buildable."),
        RasterLayer(
            MapLayerKind.Elevation,
            "Elevation + contours",
            topology?.Data.ElevationRaster,
            "DERIVED FROM .MAP HEIGHTS",
            "Elevation bands use world metres; contour lines are spaced every 25 m with 100 m major lines."),
        RasterLayer(
            MapLayerKind.WaterDepth,
            "Water depth + shoreline",
            topology?.Data.WaterDepthRaster,
            "DERIVED FROM .MAP TERRAIN/WATER",
            "Depth is serialized water height minus terrain height; local water-culling volumes are not represented."),
        new(
            MapLayerKind.ResourcePotential,
            "Resource potential",
            false,
            topology?.Data.ResourcePotentialRaster is not null,
            topology?.Data.ResourcePotentialRaster is not null ? "DERIVED · NOT LIVE NODES" : "UNAVAILABLE",
            topology?.Data.ResourcePotentialRaster is not null
                ? "Ore/rock and sulfur potential come from documented topology flags only; exact spawned nodes require server access."
                : "Import a .map file; this never shows live nodes."),
        PathLayer(MapLayerKind.Roads, "Road paths", MapPathKind.Road, topology),
        PathLayer(MapLayerKind.Railways, "Rail paths", MapPathKind.Railway, topology),
        PathLayer(MapLayerKind.Rivers, "River channels", MapPathKind.River, topology, "EXTERNAL .MAP WIDTHS"),
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
            MapLayerKind.MovementTrails,
            "Movement trails",
            movementTrailsAvailable,
            movementTrailsAvailable,
            "DERIVED · LOCAL HISTORY",
            movementTrailsAvailable ? null : "No recent team movement has been recorded yet."),
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
            "Camera codes and positions require user or catalogue data."),
        new(MapLayerKind.PersonalPins, "Personal pins", true, true, "MANUAL")
    ];

    private static MapLayerState PathLayer(
        MapLayerKind layer,
        string name,
        MapPathKind pathKind,
        SavedMapTopology? topology,
        string sourceLabel = "EXTERNAL .MAP")
    {
        var available = topology?.Data.Paths.Any(path => path.Kind == pathKind) == true;
        return new MapLayerState(
            layer,
            name,
            false,
            available,
            available ? sourceLabel : "UNAVAILABLE",
            available ? null : "The imported .map file does not contain this path type.");
    }

    private static MapLayerState RasterLayer(
        MapLayerKind layer,
        string name,
        MapRasterSnapshot? raster,
        string source,
        string explanation) => new(
            layer,
            name,
            false,
            raster is not null,
            raster is null ? "UNAVAILABLE" : source,
            raster is null ? "Import a .map file containing the required terrain layers." : explanation);
}
