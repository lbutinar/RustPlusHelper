using RustPlusHelper.Application.RustPlus;

namespace RustPlusHelper.Application.Testing;

public sealed class InMemoryCompanionEventRepository : ICompanionEventRepository
{
    private readonly Lock _lock = new();
    private readonly List<CompanionEvent> _events = [];

    public IReadOnlyList<CompanionEvent> GetRecent(Guid serverId, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        lock (_lock)
        {
            return _events
                .Where(item => item.ServerId == serverId)
                .OrderByDescending(item => item.OccurredAtUtc)
                .ThenByDescending(item => item.Id)
                .Take(limit)
                .ToArray();
        }
    }

    public void Append(CompanionEvent companionEvent, int retentionLimit, DateTimeOffset minRetainedUtc)
    {
        ArgumentNullException.ThrowIfNull(companionEvent);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(retentionLimit);
        lock (_lock)
        {
            _events.Add(companionEvent);
            var retainedIds = _events
                .Where(item => item.ServerId == companionEvent.ServerId)
                .OrderByDescending(item => item.OccurredAtUtc)
                .ThenByDescending(item => item.Id)
                .Take(retentionLimit)
                .Select(item => item.Id)
                .ToHashSet();
            _events.RemoveAll(item =>
                item.ServerId == companionEvent.ServerId
                && (!retainedIds.Contains(item.Id) || item.OccurredAtUtc < minRetainedUtc));
        }
    }

    public void PurgeOlderThan(DateTimeOffset cutoffUtc)
    {
        lock (_lock)
        {
            _events.RemoveAll(item => item.OccurredAtUtc < cutoffUtc);
        }
    }
}
