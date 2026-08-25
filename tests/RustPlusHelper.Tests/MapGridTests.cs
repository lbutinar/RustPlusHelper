using RustPlusHelper.Application.Map;

namespace RustPlusHelper.Tests;

public sealed class MapGridTests
{
    [Fact]
    public void TryParseCellWorldCenterRoundTripsThroughWorldToGrid()
    {
        const double mapSize = 4500;

        Assert.True(MapGrid.TryParseCellWorldCenter("H14", mapSize, out var worldX, out var worldY));

        var roundTripped = MapGrid.WorldToGrid(worldX, worldY, mapSize);
        Assert.NotNull(roundTripped);
        Assert.Equal("H14", roundTripped.Value.Label);
    }

    [Fact]
    public void TryParseCellWorldCenterRejectsAnOutOfRangeLabel()
    {
        Assert.False(MapGrid.TryParseCellWorldCenter("ZZ999", 4500, out _, out _));
    }

    [Fact]
    public void TryParseCellWorldCenterRejectsAMalformedLabel()
    {
        Assert.False(MapGrid.TryParseCellWorldCenter("14H", 4500, out _, out _));
        Assert.False(MapGrid.TryParseCellWorldCenter(null, 4500, out _, out _));
        Assert.False(MapGrid.TryParseCellWorldCenter("  ", 4500, out _, out _));
    }

    [Fact]
    public void TryParseCellWorldCenterRejectsAnInvalidMapSize()
    {
        Assert.False(MapGrid.TryParseCellWorldCenter("A0", 0, out _, out _));
        Assert.False(MapGrid.TryParseCellWorldCenter("A0", double.NaN, out _, out _));
    }
}
