using RustPlusHelper.Application.RustPlus;

namespace RustPlusHelper.Application.Map;

public sealed record CachedServerMap(
    Guid ServerId,
    DateTimeOffset RetrievedAtUtc,
    ServerInfoSnapshot Server,
    ServerMapSnapshot Map);

/// <summary>Stores the latest successfully downloaded map snapshot for offline reopening.</summary>
public interface IMapCacheRepository
{
    CachedServerMap? Get(Guid serverId);

    void Upsert(CachedServerMap cachedMap);
}
