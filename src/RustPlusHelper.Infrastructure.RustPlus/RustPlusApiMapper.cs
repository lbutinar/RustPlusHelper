using System.Globalization;
using RustPlusApi.Data;
using RustPlusApi.Data.Markers;
using RustPlusHelper.Application.RustPlus;

namespace RustPlusHelper.Infrastructure.RustPlus;

internal static class RustPlusApiMapper
{
    internal static ServerInfoSnapshot Map(ServerInfo source) => new(
        source.Name,
        source.HeaderImage,
        source.Url,
        source.Map,
        source.MapSize,
        ToUtc(source.WipeTime),
        source.PlayerCount,
        source.MaxPlayerCount,
        source.QueuedPlayerCount,
        source.Seed,
        source.Salt,
        source.LogoImage,
        source.Nexus,
        source.NexusId,
        source.NexusZone);

    internal static CameraInfoSnapshot Map(RustPlusApi.Camera.CameraController controller) => new(
        controller.Info.Width,
        controller.Info.Height,
        controller.Info.NearPlane,
        controller.Info.FarPlane,
        controller.IsStaticCamera,
        controller.IsPtzCamera,
        controller.IsAutoTurret,
        controller.IsDrone);

    internal static ServerMapSnapshot Map(ServerMap source) => new(
        source.Width,
        source.Height,
        source.OceanMargin,
        $"#{source.Background.ToArgb():X8}",
        source.Monuments?.Select(monument =>
            new MapMonumentSnapshot(monument.Name, monument.X, monument.Y)).ToArray() ?? [],
        source.JpgImage?.ToArray() ?? []);

    internal static TeamSnapshot Map(TeamInfo source) => new(
        source.LeaderSteamId,
        source.Members?.Select(member => new TeamMemberSnapshot(
            member.SteamId,
            member.Name,
            member.X,
            member.Y,
            member.IsOnline,
            member.IsAlive,
            ToUtc(member.LastSpawnTime),
            ToUtc(member.LastDeathTime))).ToArray() ?? [],
        source.Notes?.Select(note =>
            new TeamNoteSnapshot(note.X, note.Y, note.Text, (int)note.Icon, (int)note.Color)).ToArray() ?? [],
        source.LeaderNotes?.Select(note =>
            new TeamNoteSnapshot(note.X, note.Y, note.Text, (int)note.Icon, (int)note.Color)).ToArray() ?? [],
        source.DeathNote is null
            ? null
            : new MapPositionSnapshot(source.DeathNote.X, source.DeathNote.Y));

    internal static TeamChatSnapshot Map(TeamChatInfo source) => new(
        source.Messages?.Select(message => new TeamChatMessageSnapshot(
            message.SteamId,
            message.Name,
            message.Message,
            $"#{message.Color.ToArgb():X8}",
            ToUtc(message.Time))).ToArray() ?? []);

    internal static MapMarkersSnapshot Map(MapMarkers source)
    {
        var markers = new List<MapMarkerSnapshot>();

        markers.AddRange(source.PlayerMarkers.Select(pair => Map(pair.Key, pair.Value)));
        markers.AddRange(source.ExplosionMarkers.Select(pair => Map(pair.Key, pair.Value, MapMarkerKind.Explosion)));
        markers.AddRange(source.VendingMachineMarkers.Select(pair => Map(pair.Key, pair.Value)));
        markers.AddRange(source.Ch47Markers.Select(pair => Map(pair.Key, pair.Value, MapMarkerKind.Ch47, pair.Value.Rotation)));
        markers.AddRange(source.CargoShipMarkers.Select(pair => Map(pair.Key, pair.Value, MapMarkerKind.CargoShip, pair.Value.Rotation)));
        markers.AddRange(source.CrateMarkers.Select(pair => Map(pair.Key, pair.Value, MapMarkerKind.Crate)));
        markers.AddRange(source.GenericRadiusMarkers.Select(pair => new MapMarkerSnapshot(
            pair.Value.Id ?? pair.Key,
            MapMarkerKind.GenericRadius,
            pair.Value.X,
            pair.Value.Y,
            Radius: pair.Value.Radius)));
        markers.AddRange(source.PatrolHelicopterMarkers.Select(pair =>
            Map(pair.Key, pair.Value, MapMarkerKind.PatrolHelicopter, pair.Value.Rotation)));
        markers.AddRange(source.TravellingVendorMarkers.Select(pair =>
            Map(pair.Key, pair.Value, MapMarkerKind.TravellingVendor, pair.Value.Rotation)));
        markers.AddRange(source.UnknownMarkers.Select(pair => Map(pair.Key, pair.Value)));

        return new MapMarkersSnapshot(markers);
    }

    private static MapMarkerSnapshot Map(ulong key, PlayerMarker marker) => new(
        marker.Id ?? key,
        MapMarkerKind.Player,
        marker.X,
        marker.Y,
        Name: marker.Name,
        SteamId: marker.SteamId);

    private static MapMarkerSnapshot Map(ulong key, VendingMachineMarker marker) => new(
        marker.Id ?? key,
        MapMarkerKind.VendingMachine,
        marker.X,
        marker.Y,
        Name: marker.Name,
        IsOutOfStock: marker.IsOutOfStock,
        VendingOrders: MapOrders(marker.VendingMachineItems));

    private static MapMarkerSnapshot Map(ulong key, UnknownMarker marker) => new(
        marker.Id ?? key,
        MapMarkerKind.Unknown,
        marker.X,
        marker.Y,
        marker.RawType,
        marker.Name,
        marker.SteamId,
        marker.Rotation,
        marker.Radius,
        marker.IsOutOfStock,
        MapOrders(marker.VendingMachineItems));

    private static MapMarkerSnapshot Map(
        ulong key,
        Marker marker,
        MapMarkerKind kind,
        float? rotation = null) => new(marker.Id ?? key, kind, marker.X, marker.Y, Rotation: rotation);

    private static IReadOnlyList<VendingOrderSnapshot> MapOrders(IEnumerable<VendingMachineItem>? source) =>
        source?.Select(item => new VendingOrderSnapshot(
            item.Id,
            item.StackSize,
            item.CurrencyId,
            item.CostPerStack,
            item.StackSizeAmount,
            item.IsItemBlueprint,
            item.IsCurrencyBlueprint,
            item.ItemLife,
            item.ItemMaxLife,
            item.PriceMultiplier,
            item.ReceivedQuantityMultiplier)).ToArray() ?? [];

    private static DateTimeOffset? ToUtc(DateTime? value) => value is null ? null : ToUtc(value.Value);

    private static DateTimeOffset ToUtc(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        return new DateTimeOffset(utc).ToUniversalTime();
    }
}
