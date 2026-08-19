using RustPlusHelper.Application.Vending;

namespace RustPlusHelper.Tests;

public sealed class ItemCatalogTests
{
    [Theory]
    [InlineData(-151838493, "wood", "Wood")]
    [InlineData(-932201673, "scrap", "Scrap")]
    [InlineData(-904863145, "rifle.semiauto", "Semi-Automatic Rifle")]
    public void ResolvesStableKnownItemIds(int itemId, string expectedShortName, string expectedName)
    {
        var item = ItemCatalog.TryResolve(itemId);

        Assert.NotNull(item);
        Assert.Equal(expectedShortName, item!.ShortName);
        Assert.Equal(expectedName, item.Name);
    }

    [Fact]
    public void ReturnsNullForAnUnknownItemId()
    {
        Assert.Null(ItemCatalog.TryResolve(int.MinValue + 1));
    }

    [Fact]
    public void PrefersTheDotDelimitedShortNameForADuplicatedSourceEntry()
    {
        // The upstream dataset has this ID twice under two shortname spellings
        // ("2module car chassis" and "2module.car.chassis"); the dotted form matches Rust's own
        // shortname convention and must win the dedup.
        var item = ItemCatalog.TryResolve(-226151558);

        Assert.NotNull(item);
        Assert.Equal("2module.car.chassis", item!.ShortName);
    }
}
