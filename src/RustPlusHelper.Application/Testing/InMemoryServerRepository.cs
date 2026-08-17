using RustPlusHelper.Application.Servers;

namespace RustPlusHelper.Application.Testing;

public sealed class InMemoryServerRepository : IServerRepository
{
    private readonly Dictionary<Guid, ServerProfile> _profiles = [];

    public IReadOnlyList<ServerProfile> GetAll() => _profiles.Values
        .OrderByDescending(profile => profile.LastSelectedUtc)
        .ThenBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public ServerProfile? GetById(Guid id) => _profiles.GetValueOrDefault(id);

    public void Upsert(ServerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _profiles[profile.Id] = profile;
    }

    public bool Remove(Guid id) => _profiles.Remove(id);
}
