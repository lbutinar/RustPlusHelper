namespace RustPlusHelper.Application.RustPlus;

public interface ICompanionEventRepository
{
    IReadOnlyList<CompanionEvent> GetRecent(Guid serverId, int limit);

    /// <summary>Persists <paramref name="companionEvent"/>, then trims its server's history to
    /// <paramref name="retentionLimit"/> rows AND drops anything older than
    /// <paramref name="minRetainedUtc"/> — whichever condition matches first.</summary>
    void Append(CompanionEvent companionEvent, int retentionLimit, DateTimeOffset minRetainedUtc);

    /// <summary>Deletes events older than <paramref name="cutoffUtc"/> across every server, not just
    /// the one most recently appended to — a server whose live session hasn't run in a while never
    /// triggers another <see cref="Append"/>, so its old rows would otherwise outlive the age cap
    /// indefinitely. Intended to run once at application startup.</summary>
    void PurgeOlderThan(DateTimeOffset cutoffUtc);
}
