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
    IReadOnlyList<string> RowLabels,
    IReadOnlyList<string> CellLabels);

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

        var columnLabels = Enumerable.Range(0, count).Select(ColumnName).ToArray();
        var rowLabels = Enumerable.Range(0, count)
            .Select(row => row.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        var cellLabels = rowLabels
            .SelectMany(row => columnLabels.Select(column => $"{column}{row}"))
            .ToArray();

        return new MapGridDefinition(
            count,
            mapSize / count,
            southWest.PixelX,
            northEast.PixelY,
            northEast.PixelX,
            southWest.PixelY,
            columnLabels,
            rowLabels,
            cellLabels);
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

    /// <summary>
    /// Parses a player-typed grid label (e.g. "H14") and returns the pixel coordinates of that cell's
    /// center, using the same left/top/right/bottom pixel bounds and cell count already rendered by
    /// <see cref="CreateDefinition"/> — so a search result lines up exactly with the visible grid lines.
    /// </summary>
    public static bool TryParseCellCenter(
        string? label,
        MapGridDefinition grid,
        out double pixelX,
        out double pixelY)
    {
        ArgumentNullException.ThrowIfNull(grid);
        pixelX = 0;
        pixelY = 0;

        if (string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        var trimmed = label.Trim().ToUpperInvariant();
        var splitIndex = 0;
        while (splitIndex < trimmed.Length && trimmed[splitIndex] is >= 'A' and <= 'Z')
        {
            splitIndex++;
        }

        if (splitIndex == 0 || splitIndex == trimmed.Length)
        {
            return false;
        }

        var digits = trimmed[splitIndex..];
        if (!digits.All(char.IsAsciiDigit)
            || !int.TryParse(digits, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var row))
        {
            return false;
        }

        var column = ColumnIndex(trimmed[..splitIndex]);
        if (column < 0 || column >= grid.CellCount || row < 0 || row >= grid.CellCount)
        {
            return false;
        }

        var cellWidth = (grid.Right - grid.Left) / grid.CellCount;
        var cellHeight = (grid.Bottom - grid.Top) / grid.CellCount;
        pixelX = grid.Left + (cellWidth * (column + 0.5));
        pixelY = grid.Top + (cellHeight * (row + 0.5));
        return true;
    }

    /// <summary>Inverse of <see cref="ColumnName"/>'s bijective base-26 encoding.</summary>
    private static int ColumnIndex(string letters)
    {
        var value = 0;
        foreach (var letter in letters)
        {
            value = (value * 26) + (letter - 'A' + 1);
        }

        return value - 1;
    }
}
