using Microsoft.Extensions.Logging;
using RustPlusHelper.Application.Security;

namespace RustPlusHelper.Infrastructure.Storage.Logging;

/// <summary>
/// Writes one redacted, allowlist-friendly line per log entry to a daily rolling file under
/// <see cref="ApplicationDataPaths.GetLogsDirectory"/>. Kept deliberately simple (no external logging
/// dependency): this app's log volume is low enough that a single lock around a synchronous append is
/// not a performance concern.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private static readonly TimeSpan RetentionAge = TimeSpan.FromDays(14);

    private readonly string _directory;
    private readonly TimeProvider _timeProvider;
    private readonly object _writeLock = new();
    private DateOnly _lastPurgeDate;

    public FileLoggerProvider(string directory, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _directory = directory;
        _timeProvider = timeProvider;
        Directory.CreateDirectory(_directory);
        _lastPurgeDate = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        PurgeExpiredFiles();
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    public void Dispose()
    {
    }

    internal void Append(string categoryName, LogLevel logLevel, string message, Exception? exception)
    {
        var now = _timeProvider.GetUtcNow();
        var line = $"{now:O} [{logLevel}] {categoryName}: {SecretRedactor.Redact(message)}";
        if (exception is not null)
        {
            line += Environment.NewLine + SecretRedactor.Redact(exception.ToString());
        }

        var path = Path.Combine(_directory, $"app-{now:yyyyMMdd}.log");
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        lock (_writeLock)
        {
            File.AppendAllText(path, line + Environment.NewLine);

            if (today != _lastPurgeDate)
            {
                _lastPurgeDate = today;
                PurgeExpiredFiles();
            }
        }
    }

    private void PurgeExpiredFiles()
    {
        var cutoffUtc = _timeProvider.GetUtcNow() - RetentionAge;
        foreach (var file in Directory.EnumerateFiles(_directory, "app-*.log"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoffUtc.UtcDateTime)
                {
                    File.Delete(file);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup; a locked or already-removed file is not fatal.
            }
        }
    }
}

internal sealed class FileLogger(string categoryName, FileLoggerProvider provider) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        provider.Append(categoryName, logLevel, formatter(state, exception), exception);
    }
}
