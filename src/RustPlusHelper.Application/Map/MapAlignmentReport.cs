using System.Net;
using RustPlusHelper.Application.RustPlus;

namespace RustPlusHelper.Application.Map;

/// <summary>One monument projected onto the Rust+ map JPEG for a visual alignment check.</summary>
public readonly record struct MapAlignmentMarker(string Label, string? GridLabel, double PixelX, double PixelY);

/// <summary>
/// Builds a self-contained visual alignment check from data Rust+ already returns directly: the map
/// JPEG and each monument's own reported world position. Overlaying the projected pixel for a known,
/// visually distinctive monument (e.g. Launch Site, Airfield) onto the JPEG lets a human confirm the
/// projection/grid math without needing the official Rust+ app as a reference — the JPEG and the
/// monument coordinates are both already Rust+'s own ground truth.
/// </summary>
public static class MapAlignmentReport
{
    public static IReadOnlyList<MapAlignmentMarker> BuildMarkers(
        double mapSize,
        double imageWidth,
        double imageHeight,
        double oceanMargin,
        IReadOnlyList<MapMonumentSnapshot> monuments)
    {
        ArgumentNullException.ThrowIfNull(monuments);

        var markers = new List<MapAlignmentMarker>();
        foreach (var monument in monuments)
        {
            if (monument.X is not { } worldX || monument.Y is not { } worldY)
            {
                continue;
            }

            var pixel = MapProjection.WorldToImage(worldX, worldY, mapSize, imageWidth, imageHeight, oceanMargin);
            var grid = MapGrid.WorldToGrid(worldX, worldY, mapSize);
            var label = MonumentCatalog.Resolve(monument.TokenOrName).Name;
            markers.Add(new MapAlignmentMarker(label, grid?.Label, pixel.PixelX, pixel.PixelY));
        }

        return markers;
    }

    /// <summary>
    /// A standalone HTML page: the map JPEG with each marker drawn as a labelled dot at its projected
    /// pixel. Open it in a browser and confirm each label sits on the corresponding visible structure
    /// in the satellite image; a consistent offset would indicate a projection or grid regression.
    /// </summary>
    public static string BuildHtml(
        string mapImageFileName,
        double imageWidth,
        double imageHeight,
        IReadOnlyList<MapAlignmentMarker> markers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapImageFileName);
        ArgumentNullException.ThrowIfNull(markers);

        var markerHtml = string.Join('\n', markers.Select(marker =>
        {
            var left = imageWidth <= 0 ? 0 : marker.PixelX / imageWidth * 100;
            var top = imageHeight <= 0 ? 0 : marker.PixelY / imageHeight * 100;
            var text = WebUtility.HtmlEncode(
                marker.GridLabel is null ? marker.Label : $"{marker.Label} ({marker.GridLabel})");
            return $"""
                <div class="marker" style="left:{left.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)}%;top:{top.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)}%">
                  <span class="dot"></span><span class="label">{text}</span>
                </div>
                """;
        }));

        return $$"""
            <!doctype html>
            <html>
            <head>
            <meta charset="utf-8">
            <title>Rust+ map alignment check</title>
            <style>
              body { background: #111; color: #eee; font-family: sans-serif; margin: 0; padding: 1rem; }
              p { max-width: 60rem; }
              .map { position: relative; display: inline-block; }
              .map img { display: block; max-width: 100%; height: auto; }
              .marker { position: absolute; transform: translate(-50%, -50%); text-align: center; }
              .marker .dot { display: block; width: 10px; height: 10px; margin: 0 auto; border-radius: 50%;
                background: #ff3b3b; border: 2px solid #fff; box-shadow: 0 0 4px #000; }
              .marker .label { display: inline-block; margin-top: 2px; padding: 1px 4px; background: rgba(0,0,0,0.7);
                font-size: 12px; white-space: nowrap; border-radius: 3px; }
            </style>
            </head>
            <body>
            <p>Each dot is a monument placed using this app's own grid/projection math and the world position Rust+
            reported for it. Confirm every label sits on that monument's visible structure in the satellite image
            below; a consistent offset in one direction would indicate a projection or grid regression.</p>
            <div class="map">
              <img src="{{WebUtility.HtmlEncode(mapImageFileName)}}" alt="Rust+ map">
            {{markerHtml}}
            </div>
            </body>
            </html>
            """;
    }
}
