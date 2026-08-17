namespace RustPlusHelper.Application.RustPlus;

public sealed record RustPlusPollingOptions(
    TimeSpan TeamInterval,
    TimeSpan ChatInterval,
    TimeSpan MarkerInterval,
    TimeSpan ServerInfoInterval,
    TimeSpan ConnectTimeout,
    IReadOnlyList<TimeSpan> ReconnectDelays)
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
        ]);
}
