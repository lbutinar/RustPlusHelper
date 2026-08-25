namespace RustPlusHelper.Application.RustPlus;

public interface IMovementTrailRepository
{
    /// <summary>Every stored point for the given server, grouped by member and ordered oldest first.</summary>
    IReadOnlyDictionary<ulong, IReadOnlyList<MovementTrailPoint>> GetAll(Guid serverId);

    void Append(Guid serverId, ulong steamId, MovementTrailPoint point);

    /// <summary>Deletes points older than <paramref name="cutoffUtc"/> across every server — a
    /// storage-level safety cap independent of any server's wipe time, since a long-lived unwiped
    /// server would otherwise grow this table forever. Intended to run once at application startup,
    /// like <see cref="ICompanionEventRepository.PurgeOlderThan"/>.</summary>
    void PurgeOlderThan(DateTimeOffset cutoffUtc);
}
