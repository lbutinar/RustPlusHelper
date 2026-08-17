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
    [InlineData(0, 1000, 1000, 50)]
    [InlineData(4500, 0, 1000, 50)]
    [InlineData(4500, 1000, 1000, 500)]
    public void RejectsInvalidDimensions(double mapSize, double width, double height, double margin)
    {
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() =>
            MapProjection.WorldToImage(0, 0, mapSize, width, height, margin));
    }
}
