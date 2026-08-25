using RustPlusHelper.Application.Map;

namespace RustPlusHelper.Application.Testing;

public sealed class InMemoryPersonalMapPinRepository : IPersonalMapPinRepository
{
    private readonly Dictionary<Guid, PersonalMapPin> _pins = [];

    public IReadOnlyList<PersonalMapPin> GetAll(Guid serverId) => _pins.Values
        .Where(pin => pin.ServerId == serverId)
        .OrderBy(pin => pin.CreatedUtc)
        .ToArray();

    public void Add(PersonalMapPin pin)
    {
        ArgumentNullException.ThrowIfNull(pin);
        _pins[pin.Id] = pin;
    }

    public bool Remove(Guid serverId, Guid id) =>
        _pins.TryGetValue(id, out var pin)
        && pin.ServerId == serverId
        && _pins.Remove(id);
}
