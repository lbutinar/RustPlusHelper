using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using RustPlusHelper.Application.Map;

namespace RustPlusHelper.Infrastructure.Map;

internal static class NoBuildZoneCatalog
{
    private const int CircleSegments = 40;
    private const string ResourceSuffix = "Data.no-build-catalog.json.gz";
    private static readonly Lazy<Catalog> Data = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    public static IReadOnlyList<MapNoBuildZoneSnapshot> CreateZones(
        WorldData world,
        out MapNoBuildZoneEvidence evidence)
    {
        var catalog = Data.Value;
        var definitions = catalog.Definitions.ToDictionary(item => item.PrefabId);
        var candidates = world.Prefabs
            .Where(item => item.Position is not null && definitions.ContainsKey(item.Id))
            .ToArray();
        var zones = new List<MapNoBuildZoneSnapshot>();
        var resolvedOwners = 0;
        foreach (var prefab in candidates)
        {
            var definition = definitions[prefab.Id];
            if (definition.Zones.Count == 0)
            {
                continue;
            }

            resolvedOwners++;
            for (var zoneIndex = 0; zoneIndex < definition.Zones.Count; zoneIndex++)
            {
                var zone = definition.Zones[zoneIndex];
                var localBoundary = CreateLocalBoundary(zone);
                var boundary = localBoundary
                    .Select(point => TransformToMap(prefab, Transform(zone.LocalMatrix, point), world.Size))
                    .ToArray();
                if (boundary.Count(point => float.IsFinite(point.X) && float.IsFinite(point.Y)) < 3)
                {
                    continue;
                }

                zones.Add(new(
                    $"no-build:{prefab.Id.ToString(CultureInfo.InvariantCulture)}:{zoneIndex.ToString(CultureInfo.InvariantCulture)}",
                    definition.PrefabPath,
                    zone.Shape,
                    boundary));
            }
        }

        var warning = candidates.Length == 0
            ? "No placed prefabs matched the bundled build catalogue. Update Rust and re-import the map."
            : "The .map format has no Rust build ID; geometry is an external build snapshot and may become stale after a game update.";
        evidence = new(
            catalog.Source.RustBuildId,
            candidates.Length,
            resolvedOwners,
            zones.Count,
            $"EXTERNAL RUST BUILD {catalog.Source.RustBuildId}",
            warning);
        return zones;
    }

    private static IReadOnlyList<Vector3> CreateLocalBoundary(Zone zone)
    {
        if (string.Equals(zone.Shape, "circle", StringComparison.Ordinal))
        {
            return Enumerable.Range(0, CircleSegments)
                .Select(index =>
                {
                    var angle = index * Math.Tau / CircleSegments;
                    return new Vector3(
                        zone.Center.X + (zone.Radius * Math.Cos(angle)),
                        zone.Center.Y,
                        zone.Center.Z + (zone.Radius * Math.Sin(angle)));
                })
                .ToArray();
        }

        var halfX = zone.Size.X / 2d;
        var halfZ = zone.Size.Z / 2d;
        return
        [
            new(zone.Center.X - halfX, zone.Center.Y, zone.Center.Z - halfZ),
            new(zone.Center.X + halfX, zone.Center.Y, zone.Center.Z - halfZ),
            new(zone.Center.X + halfX, zone.Center.Y, zone.Center.Z + halfZ),
            new(zone.Center.X - halfX, zone.Center.Y, zone.Center.Z + halfZ)
        ];
    }

    private static Vector3 Transform(double[][] matrix, Vector3 point) => new(
        (matrix[0][0] * point.X) + (matrix[0][1] * point.Y) + (matrix[0][2] * point.Z) + matrix[0][3],
        (matrix[1][0] * point.X) + (matrix[1][1] * point.Y) + (matrix[1][2] * point.Z) + matrix[1][3],
        (matrix[2][0] * point.X) + (matrix[2][1] * point.Y) + (matrix[2][2] * point.Z) + matrix[2][3]);

    private static MapWorldPoint TransformToMap(PrefabData prefab, Vector3 point, uint worldSize)
    {
        var scale = prefab.Scale ?? new VectorData { X = 1, Y = 1, Z = 1 };
        var rotation = prefab.Rotation ?? new VectorData();
        var scaled = new Vector3(point.X * scale.X, point.Y * scale.Y, point.Z * scale.Z);
        var rz = RotateZ(scaled, Degrees(rotation.Z));
        var rx = RotateX(rz, Degrees(rotation.X));
        var ry = RotateY(rx, Degrees(rotation.Y));
        var position = prefab.Position!;
        var half = worldSize / 2d;
        return new(
            (float)(ry.X + position.X + half),
            (float)(ry.Z + position.Z + half));
    }

    private static Vector3 RotateX(Vector3 value, double angle) => new(
        value.X,
        (Math.Cos(angle) * value.Y) - (Math.Sin(angle) * value.Z),
        (Math.Sin(angle) * value.Y) + (Math.Cos(angle) * value.Z));

    private static Vector3 RotateY(Vector3 value, double angle) => new(
        (Math.Cos(angle) * value.X) + (Math.Sin(angle) * value.Z),
        value.Y,
        (-Math.Sin(angle) * value.X) + (Math.Cos(angle) * value.Z));

    private static Vector3 RotateZ(Vector3 value, double angle) => new(
        (Math.Cos(angle) * value.X) - (Math.Sin(angle) * value.Y),
        (Math.Sin(angle) * value.X) + (Math.Cos(angle) * value.Y),
        value.Z);

    private static double Degrees(double value) => value * Math.PI / 180d;

    private static Catalog Load()
    {
        var assembly = typeof(NoBuildZoneCatalog).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(ResourceSuffix, StringComparison.Ordinal));
        using var compressed = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException("The no-build catalogue resource is missing.");
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<Catalog>(gzip, JsonOptions)
            ?? throw new InvalidDataException("The no-build catalogue resource is invalid.");
    }

    private static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private sealed record Catalog(Source Source, IReadOnlyList<Definition> Definitions);
    private sealed record Source(string RustBuildId);
    private sealed record Definition(uint PrefabId, string PrefabPath, IReadOnlyList<Zone> Zones);

    private sealed class Zone
    {
        public string Shape { get; init; } = string.Empty;
        public double Radius { get; init; }
        public Vector3 Center { get; init; } = new();
        public Vector3 Size { get; init; } = new();
        public double[][] LocalMatrix { get; init; } = [];
    }

    private sealed record Vector3(double X = 0, double Y = 0, double Z = 0);
}
