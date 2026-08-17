namespace RustPlusHelper.Application.Map;

public readonly record struct MapGridCoordinate(int Column, int Row, string Label);

public sealed record MapGridDefinition(
    int CellCount,
    double WorldCellSize,
    double Left,
    double Top,
    double Right,
    double Bottom,
    IReadOnlyList<string> ColumnLabels,
    IReadOnlyList<string> RowLabels);

/// <summary>
/// Projects Rust world coordinates into the current centered in-game grid. Facepunch defines the
/// number of cells as floor(map size * 7 / 1024), then divides the map evenly by that count.
/// </summary>
public static class MapGrid
{
    public static int GetCellCount(double mapSize)
    {
        if (!double.IsFinite(mapSize) || mapSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapSize));
        }

        return Math.Max(1, (int)Math.Floor(mapSize * 7d / 1024d));
    }

    public static MapGridCoordinate? WorldToGrid(double worldX, double worldY, double mapSize)
    {
        if (!double.IsFinite(worldX)
            || !double.IsFinite(worldY)
            || !double.IsFinite(mapSize)
            || mapSize <= 0
            || worldX < 0
            || worldY < 0
            || worldX > mapSize
            || worldY > mapSize)
        {
            return null;
        }

        var count = GetCellCount(mapSize);
        var cellSize = mapSize / count;
        var column = Math.Min((int)Math.Floor(worldX / cellSize), count - 1);
        var row = Math.Min((int)Math.Floor((mapSize - worldY) / cellSize), count - 1);
        return new MapGridCoordinate(column, row, $"{ColumnName(column)}{row}");
    }

    public static MapGridDefinition CreateDefinition(
        double mapSize,
        double imageWidth,
        double imageHeight,
        double oceanMargin)
    {
        var southWest = MapProjection.WorldToImage(
            0,
            0,
            mapSize,
            imageWidth,
            imageHeight,
            oceanMargin);
        var northEast = MapProjection.WorldToImage(
            mapSize,
            mapSize,
            mapSize,
            imageWidth,
            imageHeight,
            oceanMargin);
        var count = GetCellCount(mapSize);

        return new MapGridDefinition(
            count,
            mapSize / count,
            southWest.PixelX,
            northEast.PixelY,
            northEast.PixelX,
            southWest.PixelY,
            Enumerable.Range(0, count).Select(ColumnName).ToArray(),
            Enumerable.Range(0, count).Select(row => row.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray());
    }

    public static string ColumnName(int column)
    {
        if (column < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        var value = column + 1;
        Span<char> buffer = stackalloc char[8];
        var index = buffer.Length;
        while (value > 0)
        {
            value--;
            buffer[--index] = (char)('A' + (value % 26));
            value /= 26;
        }

        return new string(buffer[index..]);
    }
}
