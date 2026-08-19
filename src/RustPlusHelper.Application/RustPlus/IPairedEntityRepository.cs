using RustPlusHelper.Application.Pairing;

namespace RustPlusHelper.Application.RustPlus;

/// <summary>A Smart Switch/Alarm/Storage Monitor paired to a saved server via the Rust+ FCM
/// entity-pairing notification. Rust+ has no device discovery beyond this one-time capture.</summary>
public sealed record PairedEntity(
    Guid Id,
    Guid ServerId,
    ulong EntityId,
    PairedEntityKind Kind,
    string Nickname,
    DateTimeOffset CreatedUtc);

public interface IPairedEntityRepository
{
    IReadOnlyList<PairedEntity> GetAll(Guid serverId);

    void Add(PairedEntity entity);

    bool Remove(Guid serverId, Guid id);
}
