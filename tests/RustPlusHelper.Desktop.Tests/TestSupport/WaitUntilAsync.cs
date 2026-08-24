namespace RustPlusHelper.Desktop.Tests;

/// <summary>
/// Shared polling helper for tests that need to wait for an asynchronous condition to become true,
/// bounded by a timeout. Mirrors <c>RustPlusHelper.Tests.AsyncTestHelpers.WaitUntilAsync</c> — kept as
/// a separate copy because test projects don't share a common test-support assembly, but consolidated
/// here so this project has exactly one implementation instead of one per test class.
/// </summary>
internal static class AsyncTestHelpers
{
    public static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, TimeSpan pollInterval)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        while (!condition())
        {
            await Task.Delay(pollInterval, timeoutSource.Token);
        }
    }
}
