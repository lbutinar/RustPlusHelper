using RustPlusHelper.Application.Identity;

namespace RustPlusHelper.Application.Testing;

public sealed class InMemoryPlayerIdentityRepository : IPlayerIdentityRepository
{
    public PlayerIdentity? Identity { get; private set; }

    public PlayerIdentity? Get() => Identity;

    public void Upsert(PlayerIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        Identity = identity;
    }
}
