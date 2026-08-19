using RustPlusHelper.Application.RustPlus;

namespace RustPlusHelper.Application.Testing;

public sealed class InMemorySavedCameraRepository : ISavedCameraRepository
{
    private readonly Dictionary<Guid, SavedCamera> _cameras = [];

    public IReadOnlyList<SavedCamera> GetAll(Guid serverId) => _cameras.Values
        .Where(camera => camera.ServerId == serverId)
        .OrderBy(camera => camera.Nickname, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public void Add(SavedCamera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        _cameras[camera.Id] = camera;
    }

    public bool Remove(Guid serverId, Guid id) =>
        _cameras.TryGetValue(id, out var camera)
        && camera.ServerId == serverId
        && _cameras.Remove(id);
}
