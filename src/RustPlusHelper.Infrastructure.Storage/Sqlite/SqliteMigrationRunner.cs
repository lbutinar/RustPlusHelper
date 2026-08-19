using Microsoft.Data.Sqlite;

namespace RustPlusHelper.Infrastructure.Storage.Sqlite;

internal static class SqliteMigrationRunner
{
    private const int LatestVersion = 10;

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

    private const string PlayerIdentitySchema = """
        CREATE TABLE player_identity (
            singleton_id INTEGER NOT NULL PRIMARY KEY CHECK(singleton_id = 1),
            steam_id TEXT NOT NULL CHECK(length(steam_id) BETWEEN 1 AND 20),
            updated_utc_ms INTEGER NOT NULL
        );

        INSERT INTO player_identity(singleton_id, steam_id, updated_utc_ms)
        SELECT 1, player_id, updated_utc_ms
        FROM servers
        WHERE player_id IS NOT NULL
        ORDER BY COALESCE(last_selected_utc_ms, updated_utc_ms) DESC, id
        LIMIT 1;
        """;

    private const string MapCacheSchema = """
        CREATE TABLE map_cache (
            server_id TEXT NOT NULL PRIMARY KEY,
            retrieved_utc_ms INTEGER NOT NULL,
            metadata_json TEXT NOT NULL CHECK(length(metadata_json) > 0),
            jpeg_image BLOB NOT NULL CHECK(length(jpeg_image) > 0),
            FOREIGN KEY (server_id) REFERENCES servers(id) ON DELETE CASCADE
        );
        """;

    private const string MapTopologySchema = """
        CREATE TABLE map_topology (
            server_id TEXT NOT NULL PRIMARY KEY,
            imported_utc_ms INTEGER NOT NULL,
            metadata_json TEXT NOT NULL CHECK(length(metadata_json) > 0),
            biome_rgba BLOB NULL,
            topology_rgba BLOB NULL,
            resource_potential_rgba BLOB NULL,
            FOREIGN KEY (server_id) REFERENCES servers(id) ON DELETE CASCADE
        );
        """;

    private const string CompanionEventSchema = """
        CREATE TABLE companion_events (
            id TEXT NOT NULL PRIMARY KEY,
            server_id TEXT NOT NULL,
            occurred_utc_ms INTEGER NOT NULL,
            kind TEXT NOT NULL CHECK(length(kind) > 0),
            source TEXT NOT NULL CHECK(length(source) > 0),
            title TEXT NOT NULL CHECK(length(title) > 0),
            detail TEXT NULL,
            FOREIGN KEY (server_id) REFERENCES servers(id) ON DELETE CASCADE
        );

        CREATE INDEX ix_companion_events_server_time
            ON companion_events(server_id, occurred_utc_ms DESC, id DESC);
        """;

    private const string ApplicationSecretSchema = """
        CREATE TABLE application_secrets (
            secret_kind TEXT NOT NULL PRIMARY KEY,
            protected_value BLOB NOT NULL,
            created_utc_ms INTEGER NOT NULL,
            updated_utc_ms INTEGER NOT NULL
        );
        """;

    private const string CompanionEventPositionSchema = """
        ALTER TABLE companion_events ADD COLUMN world_x REAL NULL;
        ALTER TABLE companion_events ADD COLUMN world_y REAL NULL;
        """;

    private const string TerrainSlopeSchema = """
        ALTER TABLE map_topology ADD COLUMN terrain_slope_rgba BLOB NULL;
        """;

    private const string TerrainPlanningSchema = """
        ALTER TABLE map_topology ADD COLUMN build_planning_rgba BLOB NULL;
        ALTER TABLE map_topology ADD COLUMN elevation_rgba BLOB NULL;
        ALTER TABLE map_topology ADD COLUMN water_depth_rgba BLOB NULL;
        """;

    private const string SavedCameraSchema = """
        CREATE TABLE saved_cameras (
            id TEXT NOT NULL PRIMARY KEY,
            server_id TEXT NOT NULL,
            camera_code TEXT NOT NULL CHECK(length(trim(camera_code)) BETWEEN 1 AND 64),
            nickname TEXT NOT NULL CHECK(length(trim(nickname)) BETWEEN 1 AND 100),
            created_utc_ms INTEGER NOT NULL,
            FOREIGN KEY (server_id) REFERENCES servers(id) ON DELETE CASCADE
        );

        CREATE UNIQUE INDEX ux_saved_cameras_server_code
            ON saved_cameras(server_id, camera_code);
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

        for (var version = currentVersion + 1; version <= LatestVersion; version++)
        {
            ApplyMigration(connection, version);
        }
    }

    private static void ApplyMigration(SqliteConnection connection, int version)
    {
        using var transaction = connection.BeginTransaction();
        var (name, sql) = version switch
        {
            1 => ("initial server registry and protected pairings", InitialSchema),
            2 => ("application player identity", PlayerIdentitySchema),
            3 => ("latest Rust+ map cache", MapCacheSchema),
            4 => ("derived external map topology", MapTopologySchema),
            5 => ("bounded companion event history", CompanionEventSchema),
            6 => ("protected application credentials", ApplicationSecretSchema),
            7 => ("companion event map positions", CompanionEventPositionSchema),
            8 => ("derived terrain slope raster", TerrainSlopeSchema),
            9 => ("derived terrain planning rasters", TerrainPlanningSchema),
            10 => ("saved camera codes", SavedCameraSchema),
            _ => throw new InvalidOperationException($"No migration is defined for schema version {version}.")
        };
        Execute(connection, sql, transaction);

        using var recordCommand = connection.CreateCommand();
        recordCommand.Transaction = transaction;
        recordCommand.CommandText = """
            INSERT INTO schema_migrations(version, name, applied_utc_ms)
            VALUES ($version, $name, $appliedUtcMs);
            """;
        recordCommand.Parameters.AddWithValue("$version", version);
        recordCommand.Parameters.AddWithValue("$name", name);
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
