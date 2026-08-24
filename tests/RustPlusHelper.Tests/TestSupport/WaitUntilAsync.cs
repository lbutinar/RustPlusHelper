namespace RustPlusHelper.Tests;

/// <summary>
/// Shared polling helpers for tests that need to wait for an asynchronous
/// condition to become true, bounded by a timeout.
/// </summary>
internal static class AsyncTestHelpers
{
    /// <summary>
    /// Polls <paramref name="condition"/> until it returns <c>true</c> or
    /// <paramref name="timeout"/> elapses, sleeping <paramref name="pollInterval"/>
    /// between checks.
    /// </summary>
    public static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, TimeSpan pollInterval)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        while (!condition())
        {
            await Task.Delay(pollInterval, timeoutSource.Token);
        }
    }
}
