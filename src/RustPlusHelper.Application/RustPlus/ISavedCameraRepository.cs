namespace RustPlusHelper.Application.RustPlus;

/// <summary>A user-entered known camera code for a saved server. Rust+ has no camera discovery —
/// the user must already know the in-game code (e.g. from a computer station).</summary>
public sealed record SavedCamera(Guid Id, Guid ServerId, string Code, string Nickname, DateTimeOffset CreatedUtc);

public interface ISavedCameraRepository
{
    IReadOnlyList<SavedCamera> GetAll(Guid serverId);

    void Add(SavedCamera camera);

    bool Remove(Guid serverId, Guid id);
}
