namespace RustPlusHelper.Application.Map;

public readonly record struct ProjectedMapPoint(double PixelX, double PixelY);

public static class MapProjection
{
    /// <summary>
    /// Projects Rust world coordinates (origin bottom-left) into image pixels (origin top-left).
    /// </summary>
    public static ProjectedMapPoint WorldToImage(
        double worldX,
        double worldY,
        double worldMapSize,
        double imageWidth,
        double imageHeight,
        double oceanMargin)
    {
        if (worldMapSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(worldMapSize));
        }

        if (imageWidth <= 0 || imageHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(imageWidth), "Image dimensions must be positive.");
        }

        if (oceanMargin < 0 || oceanMargin * 2 >= imageWidth || oceanMargin * 2 >= imageHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(oceanMargin));
        }

        var usableWidth = imageWidth - (2 * oceanMargin);
        var usableHeight = imageHeight - (2 * oceanMargin);
        var pixelX = (worldX * (usableWidth / worldMapSize)) + oceanMargin;
        var pixelY = imageHeight - ((worldY * (usableHeight / worldMapSize)) + oceanMargin);

        return new ProjectedMapPoint(pixelX, pixelY);
    }
}
