using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using RustPlusHelper.Application.Identity;
using RustPlusHelper.Application.Map;
using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Application.Security;
using RustPlusHelper.Application.Servers;
using RustPlusHelper.Infrastructure.Storage.Security;
using RustPlusHelper.Infrastructure.Storage.Identity;
using RustPlusHelper.Infrastructure.Storage.Map;
using RustPlusHelper.Infrastructure.Storage.RustPlus;
using RustPlusHelper.Infrastructure.Storage.Servers;
using RustPlusHelper.Infrastructure.Storage.Sqlite;

namespace RustPlusHelper.Infrastructure.Storage.Tests;

public sealed class StorageTests
{
    private static readonly DateTimeOffset FixedUtc = new(2026, 8, 17, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public void InitializationIsIdempotentAndEnablesWalAndForeignKeys()
    {
        using var temporary = new TemporaryDatabase();
        temporary.Database.Initialize();
        temporary.Database.Initialize();

        using var connection = temporary.Database.OpenConnection();
        Assert.Equal("wal", ExecuteScalar<string>(connection, "PRAGMA journal_mode;"));
        Assert.Equal(1L, ExecuteScalar<long>(connection, "PRAGMA foreign_keys;"));
        Assert.Equal(7L, ExecuteScalar<long>(connection, "SELECT MAX(version) FROM schema_migrations;"));
        var sqliteVersion = Version.Parse(ExecuteScalar<string>(connection, "SELECT sqlite_version();"));
        Assert.True(sqliteVersion >= new Version(3, 50, 2), $"Bundled SQLite {sqliteVersion} is vulnerable.");
    }

    [Fact]
    public void RejectsDatabaseCreatedByANewerApplicationSchema()
    {
        using var temporary = new TemporaryDatabase();
        temporary.Database.Initialize();
        using (var connection = temporary.Database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO schema_migrations(version, name, applied_utc_ms)
                VALUES (99, 'future test schema', 0);
                """;
            command.ExecuteNonQuery();
        }

        var reopened = new SqliteDatabase(temporary.Database.DatabasePath);
        var exception = Assert.Throws<InvalidOperationException>(reopened.Initialize);
        Assert.Contains("newer than supported", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerProfileSurvivesRepositoryRestartAndKeepsUnsignedPlayerIdAsText()
    {
        using var temporary = new TemporaryDatabase();
        var profile = CreateProfile(playerId: ulong.MaxValue);
        new SqliteServerRepository(temporary.Database).Upsert(profile);

        var reopenedDatabase = new SqliteDatabase(temporary.Database.DatabasePath);
        var restored = Assert.Single(new SqliteServerRepository(reopenedDatabase).GetAll());

        Assert.Equal(profile, restored);
        using var connection = reopenedDatabase.OpenConnection();
        Assert.Equal("text", ExecuteScalar<string>(connection, "SELECT typeof(player_id) FROM servers;"));
        Assert.Equal(ulong.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ExecuteScalar<string>(connection, "SELECT player_id FROM servers;"));
    }

    [Fact]
    public void PlayerIdentitySurvivesRestartAndUsesCanonicalUnsignedText()
    {
        using var temporary = new TemporaryDatabase();
        var identity = new PlayerIdentity(ulong.MaxValue, FixedUtc);
        new SqlitePlayerIdentityRepository(temporary.Database).Upsert(identity);

        var reopenedDatabase = new SqliteDatabase(temporary.Database.DatabasePath);
        var restored = new SqlitePlayerIdentityRepository(reopenedDatabase).Get();

        Assert.Equal(identity, restored);
        using var connection = reopenedDatabase.OpenConnection();
        Assert.Equal("text", ExecuteScalar<string>(connection, "SELECT typeof(steam_id) FROM player_identity;"));
    }

    [Fact]
    public void VersionTwoMigrationImportsMostRecentLegacyServerPlayerId()
    {
        using var temporary = new TemporaryDatabase();
        temporary.Database.Initialize();
        var profile = CreateProfile(playerId: ulong.MaxValue);
        new SqliteServerRepository(temporary.Database).Upsert(profile);

        using (var connection = temporary.Database.OpenConnection())
        {
            Execute(connection, "DROP TABLE player_identity;");
            Execute(connection, "DROP TABLE map_cache;");
            Execute(connection, "DROP TABLE map_topology;");
            Execute(connection, "DROP TABLE companion_events;");
            Execute(connection, "DROP TABLE application_secrets;");
            Execute(connection, "DELETE FROM schema_migrations WHERE version >= 2;");
        }

        var reopenedDatabase = new SqliteDatabase(temporary.Database.DatabasePath);
        reopenedDatabase.Initialize();

        Assert.Equal(ulong.MaxValue, new SqlitePlayerIdentityRepository(reopenedDatabase).Get()?.SteamId);
    }

    [Fact]
    public void MapCacheRoundTripsJpegAndMetadataAndCascadesWithServer()
    {
        using var temporary = new TemporaryDatabase();
        var servers = new SqliteServerRepository(temporary.Database);
        var profile = CreateProfile();
        servers.Upsert(profile);
        var repository = new SqliteMapCacheRepository(temporary.Database);
        var map = new CachedServerMap(
            profile.Id,
            FixedUtc,
            new ServerInfoSnapshot(
                "Cached server", null, null, "Procedural Map", 4500, FixedUtc.AddDays(-2),
                42, 200, 3, 123, 456, null, null, null, null),
            new ServerMapSnapshot(
                1000, 1000, 50, "#FF112233",
                [new MapMonumentSnapshot("launch_site_1", 100, 200)],
                [0xFF, 0xD8, 0xFF, 0xD9]));

        repository.Upsert(map);
        var restored = repository.Get(profile.Id);

        Assert.NotNull(restored);
        Assert.Equal(map.RetrievedAtUtc, restored.RetrievedAtUtc);
        Assert.Equal(map.Server, restored.Server);
        Assert.Equal(map.Map.Width, restored.Map.Width);
        Assert.Equal(map.Map.Monuments, restored.Map.Monuments);
        Assert.Equal(map.Map.JpegImage, restored.Map.JpegImage);

        Assert.True(servers.Remove(profile.Id));
        Assert.Null(repository.Get(profile.Id));
    }

    [Fact]
    public void MapTopologyRoundTripsDerivedRastersAndPathsAndCascadesWithServer()
    {
        using var temporary = new TemporaryDatabase();
        var servers = new SqliteServerRepository(temporary.Database);
        var profile = CreateProfile();
        servers.Upsert(profile);
        var repository = new SqliteMapTopologyRepository(temporary.Database);
        var topology = new SavedMapTopology(
            profile.Id,
            FixedUtc,
            new ImportedMapTopology(
                "procedural.4500.123.map",
                new string('A', 64),
                10,
                123456789,
                4500,
                [new MapSourceLayerSnapshot("topology", 16)],
                42,
                [
                    new MapPathSnapshot(
                        "Road 0",
                        MapPathKind.Road,
                        12,
                        [new MapWorldPoint(0, 0), new MapWorldPoint(4500, 4500)])
                ],
                null,
                new MapRasterSnapshot(2, 2, Enumerable.Range(0, 16).Select(value => (byte)value).ToArray()),
                new MapRasterSnapshot(1, 1, [1, 2, 3, 4]),
                [new MapNoBuildZoneSnapshot("zone:1", "assets/test.prefab", "rectangle", [
                    new MapWorldPoint(10, 20), new MapWorldPoint(30, 20), new MapWorldPoint(30, 40)])],
                new MapNoBuildZoneEvidence("24181174", 1, 1, 1, "EXTERNAL RUST BUILD 24181174", "Snapshot warning.")));

        repository.Upsert(topology);
        var restored = repository.Get(profile.Id);

        Assert.NotNull(restored);
        Assert.Equal(topology.ImportedAtUtc, restored.ImportedAtUtc);
        Assert.Equal(topology.Data.SourceFileName, restored.Data.SourceFileName);
        var restoredPath = Assert.Single(restored.Data.Paths);
        Assert.Equal("Road 0", restoredPath.Name);
        Assert.Equal(MapPathKind.Road, restoredPath.Kind);
        Assert.Equal(12, restoredPath.Width);
        Assert.Equal(topology.Data.Paths[0].Nodes.ToArray(), restoredPath.Nodes.ToArray());
        Assert.Equal(topology.Data.TopologyRaster?.Rgba, restored.Data.TopologyRaster?.Rgba);
        Assert.Equal(topology.Data.ResourcePotentialRaster?.Rgba, restored.Data.ResourcePotentialRaster?.Rgba);
        var restoredZone = Assert.Single(restored.Data.NoBuildZones!);
        Assert.Equal(topology.Data.NoBuildZones![0].Id, restoredZone.Id);
        Assert.Equal(topology.Data.NoBuildZones[0].PrefabPath, restoredZone.PrefabPath);
        Assert.Equal(topology.Data.NoBuildZones[0].Shape, restoredZone.Shape);
        Assert.Equal(topology.Data.NoBuildZones[0].Boundary.ToArray(), restoredZone.Boundary.ToArray());
        Assert.Equal(topology.Data.NoBuildZoneEvidence, restored.Data.NoBuildZoneEvidence);

        Assert.True(servers.Remove(profile.Id));
        Assert.Null(repository.Get(profile.Id));
    }

    [Fact]
    public void CompanionEventsRoundTripInOrderRespectRetentionAndCascadeWithServer()
    {
        using var temporary = new TemporaryDatabase();
        var servers = new SqliteServerRepository(temporary.Database);
        var profile = CreateProfile();
        servers.Upsert(profile);
        var repository = new SqliteCompanionEventRepository(temporary.Database);
        var oldest = new CompanionEvent(
            Guid.Parse("10000000-0000-0000-0000-000000000000"),
            profile.Id,
            FixedUtc.AddMinutes(-2),
            CompanionEventKind.ConnectionEstablished,
            CompanionEventSource.Transport,
            "Monitoring connected");
        var middle = new CompanionEvent(
            Guid.Parse("20000000-0000-0000-0000-000000000000"),
            profile.Id,
            FixedUtc.AddMinutes(-1),
            CompanionEventKind.MarkerAppeared,
            CompanionEventSource.SnapshotDiff,
            "Cargo ship appeared",
            "Derived detail",
            new MapPositionSnapshot(123.5f, 456.25f));
        var newest = new CompanionEvent(
            Guid.Parse("30000000-0000-0000-0000-000000000000"),
            profile.Id,
            FixedUtc,
            CompanionEventKind.ConnectionLost,
            CompanionEventSource.Transport,
            "Connection lost");

        repository.Append(oldest, 2);
        repository.Append(middle, 2);
        repository.Append(newest, 2);

        Assert.Equal([newest, middle], repository.GetRecent(profile.Id, 10));
        Assert.True(servers.Remove(profile.Id));
        Assert.Empty(repository.GetRecent(profile.Id, 10));
    }

    [Fact]
    public void DpapiSecretStoreRoundTripsWithoutPersistingPlaintextAndCascadesOnServerRemoval()
    {
        using var temporary = new TemporaryDatabase();
        var repository = new SqliteServerRepository(temporary.Database);
        var profile = CreateProfile();
        repository.Upsert(profile);

        var secretStore = new SqliteSecretStore(
            temporary.Database,
            new WindowsDpapiSecretProtector(),
            new FixedTimeProvider(FixedUtc));
        var cleartext = Encoding.UTF8.GetBytes("phase-two-known-secret-value");
        try
        {
            secretStore.Store(profile.Id, SecretKind.RustPlusPlayerToken, cleartext);
            Assert.True(secretStore.Contains(profile.Id, SecretKind.RustPlusPlayerToken));
            var restored = secretStore.Retrieve(profile.Id, SecretKind.RustPlusPlayerToken);
            try
            {
                Assert.NotNull(restored);
                Assert.Equal(cleartext, restored);

                using var connection = temporary.Database.OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT protected_value FROM pairings;";
                var stored = Assert.IsType<byte[]>(command.ExecuteScalar());
                Assert.False(cleartext.SequenceEqual(stored));
                Assert.True(stored.Length > cleartext.Length);
            }
            finally
            {
                if (restored is not null)
                {
                    CryptographicOperations.ZeroMemory(restored);
                }
            }

            Assert.True(repository.Remove(profile.Id));
            Assert.False(secretStore.Contains(profile.Id, SecretKind.RustPlusPlayerToken));
            using var cascadeConnection = temporary.Database.OpenConnection();
            Assert.Equal(0L, ExecuteScalar<long>(cascadeConnection, "SELECT COUNT(*) FROM pairings;"));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cleartext);
        }
    }

    [Fact]
    public void ApplicationCredentialsRoundTripWithoutPersistingPlaintext()
    {
        using var temporary = new TemporaryDatabase();
        var store = new SqliteApplicationSecretStore(
            temporary.Database,
            new WindowsDpapiSecretProtector(),
            new FixedTimeProvider(FixedUtc));
        var cleartext = Encoding.UTF8.GetBytes("sanitized-fcm-credential-fixture");
        try
        {
            store.Store(ApplicationSecretKind.RustPlusFcmCredentials, cleartext);
            var restored = store.Retrieve(ApplicationSecretKind.RustPlusFcmCredentials);
            try
            {
                Assert.Equal(cleartext, restored);
                using var connection = temporary.Database.OpenConnection();
                var stored = Assert.IsType<byte[]>(ExecuteScalar<object>(
                    connection,
                    "SELECT protected_value FROM application_secrets;"));
                Assert.False(cleartext.SequenceEqual(stored));
            }
            finally
            {
                if (restored is not null)
                {
                    CryptographicOperations.ZeroMemory(restored);
                }
            }

            Assert.True(store.Delete(ApplicationSecretKind.RustPlusFcmCredentials));
            Assert.False(store.Contains(ApplicationSecretKind.RustPlusFcmCredentials));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cleartext);
        }
    }

    [Fact]
    public void DpapiContextBindsCiphertextToItsServerAndPurpose()
    {
        var protector = new WindowsDpapiSecretProtector();
        var cleartext = Encoding.UTF8.GetBytes("context-bound-secret");
        var correctContext = Encoding.UTF8.GetBytes("server-one");
        var wrongContext = Encoding.UTF8.GetBytes("server-two");
        var ciphertext = protector.Protect(cleartext, correctContext);
        try
        {
            var restored = protector.Unprotect(ciphertext, correctContext);
            try
            {
                Assert.Equal(cleartext, restored);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(restored);
            }

            Assert.Throws<CryptographicException>(() => protector.Unprotect(ciphertext, wrongContext));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cleartext);
            CryptographicOperations.ZeroMemory(correctContext);
            CryptographicOperations.ZeroMemory(wrongContext);
            CryptographicOperations.ZeroMemory(ciphertext);
        }
    }

    private static ServerProfile CreateProfile(ulong? playerId = null) => new(
        Guid.Parse("349b4e9a-215f-4388-ad24-4df8fa572f1c"),
        "EU Main",
        "companion.example.invalid",
        28082,
        true,
        playerId,
        FixedUtc,
        FixedUtc,
        FixedUtc);

    private static T ExecuteScalar<T>(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)command.ExecuteScalar()!;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            "RustPlusHelper.Storage.Tests",
            Guid.NewGuid().ToString("N"));

        public TemporaryDatabase()
        {
            Database = new SqliteDatabase(Path.Combine(_directory, "test.db"));
        }

        public SqliteDatabase Database { get; }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
