using System.Globalization;

namespace RustPlusHelper.Application.RustPlus;

/// <summary>Formats companion event history as CSV for the user's own export/analysis. Purely local:
/// nothing here sends data anywhere, it only writes to the caller-supplied stream.</summary>
public static class CompanionEventCsvExporter
{
    /// <summary>Writes one header row plus one row per event, in the order given (the Events page
    /// shows newest first, and this preserves whatever order the caller passes).</summary>
    public static void Write(IReadOnlyList<CompanionEvent> events, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(destination);

        using var writer = new StreamWriter(destination, leaveOpen: true);
        writer.WriteLine("OccurredAtUtc,Kind,Source,Title,Detail,WorldX,WorldY");
        foreach (var item in events)
        {
            writer.WriteLine(string.Join(
                ',',
                item.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture),
                item.Kind.ToString(),
                item.Source.ToString(),
                Escape(item.Title),
                Escape(item.Detail),
                item.Position?.X.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                item.Position?.Y.ToString(CultureInfo.InvariantCulture) ?? string.Empty));
        }

        writer.Flush();
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.IndexOfAny([',', '"', '\n', '\r']) < 0
            ? value
            : $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
