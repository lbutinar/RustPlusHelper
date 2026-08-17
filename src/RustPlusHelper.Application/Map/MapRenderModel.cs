using RustPlusHelper.Application.RustPlus;

namespace RustPlusHelper.Application.Map;

public sealed record MapOverlayItem(
    string Id,
    MapLayerKind Layer,
    string Kind,
    string Label,
    double PixelX,
    double PixelY,
    float WorldX,
    float WorldY,
    bool IsOnline = false,
    bool IsAlive = true);

public sealed record MapRenderModel(
    double Width,
    double Height,
    IReadOnlyList<MapOverlayItem> Items,
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

        foreach (var monument in state.Map.Monuments)
        {
            if (monument.X is { } x && monument.Y is { } y)
            {
                items.Add(CreateItem(
                    $"monument:{items.Count}",
                    MapLayerKind.Monuments,
                    "monument",
                    monument.TokenOrName ?? "Unknown monument",
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
                x,
                y,
                mapSize,
                width,
                height,
                margin));
        }

        return new MapRenderModel(
            width,
            height,
            items,
            state.Layers.ToDictionary(
                layer => ToLayerKey(layer.Kind),
                layer => layer.IsVisible && layer.IsAvailable,
                StringComparer.Ordinal));
    }

    public static string ToLayerKey(MapLayerKind kind) => kind switch
    {
        MapLayerKind.BaseMap => "baseMap",
        MapLayerKind.Team => "team",
        MapLayerKind.TeamNotes => "teamNotes",
        MapLayerKind.VendingMachines => "vendingMachines",
        MapLayerKind.Monuments => "monuments",
        MapLayerKind.Events => "events",
        MapLayerKind.SmartDevices => "smartDevices",
        MapLayerKind.Cameras => "cameras",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static MapOverlayItem CreateItem(
        string id,
        MapLayerKind layer,
        string kind,
        string label,
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
        return new MapOverlayItem(
            id,
            layer,
            kind,
            label,
            point.PixelX,
            point.PixelY,
            worldX,
            worldY,
            isOnline,
            isAlive);
    }

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
