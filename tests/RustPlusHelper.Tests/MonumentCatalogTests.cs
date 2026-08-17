using RustPlusHelper.Application.Map;

namespace RustPlusHelper.Tests;

public sealed class MonumentCatalogTests
{
    [Theory]
    [InlineData("launch_site_1", "Launch Site", "LS")]
    [InlineData("gas_station_1", "Oxum's Gas Station", "OX")]
    [InlineData("compound", "Outpost", "OP")]
    [InlineData("sphere_tank", "The Dome", "DM")]
    [InlineData("mining_quarry_a", "Sulfur Quarry", "SU")]
    [InlineData("mining_quarry_b", "Stone Quarry", "ST")]
    [InlineData("mining_quarry_c", "HQM Quarry", "HQ")]
    [InlineData("oilrig_1", "Small Oil Rig", "SR")]
    [InlineData("oilrig_2", "Large Oil Rig", "LR")]
    [InlineData("airfield_display_name", "Airfield", "AF")]
    [InlineData("launchsite", "Launch Site", "LS")]
    [InlineData("oil_rig_small", "Small Oil Rig", "SR")]
    [InlineData("large_oil_rig", "Large Oil Rig", "LR")]
    [InlineData("gas_station", "Oxum's Gas Station", "OX")]
    [InlineData("water_treatment_plant_display_name", "Water Treatment Plant", "WT")]
    [InlineData("train_tunnel_link_display_name", "Train Tunnel Link", "TL")]
    public void ResolvesRustPrefabTokens(string token, string name, string glyph)
    {
        Assert.Equal(new MonumentDisplay(name, glyph), MonumentCatalog.Resolve(token));
    }

    [Fact]
    public void AcceptsFullPrefabPathAndHumanizesUnknownFutureToken()
    {
        Assert.Equal(
            new MonumentDisplay("Water Treatment Plant", "WT"),
            MonumentCatalog.Resolve("assets/bundled/prefabs/autospawn/monument/large/water_treatment_plant_1.prefab"));
        Assert.Equal(
            new MonumentDisplay("Naval Base", "NB"),
            MonumentCatalog.Resolve("naval_base_1"));
    }
}
