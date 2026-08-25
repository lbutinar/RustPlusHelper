using RustPlusHelper.Application.Map;
using RustPlusHelper.Application.RustPlus;

namespace RustPlusHelper.Tests;

public sealed class MapAlignmentReportTests
{
    [Fact]
    public void ProjectsEachMonumentUsingTheSameMathAsTheRenderedGrid()
    {
        var monuments = new[]
        {
            new MapMonumentSnapshot("airfield_1", 2250, 2250),
            new MapMonumentSnapshot("compound", 0, 4500),
        };

        var markers = MapAlignmentReport.BuildMarkers(4500, 1000, 1000, 50, monuments);

        Assert.Equal(2, markers.Count);
        Assert.Equal("Airfield", markers[0].Label);
        Assert.Equal(500, markers[0].PixelX, 6);
        Assert.Equal(500, markers[0].PixelY, 6);
        Assert.Equal(MapGrid.WorldToGrid(2250, 2250, 4500)?.Label, markers[0].GridLabel);

        Assert.Equal("Outpost", markers[1].Label);
        Assert.Equal(MapGrid.WorldToGrid(0, 4500, 4500)?.Label, markers[1].GridLabel);
    }

    [Fact]
    public void SkipsMonumentsWithoutAReportedPosition()
    {
        var monuments = new[] { new MapMonumentSnapshot("compound", null, null) };

        var markers = MapAlignmentReport.BuildMarkers(4500, 1000, 1000, 50, monuments);

        Assert.Empty(markers);
    }

    [Fact]
    public void RendersAnHtmlPageWithOnePositionedMarkerPerMonument()
    {
        var markers = new[]
        {
            new MapAlignmentMarker("Airfield", "P15", 500, 500),
            new MapAlignmentMarker("<script>", null, 0, 1000),
        };

        var html = MapAlignmentReport.BuildHtml("map.jpg", 1000, 1000, markers);

        Assert.Contains("src=\"map.jpg\"", html, StringComparison.Ordinal);
        Assert.Contains("left:50%", html, StringComparison.Ordinal);
        Assert.Contains("top:50%", html, StringComparison.Ordinal);
        Assert.Contains("Airfield (P15)", html, StringComparison.Ordinal);
        Assert.Contains("left:0%", html, StringComparison.Ordinal);
        Assert.Contains("top:100%", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }
}
