using RustPlusHelper.Application.Map;

namespace RustPlusHelper.Application.Testing;

public sealed class InMemoryMapTopologyRepository : IMapTopologyRepository
{
    private readonly Dictionary<Guid, SavedMapTopology> _entries = [];

    public SavedMapTopology? Get(Guid serverId) =>
        _entries.TryGetValue(serverId, out var topology) ? topology : null;

    public void Upsert(SavedMapTopology topology)
    {
        ArgumentNullException.ThrowIfNull(topology);
        _entries[topology.ServerId] = topology;
    }
}

public sealed class UnavailableMapTopologyProvider : IMapTopologyProvider
{
    public Task<ImportedMapTopology> ReadAsync(
        string filePath,
        CancellationToken cancellationToken = default) =>
        Task.FromException<ImportedMapTopology>(
            new InvalidDataException("Rust map import is unavailable in this test source."));
}

public sealed class UnavailableMapTopologyDiscovery : IMapTopologyDiscovery
{
    public Task<MapTopologyDiscoveryResult> DiscoverAsync(
        MapTopologyDiscoveryRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(MapTopologyDiscoveryResult.NotFound(
            "No automatically matched Rust map was found. Choose a .map file manually."));
}
