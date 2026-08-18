using System.Globalization;
using RustPlusHelper.Application.Map;
using RustPlusHelper.Application.RustPlus;

namespace RustPlusHelper.Application.Vending;

public sealed record VendingMachineListing(
    ulong? MarkerId,
    string Name,
    float? X,
    float? Y,
    string? GridReference,
    bool? IsOutOfStock,
    IReadOnlyList<VendingOrderSnapshot> Offers,
    string? NearestOnlineTeamMember,
    double? NearestOnlineTeamDistance);

public static class VendingMarketplace
{
    public static IReadOnlyList<VendingMachineListing> Search(
        MapMarkersSnapshot? markers,
        TeamSnapshot? team,
        uint? mapSize,
        string? query)
    {
        var normalizedQuery = query?.Trim();
        return (markers?.Markers ?? [])
            .Where(marker => marker.Kind == MapMarkerKind.VendingMachine)
            .Where(marker => Matches(marker, normalizedQuery))
            .Select(marker => CreateListing(marker, team, mapSize))
            .OrderBy(listing => listing.IsOutOfStock == true)
            .ThenBy(listing => listing.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(listing => listing.MarkerId)
            .ToArray();
    }

    private static bool Matches(MapMarkerSnapshot marker, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return marker.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) == true
            || (marker.VendingOrders ?? []).Any(order =>
                order.ItemId.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.Ordinal)
                || order.CurrencyId.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.Ordinal));
    }

    private static VendingMachineListing CreateListing(
        MapMarkerSnapshot marker,
        TeamSnapshot? team,
        uint? mapSize)
    {
        var x = marker.X.GetValueOrDefault();
        var y = marker.Y.GetValueOrDefault();
        var hasPosition = marker.X is not null
            && marker.Y is not null
            && float.IsFinite(x)
            && float.IsFinite(y);
        var nearest = hasPosition
            ? (team?.Members ?? [])
                .Where(member => member.IsOnline && float.IsFinite(member.X) && float.IsFinite(member.Y))
                .Select(member => new
                {
                    Member = member,
                    Distance = Math.Sqrt(Math.Pow(member.X - x, 2) + Math.Pow(member.Y - y, 2))
                })
                .OrderBy(candidate => candidate.Distance)
                .FirstOrDefault()
            : null;
        var grid = hasPosition && mapSize is { } size
            ? MapGrid.WorldToGrid(x, y, size)?.Label
            : null;

        return new VendingMachineListing(
            marker.Id,
            marker.Name ?? "Unnamed machine",
            marker.X,
            marker.Y,
            grid,
            marker.IsOutOfStock,
            marker.VendingOrders ?? [],
            nearest?.Member.Name ?? (nearest is null ? null : "Unnamed teammate"),
            nearest?.Distance);
    }
}
