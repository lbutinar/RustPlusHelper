using RustPlusHelper.Application.RustPlus;

namespace RustPlusHelper.Tests;

public sealed class CompanionEventCsvExporterTests
{
    [Fact]
    public void WritesAHeaderAndOneRowPerEvent()
    {
        var events = new[]
        {
            new CompanionEvent(
                Guid.NewGuid(),
                Guid.NewGuid(),
                new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
                CompanionEventKind.TeamMemberDied,
                CompanionEventSource.SnapshotDiff,
                "Kakec died",
                "Position from the Rust+ team snapshot where death was detected.",
                new MapPositionSnapshot(100, 200))
        };
        using var stream = new MemoryStream();

        CompanionEventCsvExporter.Write(events, stream);

        var csv = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal("OccurredAtUtc,Kind,Source,Title,Detail,WorldX,WorldY", lines[0]);
        Assert.Equal(
            "2026-01-01T12:00:00.0000000+00:00,TeamMemberDied,SnapshotDiff,Kakec died,Position from the Rust+ team snapshot where death was detected.,100,200",
            lines[1]);
    }

    [Fact]
    public void QuotesFieldsContainingCommasOrQuotes()
    {
        var events = new[]
        {
            new CompanionEvent(
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UnixEpoch,
                CompanionEventKind.VendingPriceChanged,
                CompanionEventSource.SnapshotDiff,
                "Scrap price changed at Bob's \"Best\" Shop",
                "250 -> 300, Scrap")
        };
        using var stream = new MemoryStream();

        CompanionEventCsvExporter.Write(events, stream);

        var csv = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains("\"Scrap price changed at Bob's \"\"Best\"\" Shop\"", csv, StringComparison.Ordinal);
        Assert.Contains("\"250 -> 300, Scrap\"", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void LeavesWorldCoordinatesBlankWhenThereIsNoPosition()
    {
        var events = new[]
        {
            new CompanionEvent(
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UnixEpoch,
                CompanionEventKind.ConnectionEstablished,
                CompanionEventSource.Transport,
                "Connected")
        };
        using var stream = new MemoryStream();

        CompanionEventCsvExporter.Write(events, stream);

        var csv = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        var lastLine = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Last().TrimEnd('\r');
        Assert.EndsWith(",,", lastLine, StringComparison.Ordinal);
    }
}
