using System.IO.Compression;
using System.Text;
using RustPlusHelper.Application.Diagnostics;
using RustPlusHelper.Application.Security;
using RustPlusHelper.Application.Servers;

namespace RustPlusHelper.Infrastructure.Storage.Diagnostics;

/// <summary>
/// Runs registered health checks and bundles a support export: app/OS version, health-check results,
/// an allowlisted (host- and player-ID-free) summary of saved servers, and redacted log files. Never
/// touches the SQLite file or any secret directly — see docs/local-storage.md's diagnostics-exporter
/// constraint.
/// </summary>
public sealed class DiagnosticsExportService(
    IEnumerable<IHealthCheck> healthChecks,
    IServerRepository serverRepository,
    TimeProvider timeProvider,
    string appVersion,
    string logsDirectory)
{
    public IReadOnlyList<HealthCheckResult> RunHealthChecks() => healthChecks.Select(RunSafely).ToArray();

    public void ExportTo(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        WriteSummary(archive);
        WriteServers(archive);
        WriteLogs(archive);
    }

    private static HealthCheckResult RunSafely(IHealthCheck check)
    {
        try
        {
            return check.Check();
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(check.Name, HealthStatus.Unhealthy, $"Health check threw: {ex.Message}");
        }
    }

    private void WriteSummary(ZipArchive archive)
    {
        var builder = new StringBuilder();
        builder.AppendLine("RustPlusHelper diagnostics export");
        builder.AppendLine($"Generated (UTC): {timeProvider.GetUtcNow():O}");
        builder.AppendLine($"App version: {appVersion}");
        builder.AppendLine(
            $"OS: {Environment.OSVersion.VersionString} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})");
        builder.AppendLine();
        builder.AppendLine("Health checks:");
        foreach (var result in RunHealthChecks())
        {
            builder.AppendLine($"  [{result.Status}] {result.Name} - {result.Detail}");
        }

        WriteEntry(archive, "summary.txt", builder.ToString());
    }

    private void WriteServers(ZipArchive archive)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Saved servers (host and player ID intentionally omitted):");
        foreach (var server in serverRepository.GetAll())
        {
            builder.AppendLine(
                $"  \"{server.DisplayName}\" - port {server.Port}, " +
                $"{(server.UseFacepunchProxy ? "secure proxy" : "direct ws:// (insecure)")}, " +
                $"created {server.CreatedUtc:yyyy-MM-dd}");
        }

        WriteEntry(archive, "servers.txt", builder.ToString());
    }

    private void WriteLogs(ZipArchive archive)
    {
        if (!Directory.Exists(logsDirectory))
        {
            return;
        }

        foreach (var logFile in Directory.EnumerateFiles(logsDirectory, "app-*.log").OrderBy(Path.GetFileName))
        {
            string content;
            try
            {
                using var stream = File.Open(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                content = reader.ReadToEnd();
            }
            catch (IOException)
            {
                continue;
            }

            // Log lines are already redacted when written; redact again defensively on export.
            WriteEntry(archive, $"logs/{Path.GetFileName(logFile)}", SecretRedactor.Redact(content));
        }
    }

    private static void WriteEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream);
        writer.Write(content);
    }
}
