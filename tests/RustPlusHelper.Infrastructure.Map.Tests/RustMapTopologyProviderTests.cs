using System.Buffers.Binary;
using K4os.Compression.LZ4.Legacy;
using ProtoBuf;
using RustPlusHelper.Application.Map;
using RustPlusHelper.Infrastructure.Map;

namespace RustPlusHelper.Infrastructure.Map.Tests;

public sealed class RustMapTopologyProviderTests
{
    [Fact]
    public async Task DecodesDocumentedRustMapContainerAndProducesDisplayLayers()
    {
        using var mapFile = TestMapFile.Create();
        var provider = new RustMapTopologyProvider();

        var result = await provider.ReadAsync(mapFile.Path);

        Assert.Equal(10, result.SerializationVersion);
        Assert.Equal(4500u, result.WorldSize);
        Assert.Equal("fixture.4500.123.map", result.SourceFileName);
        Assert.Equal(64, result.Sha256.Length);
        Assert.Equal(2, result.SourceLayers.Count);
        Assert.Equal(3, result.PrefabCount);

        var road = Assert.Single(result.Paths);
        Assert.Equal(MapPathKind.Road, road.Kind);
        Assert.Equal(new MapWorldPoint(0, 0), road.Nodes[0]);
        Assert.Equal(new MapWorldPoint(4500, 4500), road.Nodes[1]);

        Assert.NotNull(result.TopologyRaster);
        Assert.Equal(2, result.TopologyRaster.Width);
        Assert.Equal(result.TopologyRaster.ExpectedByteCount, result.TopologyRaster.Rgba.Length);
        Assert.Contains(result.TopologyRaster.Rgba, value => value != 0);
        Assert.NotNull(result.BiomeRaster);
        Assert.NotNull(result.ResourcePotentialRaster);
        Assert.Contains(result.ResourcePotentialRaster.Rgba, value => value != 0);
        var noBuildZone = Assert.Single(result.NoBuildZones!);
        Assert.Equal("circle", noBuildZone.Shape);
        Assert.Equal(40, noBuildZone.Boundary.Count);
        Assert.Equal(2390f, noBuildZone.Boundary.Max(point => point.X), 2);
        Assert.Equal(2250f, noBuildZone.Boundary.Average(point => point.Y), 2);
        Assert.Equal("24181174", result.NoBuildZoneEvidence?.CatalogRustBuildId);
        Assert.Equal(1, result.NoBuildZoneEvidence?.ResolvedPrefabCount);
    }

    [Fact]
    public async Task RejectsUnknownWorldSerializationVersionBeforeDecoding()
    {
        using var mapFile = TestMapFile.Create(serializationVersion: 99);
        var provider = new RustMapTopologyProvider();

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => provider.ReadAsync(mapFile.Path));

        Assert.Contains("version 99", exception.Message, StringComparison.Ordinal);
    }

    private sealed class TestMapFile : IDisposable
    {
        private TestMapFile(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TestMapFile Create(int serializationVersion = 10)
        {
            var directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "RustPlusHelper.Map.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, "fixture.4500.123.map");
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            Span<byte> header = stackalloc byte[12];
            BinaryPrimitives.WriteInt32LittleEndian(header[..4], serializationVersion);
            BinaryPrimitives.WriteUInt64LittleEndian(header[4..], 123456789UL);
            file.Write(header);

            if (serializationVersion == 10)
            {
                var world = CreateWorld();
                using var encoded = LZ4Legacy.Encode(file, leaveOpen: true);
                Serializer.Serialize(encoded, world);
            }

            return new TestMapFile(path);
        }

        public void Dispose() => Directory.Delete(System.IO.Path.GetDirectoryName(Path)!, recursive: true);

        private static TestWorldData CreateWorld()
        {
            var topology = new byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(topology.AsSpan(0, 4), 1u << 5);
            BinaryPrimitives.WriteUInt32LittleEndian(topology.AsSpan(4, 4), 1u << 14);
            BinaryPrimitives.WriteUInt32LittleEndian(topology.AsSpan(8, 4), 1u << 22);
            BinaryPrimitives.WriteUInt32LittleEndian(topology.AsSpan(12, 4), 1u << 21);
            return new TestWorldData
            {
                Size = 4500,
                Maps =
                {
                    new TestMapData { Name = "topology", Data = topology },
                    new TestMapData
                    {
                        Name = "biome",
                        Data =
                        [
                            255, 0, 0, 0,
                            0, 255, 0, 0,
                            0, 0, 255, 0,
                            0, 0, 0, 255,
                            0, 0, 0, 0
                        ]
                    }
                },
                Prefabs =
                {
                    new()
                    {
                        Id = 3968358155,
                        Position = new TestVectorData(),
                        Scale = new TestVectorData { X = 1, Y = 1, Z = 1 }
                    },
                    new(),
                    new()
                },
                Paths =
                {
                    new TestPathData
                    {
                        Name = "Road 0",
                        Width = 12,
                        Nodes =
                        {
                            new TestVectorData { X = -2250, Z = -2250 },
                            new TestVectorData { X = 2250, Z = 2250 }
                        }
                    }
                }
            };
        }
    }

    [ProtoContract]
    private sealed class TestWorldData
    {
        [ProtoMember(1)] public uint Size { get; set; }
        [ProtoMember(2)] public List<TestMapData> Maps { get; } = [];
        [ProtoMember(3)] public List<TestPrefabData> Prefabs { get; } = [];
        [ProtoMember(4)] public List<TestPathData> Paths { get; } = [];
    }

    [ProtoContract]
    private sealed class TestMapData
    {
        [ProtoMember(1)] public string Name { get; set; } = string.Empty;
        [ProtoMember(2)] public byte[] Data { get; set; } = [];
    }

    [ProtoContract]
    private sealed class TestPrefabData
    {
        [ProtoMember(1)] public string Category { get; set; } = string.Empty;
        [ProtoMember(2)] public uint Id { get; set; }
        [ProtoMember(3)] public TestVectorData? Position { get; set; }
        [ProtoMember(4)] public TestVectorData? Rotation { get; set; }
        [ProtoMember(5)] public TestVectorData? Scale { get; set; }
    }

    [ProtoContract]
    private sealed class TestVectorData
    {
        [ProtoMember(1)] public float X { get; set; }
        [ProtoMember(2)] public float Y { get; set; }
        [ProtoMember(3)] public float Z { get; set; }
    }

    [ProtoContract]
    private sealed class TestPathData
    {
        [ProtoMember(1)] public string Name { get; set; } = string.Empty;
        [ProtoMember(5)] public float Width { get; set; }
        [ProtoMember(15)] public List<TestVectorData> Nodes { get; } = [];
    }
}
