namespace RustPlusHelper.Application.Concurrency;

/// <summary>
/// Guards a single mutable state value against lost updates when multiple threads each compute
/// "new state = f(old state)" concurrently — e.g. a background poll loop and a UI-driven refresh both
/// deriving their next state from the same snapshot at nearly the same time. Without this, whichever
/// writer finishes last silently discards the other's changes even though neither writer did anything
/// wrong on its own.
/// </summary>
/// <remarks>
/// <see cref="Update"/> reads the current value, applies <paramref name="updater"/>, and stores the
/// result, all under one lock, so two concurrent updates can never both compute from the same stale
/// snapshot. Keep <paramref name="updater"/> synchronous and side-effect-light — it runs while the
/// lock is held, so it should not await, and any event this class's owner raises in response to a
/// change belongs outside the lock (to avoid deadlocking a reentrant handler) — see
/// <c>MapDashboardService.UpdateState</c>/<c>RustPlusLiveSessionManager.UpdateState</c> for the pattern.
/// </remarks>
public sealed class MutableStateBox<T>(T initial)
{
    private readonly Lock _lock = new();
    private T _value = initial;

    public T Value
    {
        get
        {
            lock (_lock)
            {
                return _value;
            }
        }
    }

    /// <summary>Atomically reads the current value, computes the next value from it, stores it, and
    /// returns it — so the caller can decide what to do with the result (e.g. raise a changed event)
    /// outside the lock.</summary>
    public T Update(Func<T, T> updater)
    {
        lock (_lock)
        {
            _value = updater(_value);
            return _value;
        }
    }
}
