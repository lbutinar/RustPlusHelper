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

public sealed record MapPolygonOverlay(
    string Id,
    MapLayerKind Layer,
    string Label,
    IReadOnlyList<ProjectedMapPoint> Points);

public sealed record MapHeatSpot(
    string Id,
    MapLayerKind Layer,
    string Label,
    string GridReference,
    int Count,
    double PixelX,
    double PixelY,
    DateTimeOffset LatestAtUtc);

public sealed record MapRenderModel(
    double Width,
    double Height,
    MapGridDefinition Grid,
    IReadOnlyList<MapOverlayItem> Items,
    IReadOnlyList<MapRasterOverlay> Rasters,
    IReadOnlyList<MapPolylineOverlay> Polylines,
    IReadOnlyList<MapPolygonOverlay> Polygons,
    IReadOnlyList<MapHeatSpot> HeatSpots,
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
        var polygons = new List<MapPolygonOverlay>();
        var heatSpots = new List<MapHeatSpot>();

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

        foreach (var (steamId, trail) in state.MovementTrails ?? new Dictionary<ulong, IReadOnlyList<MovementTrailPoint>>())
        {
            // Filtered to the server's last wipe, the same convention already used for the
            // team-death hotspot layer — persisted trail history otherwise survives indefinitely.
            var sinceWipe = state.Server.WipeTimeUtc is { } wipeTimeUtc
                ? trail.Where(point => point.SampledAtUtc >= wipeTimeUtc).ToArray()
                : (IReadOnlyList<MovementTrailPoint>)trail;
            if (sinceWipe.Count < 2)
            {
                continue;
            }

            var name = state.Team?.Members.FirstOrDefault(member => member.SteamId == steamId)?.Name ?? "Teammate";
            polylines.Add(new MapPolylineOverlay(
                $"movement-trail:{steamId}",
                MapLayerKind.MovementTrails,
                $"{name}'s recent path",
                1.5f,
                sinceWipe.Select(point => MapProjection.WorldToImage(point.X, point.Y, mapSize, width, height, margin)).ToArray()));
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

        var positionedDeaths = state.Events
            .Where(item => item.Kind == CompanionEventKind.TeamMemberDied
                && item.Position is not null
                && (state.Server.WipeTimeUtc is null || item.OccurredAtUtc >= state.Server.WipeTimeUtc))
            .Select(item => new
            {
                Event = item,
                Position = item.Position!,
                Grid = MapGrid.WorldToGrid(item.Position!.X, item.Position.Y, mapSize)?.Label
            })
            .Where(item => item.Grid is not null)
            .GroupBy(item => item.Grid!, StringComparer.Ordinal);
        foreach (var gridDeaths in positionedDeaths)
        {
            var count = gridDeaths.Count();
            var worldX = (float)gridDeaths.Average(item => item.Position.X);
            var worldY = (float)gridDeaths.Average(item => item.Position.Y);
            var projected = MapProjection.WorldToImage(worldX, worldY, mapSize, width, height, margin);
            heatSpots.Add(new(
                $"death-history:{gridDeaths.Key}",
                MapLayerKind.DeathHistory,
                $"{count} recorded team death{(count == 1 ? string.Empty : "s")}",
                gridDeaths.Key,
                count,
                projected.PixelX,
                projected.PixelY,
                gridDeaths.Max(item => item.Event.OccurredAtUtc)));
        }

        foreach (var pin in state.PersonalPins ?? [])
        {
            items.Add(CreateItem(
                $"personal-pin:{pin.Id}",
                MapLayerKind.PersonalPins,
                "personal-pin",
                pin.Note,
                "📍",
                pin.WorldX,
                pin.WorldY,
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
            AddRaster(rasters, topology.TerrainSlopeRaster, topology.Sha256, MapLayerKind.TerrainSlope, margin, width, height);
            AddRaster(rasters, topology.BuildPlanningRaster, topology.Sha256, MapLayerKind.BuildPlanning, margin, width, height);
            AddRaster(rasters, topology.ElevationRaster, topology.Sha256, MapLayerKind.Elevation, margin, width, height);
            AddRaster(rasters, topology.WaterDepthRaster, topology.Sha256, MapLayerKind.WaterDepth, margin, width, height);
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

                var projectedNodes = path.Nodes.Select(node => MapProjection.WorldToImage(
                    node.X,
                    node.Y,
                    mapSize,
                    width,
                    height,
                    margin)).ToArray();
                polylines.Add(new MapPolylineOverlay(
                    $"path:{pathIndex}",
                    layer.Value,
                    path.Name,
                    path.Width,
                    projectedNodes));

                if (path.Kind == MapPathKind.River)
                {
                    var metresToPixels = (width - (2d * margin)) / mapSize;
                    var corridor = CreatePathCorridor(
                        projectedNodes,
                        Math.Max(1, ((path.Width / 2d) + path.OuterPadding) * metresToPixels));
                    if (corridor.Count >= 3)
                    {
                        polygons.Add(new MapPolygonOverlay(
                            $"river-corridor:{pathIndex}",
                            MapLayerKind.Rivers,
                            $"{path.Name} · {path.Width:0.#} m channel",
                            corridor));
                    }
                }

                pathIndex++;
            }

            foreach (var zone in topology.NoBuildZones ?? [])
            {
                if (zone.Boundary.Count < 3)
                {
                    continue;
                }

                polygons.Add(new MapPolygonOverlay(
                    zone.Id,
                    MapLayerKind.NoBuildZones,
                    $"No-build zone · {FriendlyPrefabName(zone.PrefabPath)}",
                    zone.Boundary.Select(point => MapProjection.WorldToImage(
                        point.X,
                        point.Y,
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
            polygons,
            heatSpots,
            state.Layers.ToDictionary(
                layer => ToLayerKey(layer.Kind),
                layer => layer.IsVisible && layer.IsAvailable,
                StringComparer.Ordinal));
    }

    private static string FriendlyPrefabName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).Replace('_', ' ');
        return string.IsNullOrWhiteSpace(name) ? "Rust prefab" : name;
    }

    private static IReadOnlyList<ProjectedMapPoint> CreatePathCorridor(
        IReadOnlyList<ProjectedMapPoint> points,
        double halfWidth)
    {
        if (points.Count < 2)
        {
            return [];
        }

        var left = new List<ProjectedMapPoint>(points.Count);
        var right = new List<ProjectedMapPoint>(points.Count);
        for (var index = 0; index < points.Count; index++)
        {
            var previous = points[Math.Max(0, index - 1)];
            var next = points[Math.Min(points.Count - 1, index + 1)];
            var dx = next.PixelX - previous.PixelX;
            var dy = next.PixelY - previous.PixelY;
            var length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length < 0.001)
            {
                left.Add(points[index]);
                right.Add(points[index]);
                continue;
            }

            var normalX = -dy / length;
            var normalY = dx / length;
            left.Add(new(points[index].PixelX + (normalX * halfWidth), points[index].PixelY + (normalY * halfWidth)));
            right.Add(new(points[index].PixelX - (normalX * halfWidth), points[index].PixelY - (normalY * halfWidth)));
        }

        right.Reverse();
        return [.. left, .. right];
    }

    public static string ToLayerKey(MapLayerKind kind) => kind switch
    {
        MapLayerKind.BaseMap => "baseMap",
        MapLayerKind.Grid => "grid",
        MapLayerKind.Biomes => "biomes",
        MapLayerKind.Topology => "topology",
        MapLayerKind.TerrainSlope => "terrainSlope",
        MapLayerKind.BuildPlanning => "buildPlanning",
        MapLayerKind.Elevation => "elevation",
        MapLayerKind.WaterDepth => "waterDepth",
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
        MapLayerKind.DeathHistory => "deathHistory",
        MapLayerKind.MovementTrails => "movementTrails",
        MapLayerKind.SmartDevices => "smartDevices",
        MapLayerKind.Cameras => "cameras",
        MapLayerKind.PersonalPins => "personalPins",
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
