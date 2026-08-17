using RustPlusHelper.Application.Map;
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
            repository,
            TimeProvider.System);

        var result = await manager.ImportAsync(ServerId, "matching.map", 4500);

        Assert.True(result.IsSuccess);
        Assert.NotNull(repository.Get(ServerId));
        Assert.Contains("no map checksum", result.Message, StringComparison.OrdinalIgnoreCase);
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

    private sealed class StubProvider(ImportedMapTopology topology) : IMapTopologyProvider
    {
        public Task<ImportedMapTopology> ReadAsync(
            string filePath,
            CancellationToken cancellationToken = default) => Task.FromResult(topology);
    }
}
