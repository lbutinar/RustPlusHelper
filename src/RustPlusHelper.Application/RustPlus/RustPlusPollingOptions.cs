namespace RustPlusHelper.Application.RustPlus;

public sealed record RustPlusPollingOptions(
    TimeSpan TeamInterval,
    TimeSpan ChatInterval,
    TimeSpan MarkerInterval,
    TimeSpan ServerInfoInterval,
    TimeSpan ConnectTimeout,
    IReadOnlyList<TimeSpan> ReconnectDelays,
    /// <summary>How often a member's position may be persisted to their movement trail. Configurable
    /// (rather than a hardcoded constant) so tests can shrink it instead of waiting out the real
    /// production interval.</summary>
    TimeSpan MovementTrailSampleInterval)
{
    public static RustPlusPollingOptions Default { get; } = new(
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(30),
        [
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30)
        ],
        TimeSpan.FromSeconds(90));
}
