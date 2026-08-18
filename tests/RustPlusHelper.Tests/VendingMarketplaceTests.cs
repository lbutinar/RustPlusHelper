using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Application.Vending;

namespace RustPlusHelper.Tests;

public sealed class VendingMarketplaceTests
{
    [Fact]
    public void SearchesMachineNamesAndDirectNumericOfferIds()
    {
        var markers = new MapMarkersSnapshot([
            Machine(1, "Weapons", -904863145, -932201673),
            Machine(2, "Resources", -151838493, -932201673)
        ]);

        Assert.Equal("Weapons", Assert.Single(VendingMarketplace.Search(markers, null, 4500, "weapon")).Name);
        Assert.Equal("Resources", Assert.Single(VendingMarketplace.Search(markers, null, 4500, "-151838493")).Name);
        Assert.Equal(2, VendingMarketplace.Search(markers, null, 4500, "-932201673").Count);
    }

    [Fact]
    public void DerivesGridAndNearestOnlineTeamDistanceWithoutChangingDirectOffers()
    {
        var offer = new VendingOrderSnapshot(-904863145, 1, -932201673, 85, 3, false, false, 1, 1, 1, 1);
        var markers = new MapMarkersSnapshot([
            new MapMarkerSnapshot(1, MapMarkerKind.VendingMachine, 150, 300, Name: "Weapons", VendingOrders: [offer])
        ]);
        var team = new TeamSnapshot(
            1,
            [
                new TeamMemberSnapshot(1, "Online", 150, 400, true, true, default, default),
                new TeamMemberSnapshot(2, "Closer but offline", 150, 310, false, true, default, default)
            ],
            [],
            [],
            null);

        var listing = Assert.Single(VendingMarketplace.Search(markers, team, 4500, null));

        Assert.Equal("B28", listing.GridReference);
        Assert.Equal("Online", listing.NearestOnlineTeamMember);
        Assert.Equal(100, listing.NearestOnlineTeamDistance);
        Assert.Same(offer, Assert.Single(listing.Offers));
    }

    private static MapMarkerSnapshot Machine(ulong id, string name, int itemId, int currencyId) =>
        new(
            id,
            MapMarkerKind.VendingMachine,
            100,
            200,
            Name: name,
            VendingOrders: [new VendingOrderSnapshot(itemId, 1, currencyId, 10, 2, false, false, 1, 1, null, null)]);
}
