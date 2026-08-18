using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using K4os.Compression.LZ4.Legacy;
using ProtoBuf;
using RustPlusHelper.Application.Map;

namespace RustPlusHelper.Infrastructure.Map;

public sealed class RustMapTopologyProvider : IMapTopologyProvider
{
    private const int HeaderSize = 12;
    private const int SupportedSerializationVersion = 10;
    private const long MaximumSourceBytes = 1024L * 1024 * 1024;
    private const long MaximumDecodedBytes = 1024L * 1024 * 1024;
    private const int MaximumPathNodes = 1_000_000;
    private const int PreviewResolution = 384;

    public async Task<ImportedMapTopology> ReadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        FileStream source;
        try
        {
            source = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException("The selected Rust map file could not be opened.", exception);
        }

        await using (source.ConfigureAwait(false))
        {
            if (source.Length < HeaderSize)
            {
                throw new InvalidDataException("The selected file is too short to be a Rust .map file.");
            }

            if (source.Length > MaximumSourceBytes)
            {
                throw new InvalidDataException("The selected Rust map exceeds the 1 GiB safety limit.");
            }

            var sha256 = Convert.ToHexString(await SHA256.HashDataAsync(source, cancellationToken).ConfigureAwait(false));
            source.Position = 0;

            var header = new byte[HeaderSize];
            await source.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
            var serializationVersion = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
            var timestamp = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(4, 8));
            if (serializationVersion != SupportedSerializationVersion)
            {
                throw new InvalidDataException(
                    $"Rust world serialization version {serializationVersion.ToString(CultureInfo.InvariantCulture)} is not supported; this build supports version {SupportedSerializationVersion}.");
            }

            WorldData world;
            try
            {
                using var decoded = LZ4Legacy.Decode(source);
                using var protobuf = new MemoryStream();
                await CopyWithLimitAsync(decoded, protobuf, MaximumDecodedBytes, cancellationToken).ConfigureAwait(false);
                protobuf.Position = 0;
                world = Serializer.Deserialize<WorldData>(protobuf);
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new InvalidDataException("The selected file is not a supported or complete Rust .map file.", exception);
            }

            ValidateWorld(world);
            return CreateImportedTopology(
                Path.GetFileName(filePath),
                sha256,
                serializationVersion,
                timestamp,
                world);
        }
    }

    private static ImportedMapTopology CreateImportedTopology(
        string sourceFileName,
        string sha256,
        int serializationVersion,
        ulong timestamp,
        WorldData world)
    {
        var topologyLayer = FindLayer(world, "topology");
        var topologyResolution = topologyLayer is null ? 0 : GetSquareResolution(topologyLayer.Data.Length / 4, "topology");
        var paths = world.Paths.Select(path => ToSnapshot(path, world.Size)).ToArray();
        var noBuildZones = NoBuildZoneCatalog.CreateZones(world, out var noBuildEvidence);
        var heightLayer = FindLayer(world, "height");
        var waterLayer = FindLayer(world, "water");

        return new ImportedMapTopology(
            sourceFileName,
            sha256,
            serializationVersion,
            timestamp,
            world.Size,
            world.Maps.Select(layer => new MapSourceLayerSnapshot(layer.Name, layer.Data.Length)).ToArray(),
            world.Prefabs.Count,
            paths,
            CreateBiomeRaster(FindLayer(world, "biome"), topologyResolution),
            topologyLayer is null ? null : CreateTopologyRaster(topologyLayer.Data, topologyResolution),
            topologyLayer is null ? null : CreateResourcePotentialRaster(topologyLayer.Data, topologyResolution),
            noBuildZones,
            noBuildEvidence,
            CreateTerrainSlopeRaster(heightLayer, waterLayer, topologyLayer, topologyResolution, world.Size),
            CreateBuildPlanningRaster(
                heightLayer,
                waterLayer,
                topologyLayer,
                topologyResolution,
                world,
                noBuildZones),
            CreateElevationRaster(heightLayer, waterLayer, topologyLayer, topologyResolution, world.Size),
            CreateWaterDepthRaster(heightLayer, waterLayer, topologyLayer, topologyResolution, world.Size));
    }

    private static MapPathSnapshot ToSnapshot(PathData path, uint worldSize)
    {
        var halfSize = worldSize / 2f;
        return new MapPathSnapshot(
            path.Name,
            ClassifyPath(path.Name),
            path.Width,
            path.Nodes.Select(node => new MapWorldPoint(node.X + halfSize, node.Z + halfSize)).ToArray(),
            path.InnerPadding,
            path.OuterPadding,
            path.InnerFade,
            path.OuterFade);
    }

    private static MapPathKind ClassifyPath(string name)
    {
        if (name.Contains("river", StringComparison.OrdinalIgnoreCase))
        {
            return MapPathKind.River;
        }

        if (name.Contains("rail", StringComparison.OrdinalIgnoreCase)
            || name.Contains("train", StringComparison.OrdinalIgnoreCase))
        {
            return MapPathKind.Railway;
        }

        if (name.Contains("road", StringComparison.OrdinalIgnoreCase))
        {
            return MapPathKind.Road;
        }

        return MapPathKind.Other;
    }

    private static MapRasterSnapshot CreateTopologyRaster(byte[] data, int sourceResolution) =>
        CreateTopologyRaster(data, sourceResolution, ResourceMode.AllTopology);

    private static MapRasterSnapshot CreateResourcePotentialRaster(byte[] data, int sourceResolution) =>
        CreateTopologyRaster(data, sourceResolution, ResourceMode.ResourcePotential);

    private static MapRasterSnapshot CreateTopologyRaster(
        byte[] data,
        int sourceResolution,
        ResourceMode mode)
    {
        if (data.Length % 4 != 0)
        {
            throw new InvalidDataException("The topology layer byte count is invalid.");
        }

        var outputResolution = Math.Min(sourceResolution, PreviewResolution);
        var rgba = new byte[checked(outputResolution * outputResolution * 4)];
        for (var outputY = 0; outputY < outputResolution; outputY++)
        {
            var sourceTop = sourceResolution - (int)Math.Ceiling((outputY + 1d) * sourceResolution / outputResolution);
            var sourceBottom = sourceResolution - (int)Math.Floor(outputY * (double)sourceResolution / outputResolution);
            for (var outputX = 0; outputX < outputResolution; outputX++)
            {
                var sourceLeft = (int)Math.Floor(outputX * (double)sourceResolution / outputResolution);
                var sourceRight = (int)Math.Ceiling((outputX + 1d) * sourceResolution / outputResolution);
                uint combined = 0;
                for (var sourceY = Math.Max(0, sourceTop); sourceY < Math.Min(sourceResolution, sourceBottom); sourceY++)
                {
                    for (var sourceX = sourceLeft; sourceX < sourceRight; sourceX++)
                    {
                        var offset = checked(((sourceY * sourceResolution) + sourceX) * 4);
                        combined |= BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
                    }
                }

                var color = mode == ResourceMode.ResourcePotential
                    ? ResourceColor(combined)
                    : TopologyColor(combined);
                var outputOffset = ((outputY * outputResolution) + outputX) * 4;
                rgba[outputOffset] = color.R;
                rgba[outputOffset + 1] = color.G;
                rgba[outputOffset + 2] = color.B;
                rgba[outputOffset + 3] = color.A;
            }
        }

        return new MapRasterSnapshot(outputResolution, outputResolution, rgba);
    }

    private static MapRasterSnapshot? CreateBiomeRaster(MapData? layer, int topologyResolution)
    {
        if (layer is null || topologyResolution <= 0)
        {
            return null;
        }

        var pixels = checked(topologyResolution * topologyResolution);
        if (layer.Data.Length % pixels != 0)
        {
            throw new InvalidDataException("The biome layer does not match the topology resolution.");
        }

        var channels = layer.Data.Length / pixels;
        if (channels is < 1 or > 5)
        {
            throw new InvalidDataException("The biome layer contains an unsupported channel count.");
        }

        var outputResolution = Math.Min(topologyResolution, PreviewResolution);
        var rgba = new byte[checked(outputResolution * outputResolution * 4)];
        for (var outputY = 0; outputY < outputResolution; outputY++)
        {
            var sourceY = topologyResolution - 1 - (int)((outputY + 0.5) * topologyResolution / outputResolution);
            for (var outputX = 0; outputX < outputResolution; outputX++)
            {
                var sourceX = (int)((outputX + 0.5) * topologyResolution / outputResolution);
                var sourceIndex = (sourceY * topologyResolution) + sourceX;
                var dominantChannel = 0;
                var dominantValue = -1;
                for (var channel = 0; channel < channels; channel++)
                {
                    var value = layer.Data[(channel * pixels) + sourceIndex];
                    if (value > dominantValue)
                    {
                        dominantValue = value;
                        dominantChannel = channel;
                    }
                }

                var color = BiomeColor(dominantChannel);
                var outputOffset = ((outputY * outputResolution) + outputX) * 4;
                rgba[outputOffset] = color.R;
                rgba[outputOffset + 1] = color.G;
                rgba[outputOffset + 2] = color.B;
                rgba[outputOffset + 3] = color.A;
            }
        }

        return new MapRasterSnapshot(outputResolution, outputResolution, rgba);
    }

    private static MapRasterSnapshot? CreateTerrainSlopeRaster(
        MapData? heightLayer,
        MapData? waterLayer,
        MapData? topologyLayer,
        int topologyResolution,
        uint worldSize)
    {
        if (heightLayer is null)
        {
            return null;
        }

        var heightResolution = GetInt16Resolution(heightLayer, "height");
        var waterResolution = waterLayer is null ? 0 : GetInt16Resolution(waterLayer, "water");
        var outputResolution = Math.Min(heightResolution, PreviewResolution);
        var metresPerSample = worldSize / (double)(heightResolution - 1);
        var rgba = new byte[checked(outputResolution * outputResolution * 4)];
        for (var outputY = 0; outputY < outputResolution; outputY++)
        {
            var sourceY = Math.Clamp(
                (int)((outputY + 0.5) * heightResolution / outputResolution),
                0,
                heightResolution - 1);
            for (var outputX = 0; outputX < outputResolution; outputX++)
            {
                var sourceX = Math.Clamp(
                    (int)((outputX + 0.5) * heightResolution / outputResolution),
                    0,
                    heightResolution - 1);
                var left = HeightMetres(heightLayer.Data, heightResolution, Math.Max(0, sourceX - 1), sourceY);
                var right = HeightMetres(heightLayer.Data, heightResolution, Math.Min(heightResolution - 1, sourceX + 1), sourceY);
                var bottom = HeightMetres(heightLayer.Data, heightResolution, sourceX, Math.Max(0, sourceY - 1));
                var top = HeightMetres(heightLayer.Data, heightResolution, sourceX, Math.Min(heightResolution - 1, sourceY + 1));
                var xSpan = Math.Max(1, Math.Min(heightResolution - 1, sourceX + 1) - Math.Max(0, sourceX - 1));
                var ySpan = Math.Max(1, Math.Min(heightResolution - 1, sourceY + 1) - Math.Max(0, sourceY - 1));
                var dx = (right - left) / (xSpan * metresPerSample);
                var dz = (top - bottom) / (ySpan * metresPerSample);
                var slopeDegrees = Math.Atan(Math.Sqrt((dx * dx) + (dz * dz))) * 180d / Math.PI;

                var terrainHeight = HeightMetres(heightLayer.Data, heightResolution, sourceX, sourceY);
                var isWater = IsWater(
                    terrainHeight,
                    sourceX,
                    sourceY,
                    heightResolution,
                    waterLayer,
                    waterResolution,
                    topologyLayer,
                    topologyResolution);
                var color = isWater ? new Rgba(45, 126, 180, 100) : SlopeColor(slopeDegrees);
                var outputOffset = (((outputResolution - 1 - outputY) * outputResolution) + outputX) * 4;
                rgba[outputOffset] = color.R;
                rgba[outputOffset + 1] = color.G;
                rgba[outputOffset + 2] = color.B;
                rgba[outputOffset + 3] = color.A;
            }
        }

        return new MapRasterSnapshot(outputResolution, outputResolution, rgba);
    }

    private static MapRasterSnapshot? CreateBuildPlanningRaster(
        MapData? heightLayer,
        MapData? waterLayer,
        MapData? topologyLayer,
        int topologyResolution,
        WorldData world,
        IReadOnlyList<MapNoBuildZoneSnapshot> noBuildZones)
    {
        if (heightLayer is null)
        {
            return null;
        }

        var heightResolution = GetInt16Resolution(heightLayer, "height");
        var waterResolution = waterLayer is null ? 0 : GetInt16Resolution(waterLayer, "water");
        var outputResolution = Math.Min(heightResolution, PreviewResolution);
        var metresPerSample = world.Size / (double)(heightResolution - 1);
        var blocked = CreateKnownBlockedMask(world, noBuildZones, outputResolution);
        var rgba = new byte[checked(outputResolution * outputResolution * 4)];
        for (var outputY = 0; outputY < outputResolution; outputY++)
        {
            var sourceY = SampleCoordinate(outputY, outputResolution, heightResolution);
            var displayY = outputResolution - 1 - outputY;
            for (var outputX = 0; outputX < outputResolution; outputX++)
            {
                var sourceX = SampleCoordinate(outputX, outputResolution, heightResolution);
                var terrainHeight = HeightMetres(heightLayer.Data, heightResolution, sourceX, sourceY);
                var depth = WaterDepthMetres(
                    terrainHeight,
                    sourceX,
                    sourceY,
                    heightResolution,
                    waterLayer,
                    waterResolution,
                    topologyLayer,
                    topologyResolution);
                var slope = SlopeDegrees(heightLayer.Data, heightResolution, sourceX, sourceY, metresPerSample);
                var topology = TopologyAt(
                    sourceX,
                    sourceY,
                    heightResolution,
                    topologyLayer,
                    topologyResolution);
                var knownBlocked = blocked[(displayY * outputResolution) + outputX]
                    || HasAny(topology, 11, 21);
                var color = depth > 0.1
                    ? new Rgba(45, 126, 180, 115)
                    : knownBlocked || slope > 25
                        ? new Rgba(216, 61, 50, 175)
                        : slope > 12
                            ? new Rgba(244, 153, 53, 155)
                            : slope > 5
                                ? new Rgba(214, 197, 68, 140)
                                : new Rgba(53, 194, 111, 135);
                WritePixel(rgba, outputResolution, outputX, displayY, color);
            }
        }

        return new MapRasterSnapshot(outputResolution, outputResolution, rgba);
    }

    private static MapRasterSnapshot? CreateElevationRaster(
        MapData? heightLayer,
        MapData? waterLayer,
        MapData? topologyLayer,
        int topologyResolution,
        uint worldSize)
    {
        if (heightLayer is null)
        {
            return null;
        }

        var heightResolution = GetInt16Resolution(heightLayer, "height");
        var waterResolution = waterLayer is null ? 0 : GetInt16Resolution(waterLayer, "water");
        var outputResolution = Math.Min(heightResolution, PreviewResolution);
        var samples = new double[outputResolution, outputResolution];
        var wet = new bool[outputResolution, outputResolution];
        for (var y = 0; y < outputResolution; y++)
        {
            var sourceY = SampleCoordinate(y, outputResolution, heightResolution);
            for (var x = 0; x < outputResolution; x++)
            {
                var sourceX = SampleCoordinate(x, outputResolution, heightResolution);
                var height = HeightMetres(heightLayer.Data, heightResolution, sourceX, sourceY);
                samples[y, x] = height;
                wet[y, x] = WaterDepthMetres(
                    height,
                    sourceX,
                    sourceY,
                    heightResolution,
                    waterLayer,
                    waterResolution,
                    topologyLayer,
                    topologyResolution) > 0.1;
            }
        }

        var rgba = new byte[checked(outputResolution * outputResolution * 4)];
        for (var y = 0; y < outputResolution; y++)
        {
            var displayY = outputResolution - 1 - y;
            for (var x = 0; x < outputResolution; x++)
            {
                if (wet[y, x])
                {
                    continue;
                }

                var height = samples[y, x];
                var minorContour = (x > 0 && ContourBand(samples[y, x - 1], 25) != ContourBand(height, 25))
                    || (y > 0 && ContourBand(samples[y - 1, x], 25) != ContourBand(height, 25));
                var majorContour = (x > 0 && ContourBand(samples[y, x - 1], 100) != ContourBand(height, 100))
                    || (y > 0 && ContourBand(samples[y - 1, x], 100) != ContourBand(height, 100));
                var color = majorContour
                    ? new Rgba(245, 244, 232, 220)
                    : minorContour
                        ? new Rgba(232, 225, 205, 155)
                        : ElevationColor(height);
                WritePixel(rgba, outputResolution, x, displayY, color);
            }
        }

        return new MapRasterSnapshot(outputResolution, outputResolution, rgba);
    }

    private static MapRasterSnapshot? CreateWaterDepthRaster(
        MapData? heightLayer,
        MapData? waterLayer,
        MapData? topologyLayer,
        int topologyResolution,
        uint worldSize)
    {
        if (heightLayer is null)
        {
            return null;
        }

        var heightResolution = GetInt16Resolution(heightLayer, "height");
        var waterResolution = waterLayer is null ? 0 : GetInt16Resolution(waterLayer, "water");
        var outputResolution = Math.Min(heightResolution, PreviewResolution);
        var depths = new double[outputResolution, outputResolution];
        for (var y = 0; y < outputResolution; y++)
        {
            var sourceY = SampleCoordinate(y, outputResolution, heightResolution);
            for (var x = 0; x < outputResolution; x++)
            {
                var sourceX = SampleCoordinate(x, outputResolution, heightResolution);
                var terrainHeight = HeightMetres(heightLayer.Data, heightResolution, sourceX, sourceY);
                depths[y, x] = WaterDepthMetres(
                    terrainHeight,
                    sourceX,
                    sourceY,
                    heightResolution,
                    waterLayer,
                    waterResolution,
                    topologyLayer,
                    topologyResolution);
            }
        }

        var rgba = new byte[checked(outputResolution * outputResolution * 4)];
        for (var y = 0; y < outputResolution; y++)
        {
            var displayY = outputResolution - 1 - y;
            for (var x = 0; x < outputResolution; x++)
            {
                var depth = depths[y, x];
                if (depth <= 0.1)
                {
                    continue;
                }

                var shoreline = (x > 0 && depths[y, x - 1] <= 0.1)
                    || (x + 1 < outputResolution && depths[y, x + 1] <= 0.1)
                    || (y > 0 && depths[y - 1, x] <= 0.1)
                    || (y + 1 < outputResolution && depths[y + 1, x] <= 0.1);
                var color = shoreline
                    ? new Rgba(198, 244, 255, 220)
                    : depth <= 1
                        ? new Rgba(107, 211, 232, 120)
                        : depth <= 5
                            ? new Rgba(48, 158, 204, 140)
                            : depth <= 20
                                ? new Rgba(34, 104, 174, 160)
                                : new Rgba(25, 55, 118, 185);
                WritePixel(rgba, outputResolution, x, displayY, color);
            }
        }

        return new MapRasterSnapshot(outputResolution, outputResolution, rgba);
    }

    private static bool[] CreateKnownBlockedMask(
        WorldData world,
        IReadOnlyList<MapNoBuildZoneSnapshot> noBuildZones,
        int resolution)
    {
        var blocked = new bool[checked(resolution * resolution)];
        foreach (var zone in noBuildZones.Where(zone => zone.Boundary.Count >= 3))
        {
            FillPolygon(blocked, resolution, world.Size, zone.Boundary);
        }

        foreach (var path in world.Paths.Where(path => ClassifyPath(path.Name) is MapPathKind.Road or MapPathKind.Railway))
        {
            var radius = Math.Max(1, (path.Width / 2d) + path.OuterPadding);
            for (var index = 1; index < path.Nodes.Count; index++)
            {
                FillSegment(blocked, resolution, world.Size, path.Nodes[index - 1], path.Nodes[index], radius);
            }
        }

        return blocked;
    }

    private static void FillPolygon(
        bool[] target,
        int resolution,
        uint worldSize,
        IReadOnlyList<MapWorldPoint> boundary)
    {
        var minX = Math.Max(0, (int)Math.Floor(boundary.Min(point => point.X) * resolution / worldSize));
        var maxX = Math.Min(resolution - 1, (int)Math.Ceiling(boundary.Max(point => point.X) * resolution / worldSize));
        var minY = Math.Max(0, (int)Math.Floor((worldSize - boundary.Max(point => point.Y)) * resolution / worldSize));
        var maxY = Math.Min(resolution - 1, (int)Math.Ceiling((worldSize - boundary.Min(point => point.Y)) * resolution / worldSize));
        for (var y = minY; y <= maxY; y++)
        {
            var worldY = worldSize - ((y + 0.5) * worldSize / resolution);
            for (var x = minX; x <= maxX; x++)
            {
                var worldX = (x + 0.5) * worldSize / resolution;
                if (PointInPolygon(worldX, worldY, boundary))
                {
                    target[(y * resolution) + x] = true;
                }
            }
        }
    }

    private static bool PointInPolygon(double x, double y, IReadOnlyList<MapWorldPoint> boundary)
    {
        var inside = false;
        for (int current = 0, previous = boundary.Count - 1; current < boundary.Count; previous = current++)
        {
            var a = boundary[current];
            var b = boundary[previous];
            if ((a.Y > y) != (b.Y > y)
                && x < ((b.X - a.X) * (y - a.Y) / (b.Y - a.Y)) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static void FillSegment(
        bool[] target,
        int resolution,
        uint worldSize,
        VectorData start,
        VectorData end,
        double radius)
    {
        var half = worldSize / 2d;
        var startX = start.X + half;
        var startY = start.Z + half;
        var endX = end.X + half;
        var endY = end.Z + half;
        var minX = Math.Max(0, (int)Math.Floor((Math.Min(startX, endX) - radius) * resolution / worldSize));
        var maxX = Math.Min(resolution - 1, (int)Math.Ceiling((Math.Max(startX, endX) + radius) * resolution / worldSize));
        var minY = Math.Max(0, (int)Math.Floor((worldSize - Math.Max(startY, endY) - radius) * resolution / worldSize));
        var maxY = Math.Min(resolution - 1, (int)Math.Ceiling((worldSize - Math.Min(startY, endY) + radius) * resolution / worldSize));
        for (var y = minY; y <= maxY; y++)
        {
            var worldY = worldSize - ((y + 0.5) * worldSize / resolution);
            for (var x = minX; x <= maxX; x++)
            {
                var worldX = (x + 0.5) * worldSize / resolution;
                if (DistanceToSegment(worldX, worldY, startX, startY, endX, endY) <= radius)
                {
                    target[(y * resolution) + x] = true;
                }
            }
        }
    }

    private static double DistanceToSegment(
        double x,
        double y,
        double startX,
        double startY,
        double endX,
        double endY)
    {
        var dx = endX - startX;
        var dy = endY - startY;
        var lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared <= double.Epsilon)
        {
            return Math.Sqrt(((x - startX) * (x - startX)) + ((y - startY) * (y - startY)));
        }

        var t = Math.Clamp((((x - startX) * dx) + ((y - startY) * dy)) / lengthSquared, 0, 1);
        var nearestX = startX + (t * dx);
        var nearestY = startY + (t * dy);
        return Math.Sqrt(((x - nearestX) * (x - nearestX)) + ((y - nearestY) * (y - nearestY)));
    }

    private static bool IsWater(
        double terrainHeight,
        int sourceX,
        int sourceY,
        int heightResolution,
        MapData? waterLayer,
        int waterResolution,
        MapData? topologyLayer,
        int topologyResolution)
    {
        if (waterLayer is not null)
        {
            var waterX = Math.Min(waterResolution - 1, sourceX * waterResolution / heightResolution);
            var waterY = Math.Min(waterResolution - 1, sourceY * waterResolution / heightResolution);
            if (HeightMetres(waterLayer.Data, waterResolution, waterX, waterY) > terrainHeight + 0.1)
            {
                return true;
            }
        }

        if (topologyLayer is null || topologyResolution <= 0 || terrainHeight >= 0)
        {
            return false;
        }

        var topologyX = Math.Min(topologyResolution - 1, sourceX * topologyResolution / heightResolution);
        var topologyY = Math.Min(topologyResolution - 1, sourceY * topologyResolution / heightResolution);
        var offset = ((topologyY * topologyResolution) + topologyX) * 4;
        var topology = BinaryPrimitives.ReadUInt32LittleEndian(topologyLayer.Data.AsSpan(offset, 4));
        return HasAny(topology, 7, 8);
    }

    private static double WaterDepthMetres(
        double terrainHeight,
        int sourceX,
        int sourceY,
        int heightResolution,
        MapData? waterLayer,
        int waterResolution,
        MapData? topologyLayer,
        int topologyResolution)
    {
        var waterHeight = -500d;
        if (waterLayer is not null)
        {
            var waterX = Math.Min(waterResolution - 1, sourceX * waterResolution / heightResolution);
            var waterY = Math.Min(waterResolution - 1, sourceY * waterResolution / heightResolution);
            waterHeight = HeightMetres(waterLayer.Data, waterResolution, waterX, waterY);
        }

        var topology = TopologyAt(
            sourceX,
            sourceY,
            heightResolution,
            topologyLayer,
            topologyResolution);
        if (HasAny(topology, 7, 8) && waterHeight < 0)
        {
            waterHeight = 0;
        }

        return Math.Max(0, waterHeight - terrainHeight);
    }

    private static uint TopologyAt(
        int sourceX,
        int sourceY,
        int sourceResolution,
        MapData? topologyLayer,
        int topologyResolution)
    {
        if (topologyLayer is null || topologyResolution <= 0)
        {
            return 0;
        }

        var topologyX = Math.Min(topologyResolution - 1, sourceX * topologyResolution / sourceResolution);
        var topologyY = Math.Min(topologyResolution - 1, sourceY * topologyResolution / sourceResolution);
        var offset = ((topologyY * topologyResolution) + topologyX) * 4;
        return BinaryPrimitives.ReadUInt32LittleEndian(topologyLayer.Data.AsSpan(offset, 4));
    }

    private static double SlopeDegrees(
        byte[] heightData,
        int resolution,
        int x,
        int y,
        double metresPerSample)
    {
        var minX = Math.Max(0, x - 1);
        var maxX = Math.Min(resolution - 1, x + 1);
        var minY = Math.Max(0, y - 1);
        var maxY = Math.Min(resolution - 1, y + 1);
        var dx = (HeightMetres(heightData, resolution, maxX, y) - HeightMetres(heightData, resolution, minX, y))
            / (Math.Max(1, maxX - minX) * metresPerSample);
        var dz = (HeightMetres(heightData, resolution, x, maxY) - HeightMetres(heightData, resolution, x, minY))
            / (Math.Max(1, maxY - minY) * metresPerSample);
        return Math.Atan(Math.Sqrt((dx * dx) + (dz * dz))) * 180d / Math.PI;
    }

    private static int SampleCoordinate(int output, int outputResolution, int sourceResolution) =>
        Math.Clamp((int)((output + 0.5) * sourceResolution / outputResolution), 0, sourceResolution - 1);

    private static long ContourBand(double height, int interval) => (long)Math.Floor(height / interval);

    private static Rgba ElevationColor(double height)
    {
        if (height <= 25)
        {
            return new Rgba(63, 145, 82, 65);
        }

        if (height <= 100)
        {
            return new Rgba(166, 166, 78, 70);
        }

        return height <= 200
            ? new Rgba(180, 111, 69, 80)
            : new Rgba(137, 93, 157, 95);
    }

    private static void WritePixel(byte[] rgba, int resolution, int x, int y, Rgba color)
    {
        var offset = ((y * resolution) + x) * 4;
        rgba[offset] = color.R;
        rgba[offset + 1] = color.G;
        rgba[offset + 2] = color.B;
        rgba[offset + 3] = color.A;
    }

    private static int GetInt16Resolution(MapData layer, string name)
    {
        if (layer.Data.Length % 2 != 0)
        {
            throw new InvalidDataException($"The {name} layer byte count is invalid.");
        }

        var resolution = GetSquareResolution(layer.Data.Length / 2, name);
        if (resolution < 2)
        {
            throw new InvalidDataException($"The {name} layer is too small for slope analysis.");
        }

        return resolution;
    }

    private static double HeightMetres(byte[] data, int resolution, int x, int y)
    {
        const double shortNormalizer = 32766d;
        const double verticalSize = 1000d;
        const double verticalOffset = -500d;
        var offset = ((y * resolution) + x) * 2;
        var normalized = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset, 2)) / shortNormalizer;
        return (normalized * verticalSize) + verticalOffset;
    }

    private static Rgba SlopeColor(double slopeDegrees)
    {
        if (slopeDegrees <= 5)
        {
            return new Rgba(53, 194, 111, 135);
        }

        if (slopeDegrees <= 12)
        {
            return new Rgba(184, 205, 74, 135);
        }

        return slopeDegrees <= 25
            ? new Rgba(244, 153, 53, 150)
            : new Rgba(216, 61, 50, 170);
    }

    private static Rgba TopologyColor(uint value)
    {
        if (HasAny(value, 7, 14, 16))
        {
            return new Rgba(44, 132, 174, 115);
        }

        if (HasAny(value, 10, 11, 20, 21))
        {
            return new Rgba(210, 64, 50, 120);
        }

        if (HasAny(value, 1, 22, 23))
        {
            return new Rgba(166, 149, 129, 120);
        }

        if (HasAny(value, 5, 6))
        {
            return new Rgba(42, 121, 71, 105);
        }

        if (HasAny(value, 0, 9, 24))
        {
            return new Rgba(190, 156, 74, 90);
        }

        return default;
    }

    private static Rgba ResourceColor(uint value)
    {
        if (HasAny(value, 22))
        {
            return new Rgba(255, 116, 38, 175);
        }

        return HasAny(value, 9, 24)
            ? new Rgba(255, 202, 58, 90)
            : default;
    }

    private static Rgba BiomeColor(int channel) => channel switch
    {
        0 => new Rgba(216, 159, 73, 95),
        1 => new Rgba(80, 143, 73, 95),
        2 => new Rgba(139, 145, 121, 95),
        3 => new Rgba(205, 229, 234, 105),
        4 => new Rgba(22, 112, 61, 110),
        _ => default
    };

    private static bool HasAny(uint value, params int[] bits) =>
        bits.Any(bit => (value & (1u << bit)) != 0);

    private static MapData? FindLayer(WorldData world, string name) =>
        world.Maps.FirstOrDefault(layer => string.Equals(layer.Name, name, StringComparison.OrdinalIgnoreCase));

    private static int GetSquareResolution(int elementCount, string layerName)
    {
        var resolution = (int)Math.Sqrt(elementCount);
        if (resolution <= 0 || checked(resolution * resolution) != elementCount)
        {
            throw new InvalidDataException($"The {layerName} layer is not a square grid.");
        }

        return resolution;
    }

    private static void ValidateWorld(WorldData world)
    {
        if (world.Size is < 1000 or > 10000)
        {
            throw new InvalidDataException("The Rust map contains an invalid world size.");
        }

        if (world.Maps.Count == 0)
        {
            throw new InvalidDataException("The Rust map contains no terrain layers.");
        }

        if (world.Paths.Sum(path => (long)path.Nodes.Count) > MaximumPathNodes)
        {
            throw new InvalidDataException("The Rust map contains too many path nodes.");
        }

        var topology = FindLayer(world, "topology")
            ?? throw new InvalidDataException("The Rust map does not contain a topology layer.");
        if (topology.Data.Length == 0 || topology.Data.Length % 4 != 0)
        {
            throw new InvalidDataException("The Rust map topology layer is invalid.");
        }
    }

    private static async Task CopyWithLimitAsync(
        Stream source,
        Stream destination,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[128 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }

            total += read;
            if (total > maximumBytes)
            {
                throw new InvalidDataException("The decoded Rust map exceeds the 1 GiB safety limit.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private enum ResourceMode
    {
        AllTopology,
        ResourcePotential
    }

    private readonly record struct Rgba(byte R, byte G, byte B, byte A);
}
