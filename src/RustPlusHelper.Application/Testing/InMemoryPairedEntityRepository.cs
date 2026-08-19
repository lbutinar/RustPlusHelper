using RustPlusHelper.Application.RustPlus;

namespace RustPlusHelper.Application.Testing;

public sealed class InMemoryPairedEntityRepository : IPairedEntityRepository
{
    private readonly Dictionary<Guid, PairedEntity> _entities = [];

    public IReadOnlyList<PairedEntity> GetAll(Guid serverId) => _entities.Values
        .Where(entity => entity.ServerId == serverId)
        .OrderBy(entity => entity.Nickname, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public void Add(PairedEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _entities[entity.Id] = entity;
    }

    public bool Remove(Guid serverId, Guid id) =>
        _entities.TryGetValue(id, out var entity)
        && entity.ServerId == serverId
        && _entities.Remove(id);
}
