using RustPlusHelper.Application.Security;
using RustPlusHelper.Application.Servers;

namespace RustPlusHelper.Application.Identity;

public sealed class PlayerIdentityManager(
    IPlayerIdentityRepository repository,
    TimeProvider timeProvider,
    IServerRepository? serverRepository = null,
    ISecretStore? secretStore = null)
{
    public event EventHandler? Changed;

    public PlayerIdentity? Current { get; private set; }

    public void Load() => Current = repository.Get();

    public PlayerIdentity Save(ulong steamId)
    {
        if (steamId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(steamId), "Steam64 ID must be greater than zero.");
        }

        if (Current is not null
            && Current.SteamId != steamId
            && serverRepository is not null
            && secretStore is not null
            && serverRepository.GetAll().Any(profile =>
                secretStore.Contains(profile.Id, SecretKind.RustPlusPlayerToken)))
        {
            throw new InvalidOperationException(
                "Remove or re-pair saved servers before changing the Steam64 identity.");
        }

        var identity = new PlayerIdentity(steamId, timeProvider.GetUtcNow());
        repository.Upsert(identity);
        Current = identity;
        Changed?.Invoke(this, EventArgs.Empty);
        return identity;
    }
}
