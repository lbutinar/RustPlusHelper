using RustPlusHelper.Application.Diagnostics;
using RustPlusHelper.Infrastructure.Storage.Sqlite;

namespace RustPlusHelper.Infrastructure.Storage.Diagnostics;

public sealed class DatabaseHealthCheck(SqliteDatabase database) : IHealthCheck
{
    public string Name => "Local database";

    public HealthCheckResult Check()
    {
        try
        {
            database.Initialize();

            using var connection = database.OpenConnection();

            using var versionCommand = connection.CreateCommand();
            versionCommand.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
            var version = Convert.ToInt64(versionCommand.ExecuteScalar());

            using var integrityCommand = connection.CreateCommand();
            integrityCommand.CommandText = "PRAGMA integrity_check;";
            var integrity = Convert.ToString(integrityCommand.ExecuteScalar());

            if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return new HealthCheckResult(Name, HealthStatus.Unhealthy, $"Integrity check reported: {integrity}");
            }

            if (version != SqliteMigrationRunner.LatestVersion)
            {
                return new HealthCheckResult(
                    Name,
                    HealthStatus.Unhealthy,
                    $"Schema version {version} does not match the expected {SqliteMigrationRunner.LatestVersion}.");
            }

            return new HealthCheckResult(Name, HealthStatus.Healthy, $"Schema version {version}; integrity check passed.");
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(Name, HealthStatus.Unhealthy, $"Could not open the local database: {ex.Message}");
        }
    }
}
