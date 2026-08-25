using RustPlusHelper.Application.RustPlus;

namespace RustPlusHelper.Application.Testing;

public sealed class InMemoryMovementTrailRepository : IMovementTrailRepository
{
    private readonly Lock _lock = new();
    private readonly List<(Guid ServerId, ulong SteamId, MovementTrailPoint Point)> _points = [];

    public IReadOnlyDictionary<ulong, IReadOnlyList<MovementTrailPoint>> GetAll(Guid serverId)
    {
        lock (_lock)
        {
            return _points
                .Where(item => item.ServerId == serverId)
                .GroupBy(item => item.SteamId)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<MovementTrailPoint>)group
                        .Select(item => item.Point)
                        .OrderBy(point => point.SampledAtUtc)
                        .ToArray());
        }
    }

    public void Append(Guid serverId, ulong steamId, MovementTrailPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        lock (_lock)
        {
            _points.Add((serverId, steamId, point));
        }
    }

    public void PurgeOlderThan(DateTimeOffset cutoffUtc)
    {
        lock (_lock)
        {
            _points.RemoveAll(item => item.Point.SampledAtUtc < cutoffUtc);
        }
    }
}
