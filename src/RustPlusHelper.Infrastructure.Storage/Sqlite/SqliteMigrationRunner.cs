using Microsoft.Data.Sqlite;

namespace RustPlusHelper.Infrastructure.Storage.Sqlite;

internal static class SqliteMigrationRunner
{
    private const int LatestVersion = 1;

    private const string InitialSchema = """
        CREATE TABLE servers (
            id TEXT NOT NULL PRIMARY KEY,
            display_name TEXT NOT NULL CHECK(length(trim(display_name)) BETWEEN 1 AND 100),
            host TEXT NOT NULL CHECK(length(trim(host)) BETWEEN 1 AND 255),
            port INTEGER NOT NULL CHECK(port BETWEEN 1 AND 65535),
            use_facepunch_proxy INTEGER NOT NULL CHECK(use_facepunch_proxy IN (0, 1)),
            player_id TEXT NULL,
            created_utc_ms INTEGER NOT NULL,
            updated_utc_ms INTEGER NOT NULL,
            last_selected_utc_ms INTEGER NULL
        );

        CREATE TABLE pairings (
            server_id TEXT NOT NULL,
            secret_kind TEXT NOT NULL,
            protected_value BLOB NOT NULL,
            created_utc_ms INTEGER NOT NULL,
            updated_utc_ms INTEGER NOT NULL,
            PRIMARY KEY (server_id, secret_kind),
            FOREIGN KEY (server_id) REFERENCES servers(id) ON DELETE CASCADE
        );

        CREATE INDEX ix_servers_last_selected
            ON servers(last_selected_utc_ms DESC);
        """;

    public static void Apply(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        Execute(connection, "PRAGMA journal_mode = WAL;");
        Execute(connection, "PRAGMA synchronous = NORMAL;");
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version INTEGER NOT NULL PRIMARY KEY,
                name TEXT NOT NULL,
                applied_utc_ms INTEGER NOT NULL
            );
            """);

        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
        var currentVersion = Convert.ToInt32(versionCommand.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        if (currentVersion > LatestVersion)
        {
            throw new InvalidOperationException(
                $"Database schema version {currentVersion} is newer than supported version {LatestVersion}.");
        }

        if (currentVersion == LatestVersion)
        {
            return;
        }

        using var transaction = connection.BeginTransaction();
        Execute(connection, InitialSchema, transaction);

        using var recordCommand = connection.CreateCommand();
        recordCommand.Transaction = transaction;
        recordCommand.CommandText = """
            INSERT INTO schema_migrations(version, name, applied_utc_ms)
            VALUES (1, 'initial server registry and protected pairings', $appliedUtcMs);
            """;
        recordCommand.Parameters.AddWithValue("$appliedUtcMs", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        recordCommand.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void Execute(
        SqliteConnection connection,
        string commandText,
        SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }
}
