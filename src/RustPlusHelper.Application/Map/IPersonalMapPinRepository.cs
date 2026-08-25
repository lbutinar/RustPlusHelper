namespace RustPlusHelper.Application.Map;

/// <summary>A user-placed labeled pin on a saved server's map. Rust+ has no concept of personal
/// map annotations — these are entirely local and never sent to the game server.</summary>
public sealed record PersonalMapPin(Guid Id, Guid ServerId, float WorldX, float WorldY, string Note, DateTimeOffset CreatedUtc);

public interface IPersonalMapPinRepository
{
    IReadOnlyList<PersonalMapPin> GetAll(Guid serverId);

    void Add(PersonalMapPin pin);

    bool Remove(Guid serverId, Guid id);
}
