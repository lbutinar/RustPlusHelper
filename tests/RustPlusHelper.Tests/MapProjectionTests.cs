using RustPlusHelper.Application.Map;

namespace RustPlusHelper.Tests;

public sealed class MapProjectionTests
{
    [Theory]
    [InlineData(0, 0, 50, 950)]
    [InlineData(4500, 4500, 950, 50)]
    [InlineData(2250, 2250, 500, 500)]
    public void ProjectsWorldCoordinatesIntoImagePixels(
        double worldX,
        double worldY,
        double expectedX,
        double expectedY)
    {
        var result = MapProjection.WorldToImage(worldX, worldY, 4500, 1000, 1000, 50);

        Assert.Equal(expectedX, result.PixelX, 6);
        Assert.Equal(expectedY, result.PixelY, 6);
    }

    [Theory]
    [InlineData(3500, 23)]
    [InlineData(4500, 30)]
    public void UsesCurrentCenteredFacepunchGridCount(double mapSize, int expected)
    {
        Assert.Equal(expected, MapGrid.GetCellCount(mapSize));
    }

    [Theory]
    [InlineData(0, 4500, "A0")]
    [InlineData(149.9, 4350.1, "A0")]
    [InlineData(150, 4350, "B1")]
    [InlineData(3900, 3600, "AA6")]
    [InlineData(4500, 0, "AD29")]
    public void ConvertsWorldPositionsToPlayerFacingGrid(
        double worldX,
        double worldY,
        string expected)
    {
        Assert.Equal(expected, MapGrid.WorldToGrid(worldX, worldY, 4500)?.Label);
    }

    [Fact]
    public void BuildsGridInsideRustPlusOceanMargin()
    {
        var grid = MapGrid.CreateDefinition(4500, 1000, 1000, 50);

        Assert.Equal(30, grid.CellCount);
        Assert.Equal(150, grid.WorldCellSize, 6);
        Assert.Equal(50, grid.Left, 6);
        Assert.Equal(50, grid.Top, 6);
        Assert.Equal(950, grid.Right, 6);
        Assert.Equal(950, grid.Bottom, 6);
        Assert.Equal("A", grid.ColumnLabels[0]);
        Assert.Equal("Z", grid.ColumnLabels[25]);
        Assert.Equal("AA", grid.ColumnLabels[26]);
        Assert.Equal("AD", grid.ColumnLabels[29]);
    }

    [Theory]
    [InlineData(-1, 100)]
    [InlineData(100, -1)]
    [InlineData(4501, 100)]
    [InlineData(100, 4501)]
    public void RejectsCoordinatesOutsidePlayableGrid(double worldX, double worldY)
    {
        Assert.Null(MapGrid.WorldToGrid(worldX, worldY, 4500));
    }

    [Theory]
    [InlineData(0, 1000, 1000, 50)]
    [InlineData(4500, 0, 1000, 50)]
    [InlineData(4500, 1000, 1000, 500)]
    public void RejectsInvalidDimensions(double mapSize, double width, double height, double margin)
    {
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() =>
            MapProjection.WorldToImage(0, 0, mapSize, width, height, margin));
    }
}
