using RustPlusHelper.Application.RustPlus;

namespace RustPlusHelper.Application.Map;

public sealed record MapOverlayItem(
    string Id,
    MapLayerKind Layer,
    string Kind,
    string Label,
    string Glyph,
    string? GridReference,
    double PixelX,
    double PixelY,
    float WorldX,
    float WorldY,
    bool IsOnline = false,
    bool IsAlive = true);

public sealed record MapRasterOverlay(
    string Id,
    MapLayerKind Layer,
    int Width,
    int Height,
    byte[] Rgba,
    double PixelLeft,
    double PixelTop,
    double PixelRight,
    double PixelBottom);

public sealed record MapPolylineOverlay(
    string Id,
    MapLayerKind Layer,
    string Label,
    float Width,
    IReadOnlyList<ProjectedMapPoint> Points);

public sealed record MapRenderModel(
    double Width,
    double Height,
    MapGridDefinition Grid,
    IReadOnlyList<MapOverlayItem> Items,
    IReadOnlyList<MapRasterOverlay> Rasters,
    IReadOnlyList<MapPolylineOverlay> Polylines,
    IReadOnlyDictionary<string, bool> LayerVisibility);

public static class MapRenderModelFactory
{
    public static MapRenderModel? Create(MapDashboardState state)
    {
        if (state.Server?.MapSize is not { } mapSize
            || state.Map?.Width is not { } width
            || state.Map.Height is not { } height
            || state.Map.OceanMargin is not { } margin)
        {
            return null;
        }

        var items = new List<MapOverlayItem>();
        var rasters = new List<MapRasterOverlay>();
        var polylines = new List<MapPolylineOverlay>();

        foreach (var monument in state.Map.Monuments)
        {
            if (monument.X is { } x && monument.Y is { } y)
            {
                var display = MonumentCatalog.Resolve(monument.TokenOrName);
                items.Add(CreateItem(
                    $"monument:{items.Count}",
                    MapLayerKind.Monuments,
                    "monument",
                    display.Name,
                    display.Glyph,
                    x,
                    y,
                    mapSize,
                    width,
                    height,
                    margin));
            }
        }

        foreach (var member in state.Team?.Members ?? [])
        {
            items.Add(CreateItem(
                $"team:{member.SteamId}",
                MapLayerKind.Team,
                "team",
                member.Name ?? "Unknown teammate",
                "T",
                member.X,
                member.Y,
                mapSize,
                width,
                height,
                margin,
                member.IsOnline,
                member.IsAlive));
        }

        var noteIndex = 0;
        foreach (var note in state.Team?.Notes ?? [])
        {
            items.Add(CreateItem(
                $"note:{noteIndex++}",
                MapLayerKind.TeamNotes,
                "team-note",
                note.Text ?? "Team note",
                "+",
                note.X,
                note.Y,
                mapSize,
                width,
                height,
                margin));
        }

        foreach (var note in state.Team?.LeaderNotes ?? [])
        {
            items.Add(CreateItem(
                $"leader-note:{noteIndex++}",
                MapLayerKind.TeamNotes,
                "team-note",
                note.Text ?? "Leader note",
                "+",
                note.X,
                note.Y,
                mapSize,
                width,
                height,
                margin));
        }

        if (state.Team?.LeaderDeathPosition is { } deathPosition)
        {
            items.Add(CreateItem(
                "team:leader-death",
                MapLayerKind.Events,
                "death",
                "Team leader death position",
                "†",
                deathPosition.X,
                deathPosition.Y,
                mapSize,
                width,
                height,
                margin));
        }

        foreach (var marker in state.Markers?.Markers ?? [])
        {
            if (marker.X is not { } x || marker.Y is not { } y)
            {
                continue;
            }

            var layer = marker.Kind == MapMarkerKind.VendingMachine
                ? MapLayerKind.VendingMachines
                : marker.Kind == MapMarkerKind.Player
                    ? MapLayerKind.Team
                    : MapLayerKind.Events;
            var label = marker.Name ?? marker.Kind.ToString();

            items.Add(CreateItem(
                $"marker:{marker.Id?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                layer,
                ToMarkerKind(marker.Kind),
                label,
                MarkerGlyph(marker.Kind),
                x,
                y,
                mapSize,
                width,
                height,
                margin));
        }

        if (state.Topology?.Data is { } topology && topology.WorldSize == mapSize)
        {
            AddRaster(rasters, topology.BiomeRaster, topology.Sha256, MapLayerKind.Biomes, margin, width, height);
            AddRaster(rasters, topology.TopologyRaster, topology.Sha256, MapLayerKind.Topology, margin, width, height);
            AddRaster(
                rasters,
                topology.ResourcePotentialRaster,
                topology.Sha256,
                MapLayerKind.ResourcePotential,
                margin,
                width,
                height);

            var pathIndex = 0;
            foreach (var path in topology.Paths)
            {
                var layer = path.Kind switch
                {
                    MapPathKind.Road => MapLayerKind.Roads,
                    MapPathKind.Railway => MapLayerKind.Railways,
                    MapPathKind.River => MapLayerKind.Rivers,
                    _ => (MapLayerKind?)null
                };
                if (layer is null || path.Nodes.Count < 2)
                {
                    continue;
                }

                polylines.Add(new MapPolylineOverlay(
                    $"path:{pathIndex++}",
                    layer.Value,
                    path.Name,
                    path.Width,
                    path.Nodes.Select(node => MapProjection.WorldToImage(
                        node.X,
                        node.Y,
                        mapSize,
                        width,
                        height,
                        margin)).ToArray()));
            }
        }

        return new MapRenderModel(
            width,
            height,
            MapGrid.CreateDefinition(mapSize, width, height, margin),
            items,
            rasters,
            polylines,
            state.Layers.ToDictionary(
                layer => ToLayerKey(layer.Kind),
                layer => layer.IsVisible && layer.IsAvailable,
                StringComparer.Ordinal));
    }

    public static string ToLayerKey(MapLayerKind kind) => kind switch
    {
        MapLayerKind.BaseMap => "baseMap",
        MapLayerKind.Grid => "grid",
        MapLayerKind.Biomes => "biomes",
        MapLayerKind.Topology => "topology",
        MapLayerKind.ResourcePotential => "resourcePotential",
        MapLayerKind.Roads => "roads",
        MapLayerKind.Railways => "railways",
        MapLayerKind.Rivers => "rivers",
        MapLayerKind.NoBuildZones => "noBuildZones",
        MapLayerKind.Team => "team",
        MapLayerKind.TeamNotes => "teamNotes",
        MapLayerKind.VendingMachines => "vendingMachines",
        MapLayerKind.Monuments => "monuments",
        MapLayerKind.Events => "events",
        MapLayerKind.SmartDevices => "smartDevices",
        MapLayerKind.Cameras => "cameras",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static void AddRaster(
        ICollection<MapRasterOverlay> rasters,
        MapRasterSnapshot? raster,
        string fingerprint,
        MapLayerKind layer,
        int margin,
        uint imageWidth,
        uint imageHeight)
    {
        if (raster is null)
        {
            return;
        }

        rasters.Add(new MapRasterOverlay(
            $"{fingerprint}:{ToLayerKey(layer)}",
            layer,
            raster.Width,
            raster.Height,
            raster.Rgba,
            margin,
            margin,
            imageWidth - margin,
            imageHeight - margin));
    }

    private static MapOverlayItem CreateItem(
        string id,
        MapLayerKind layer,
        string kind,
        string label,
        string glyph,
        float worldX,
        float worldY,
        uint mapSize,
        uint width,
        uint height,
        int margin,
        bool isOnline = false,
        bool isAlive = true)
    {
        var point = MapProjection.WorldToImage(worldX, worldY, mapSize, width, height, margin);
        var grid = MapGrid.WorldToGrid(worldX, worldY, mapSize)?.Label;
        return new MapOverlayItem(
            id,
            layer,
            kind,
            label,
            glyph,
            grid,
            point.PixelX,
            point.PixelY,
            worldX,
            worldY,
            isOnline,
            isAlive);
    }

    private static string MarkerGlyph(MapMarkerKind kind) => kind switch
    {
        MapMarkerKind.Player => "T",
        MapMarkerKind.Explosion => "!",
        MapMarkerKind.VendingMachine => "V",
        MapMarkerKind.Ch47 => "47",
        MapMarkerKind.CargoShip => "C",
        MapMarkerKind.Crate => "□",
        MapMarkerKind.GenericRadius => "○",
        MapMarkerKind.PatrolHelicopter => "H",
        MapMarkerKind.TravellingVendor => "TV",
        _ => "?"
    };

    private static string ToMarkerKind(MapMarkerKind kind) => kind switch
    {
        MapMarkerKind.Player => "team",
        MapMarkerKind.Explosion => "explosion",
        MapMarkerKind.VendingMachine => "vending",
        MapMarkerKind.Ch47 => "ch47",
        MapMarkerKind.CargoShip => "cargo",
        MapMarkerKind.Crate => "crate",
        MapMarkerKind.GenericRadius => "radius",
        MapMarkerKind.PatrolHelicopter => "patrol-heli",
        MapMarkerKind.TravellingVendor => "travelling-vendor",
        MapMarkerKind.Unknown => "unknown",
        _ => "unknown"
    };
}
