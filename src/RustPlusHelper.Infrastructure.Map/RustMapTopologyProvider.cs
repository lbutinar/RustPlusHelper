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
            noBuildEvidence);
    }

    private static MapPathSnapshot ToSnapshot(PathData path, uint worldSize)
    {
        var halfSize = worldSize / 2f;
        return new MapPathSnapshot(
            path.Name,
            ClassifyPath(path.Name),
            path.Width,
            path.Nodes.Select(node => new MapWorldPoint(node.X + halfSize, node.Z + halfSize)).ToArray());
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
