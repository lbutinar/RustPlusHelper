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
