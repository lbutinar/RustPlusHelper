using RustPlusHelper.Application.Map;

namespace RustPlusHelper.Application.Testing;

public sealed class InMemoryMapCacheRepository : IMapCacheRepository
{
    private readonly Dictionary<Guid, CachedServerMap> _entries = [];

    public CachedServerMap? Get(Guid serverId) =>
        _entries.TryGetValue(serverId, out var cached) ? cached : null;

    public void Upsert(CachedServerMap cachedMap)
    {
        ArgumentNullException.ThrowIfNull(cachedMap);
        _entries[cachedMap.ServerId] = cachedMap;
    }
}
