namespace RustPlusHelper.Application.Identity;

public sealed record PlayerIdentity(
    ulong SteamId,
    DateTimeOffset UpdatedUtc);

public interface IPlayerIdentityRepository
{
    PlayerIdentity? Get();

    void Upsert(PlayerIdentity identity);
}
