using RustPlusHelper.Application.Map;
using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Application.Testing;

namespace RustPlusHelper.Tests;

public sealed class MapTopologyManagerTests
{
    private static readonly Guid ServerId = Guid.Parse("51945f5b-a14c-4443-bd34-8d3a00510467");

    [Fact]
    public async Task RejectsDefiniteWorldSizeMismatchWithoutReplacingSavedData()
    {
        var repository = new InMemoryMapTopologyRepository();
        var manager = new MapTopologyManager(
            new StubProvider(CreateImport(4000)),
            new UnavailableMapTopologyDiscovery(),
            repository,
            TimeProvider.System);

        var result = await manager.ImportAsync(ServerId, "different.map", 4500);

        Assert.False(result.IsSuccess);
        Assert.Contains("4000 m", result.Message, StringComparison.Ordinal);
        Assert.Null(repository.Get(ServerId));
    }

    [Fact]
    public async Task PersistsSizeMatchedImportButDoesNotClaimChecksumVerification()
    {
        var repository = new InMemoryMapTopologyRepository();
        var manager = new MapTopologyManager(
            new StubProvider(CreateImport(4500)),
            new UnavailableMapTopologyDiscovery(),
            repository,
            TimeProvider.System);

        var result = await manager.ImportAsync(ServerId, "matching.map", 4500);

        Assert.True(result.IsSuccess);
        Assert.NotNull(repository.Get(ServerId));
        Assert.Contains("no map checksum", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AutomaticallyImportsUniqueDiscoveredCacheMatch()
    {
        var repository = new InMemoryMapTopologyRepository();
        var provider = new StubProvider(CreateImport(4500));
        var manager = new MapTopologyManager(
            provider,
            new StubDiscovery(MapTopologyDiscoveryResult.Matched(
                new DiscoveredMapTopology(
                    "matching.map",
                    "matching.map",
                    1,
                    MapTopologyMatchKind.RustClientLog),
                "matched")),
            repository,
            TimeProvider.System);

        var result = await manager.TryAutoImportAsync(
            ServerId,
            "192.0.2.25",
            ServerInfo());

        Assert.True(result.WasImported);
        Assert.NotNull(repository.Get(ServerId));
        Assert.Equal(1, provider.ReadCount);
        Assert.Contains("client connection log", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReusesPersistedTopologyWhenCacheNameAndHeaderTimestampMatch()
    {
        var repository = new InMemoryMapTopologyRepository();
        repository.Upsert(new SavedMapTopology(ServerId, DateTimeOffset.UtcNow, CreateImport(4500)));
        var provider = new StubProvider(CreateImport(4500));
        var manager = new MapTopologyManager(
            provider,
            new StubDiscovery(MapTopologyDiscoveryResult.Matched(
                new DiscoveredMapTopology(
                    "matching.map",
                    "matching.map",
                    1,
                    MapTopologyMatchKind.RustClientLog),
                "matched")),
            repository,
            TimeProvider.System);

        var result = await manager.TryAutoImportAsync(
            ServerId,
            "192.0.2.25",
            ServerInfo());

        Assert.False(result.WasImported);
        Assert.NotNull(result.Topology);
        Assert.Equal(0, provider.ReadCount);
    }

    [Fact]
    public async Task RefreshesMatchingLegacyImportWhenHeightLayerHasNoSlopeRaster()
    {
        var legacy = CreateImport(4500) with
        {
            SourceLayers =
            [
                new MapSourceLayerSnapshot("topology", 16),
                new MapSourceLayerSnapshot("height", 50)
            ]
        };
        var upgraded = legacy with { TerrainSlopeRaster = new MapRasterSnapshot(1, 1, [53, 194, 111, 135]) };
        var repository = new InMemoryMapTopologyRepository();
        repository.Upsert(new SavedMapTopology(ServerId, DateTimeOffset.UtcNow, legacy));
        var provider = new StubProvider(upgraded);
        var manager = new MapTopologyManager(
            provider,
            new StubDiscovery(MapTopologyDiscoveryResult.Matched(
                new DiscoveredMapTopology(
                    "matching.map",
                    "matching.map",
                    1,
                    MapTopologyMatchKind.RustClientLog),
                "matched")),
            repository,
            TimeProvider.System);

        var result = await manager.TryAutoImportAsync(ServerId, "192.0.2.25", ServerInfo());

        Assert.True(result.WasImported);
        Assert.Equal(1, provider.ReadCount);
        Assert.NotNull(repository.Get(ServerId)?.Data.TerrainSlopeRaster);
    }

    private static ImportedMapTopology CreateImport(uint worldSize) => new(
        "matching.map",
        new string('A', 64),
        10,
        1,
        worldSize,
        [new MapSourceLayerSnapshot("topology", 16)],
        0,
        [],
        null,
        new MapRasterSnapshot(1, 1, [1, 2, 3, 4]),
        null);

    private static ServerInfoSnapshot ServerInfo() => new(
        "Test server",
        null,
        null,
        "Procedural Map",
        4500,
        DateTimeOffset.UtcNow.AddDays(-1),
        null,
        null,
        null,
        1234,
        5678,
        null,
        null,
        null,
        null);

    private sealed class StubProvider(ImportedMapTopology topology) : IMapTopologyProvider
    {
        public int ReadCount { get; private set; }

        public Task<ImportedMapTopology> ReadAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return Task.FromResult(topology);
        }
    }

    private sealed class StubDiscovery(MapTopologyDiscoveryResult result) : IMapTopologyDiscovery
    {
        public Task<MapTopologyDiscoveryResult> DiscoverAsync(
            MapTopologyDiscoveryRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }
}
