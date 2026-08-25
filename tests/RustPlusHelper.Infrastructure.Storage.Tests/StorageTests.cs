using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using RustPlusHelper.Application.Diagnostics;
using RustPlusHelper.Application.Identity;
using RustPlusHelper.Application.Map;
using RustPlusHelper.Application.Pairing;
using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Application.Security;
using RustPlusHelper.Application.Servers;
using RustPlusHelper.Application.Testing;
using RustPlusHelper.Infrastructure.Storage.Diagnostics;
using RustPlusHelper.Infrastructure.Storage.Security;
using RustPlusHelper.Infrastructure.Storage.Identity;
using RustPlusHelper.Infrastructure.Storage.Logging;
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
        Assert.Equal(15L, ExecuteScalar<long>(connection, "SELECT MAX(version) FROM schema_migrations;"));
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
    public void TerrainRasterMigrationsAddAllDerivedStorageColumns()
    {
        using var temporary = new TemporaryDatabase();
        temporary.Database.Initialize();
        using (var connection = temporary.Database.OpenConnection())
        {
            Execute(connection, "ALTER TABLE map_topology DROP COLUMN terrain_slope_rgba;");
            Execute(connection, "ALTER TABLE map_topology DROP COLUMN build_planning_rgba;");
            Execute(connection, "ALTER TABLE map_topology DROP COLUMN elevation_rgba;");
            Execute(connection, "ALTER TABLE map_topology DROP COLUMN water_depth_rgba;");
            Execute(connection, "DROP TABLE saved_cameras;");
            Execute(connection, "DROP TABLE paired_entities;");
            Execute(connection, "ALTER TABLE servers DROP COLUMN rust_plus_server_id;");
            Execute(connection, "DROP TABLE movement_trail_points;");
            Execute(connection, "ALTER TABLE servers DROP COLUMN wipe_cycle;");
            Execute(connection, "DROP TABLE personal_map_pins;");
            Execute(connection, "DELETE FROM schema_migrations WHERE version >= 8;");
        }

        var reopened = new SqliteDatabase(temporary.Database.DatabasePath);
        reopened.Initialize();

        using var migratedConnection = reopened.OpenConnection();
        Assert.Equal(4L, ExecuteScalar<long>(
            migratedConnection,
            "SELECT COUNT(*) FROM pragma_table_info('map_topology') WHERE name IN ('terrain_slope_rgba', 'build_planning_rgba', 'elevation_rgba', 'water_depth_rgba');"));
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
    public void ServerProfileRoundTripsRustPlusServerIdAndAllowsItToRemainNull()
    {
        using var temporary = new TemporaryDatabase();
        var servers = new SqliteServerRepository(temporary.Database);
        var rustPlusServerId = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
        var withId = CreateProfile(playerId: 1, rustPlusServerId: rustPlusServerId);
        servers.Upsert(withId);

        var withoutId = withId with
        {
            Id = Guid.Parse("6ba7b811-9dad-11d1-80b4-00c04fd430c8"),
            RustPlusServerId = null
        };
        servers.Upsert(withoutId);

        var reopenedDatabase = new SqliteDatabase(temporary.Database.DatabasePath);
        var restored = new SqliteServerRepository(reopenedDatabase).GetAll();

        Assert.Equal(rustPlusServerId, restored.Single(profile => profile.Id == withId.Id).RustPlusServerId);
        Assert.Null(restored.Single(profile => profile.Id == withoutId.Id).RustPlusServerId);
    }

    [Fact]
    public void ServerProfileRoundTripsWipeCycle()
    {
        using var temporary = new TemporaryDatabase();
        var servers = new SqliteServerRepository(temporary.Database);
        var weekly = CreateProfile(playerId: 1, wipeCycle: WipeCycle.Weekly);
        servers.Upsert(weekly);

        var reopenedDatabase = new SqliteDatabase(temporary.Database.DatabasePath);
        var restored = Assert.Single(new SqliteServerRepository(reopenedDatabase).GetAll());

        Assert.Equal(WipeCycle.Weekly, restored.WipeCycle);
    }

    [Fact]
    public void ServerProfileTreatsANullWipeCycleColumnAsUnknown()
    {
        using var temporary = new TemporaryDatabase();
        var servers = new SqliteServerRepository(temporary.Database);
        servers.Upsert(CreateProfile(playerId: 1, wipeCycle: WipeCycle.Monthly));

        using (var connection = temporary.Database.OpenConnection())
        {
            // Simulates a row written before this column existed (NULL), rather than the default
            // enum value 0 the app itself would have written for "unset".
            Execute(connection, "UPDATE servers SET wipe_cycle = NULL;");
        }

        var reopenedDatabase = new SqliteDatabase(temporary.Database.DatabasePath);
        var restored = Assert.Single(new SqliteServerRepository(reopenedDatabase).GetAll());

        Assert.Equal(WipeCycle.Unknown, restored.WipeCycle);
    }

    [Fact]
    public void MovementTrailPointsSurviveRestartOrderedOldestFirstWithUnsignedSteamIdAsText()
    {
        using var temporary = new TemporaryDatabase();
        var profile = CreateProfile(playerId: 1);
        new SqliteServerRepository(temporary.Database).Upsert(profile);
        var repository = new SqliteMovementTrailRepository(temporary.Database);
        var steamId = ulong.MaxValue - 42;
        var older = new MovementTrailPoint(1200, 2200, FixedUtc.AddMinutes(-5));
        var newer = new MovementTrailPoint(1300, 2300, FixedUtc);
        repository.Append(profile.Id, steamId, newer);
        repository.Append(profile.Id, steamId, older);

        var reopenedDatabase = new SqliteDatabase(temporary.Database.DatabasePath);
        var restored = new SqliteMovementTrailRepository(reopenedDatabase).GetAll(profile.Id);

        var points = Assert.Single(restored).Value;
        Assert.Equal([older, newer], points);
        using var connection = reopenedDatabase.OpenConnection();
        Assert.Equal("text", ExecuteScalar<string>(connection, "SELECT typeof(steam_id) FROM movement_trail_points LIMIT 1;"));
        Assert.Equal(steamId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ExecuteScalar<string>(connection, "SELECT steam_id FROM movement_trail_points LIMIT 1;"));
    }

    [Fact]
    public void MovementTrailPointsAreDeletedWhenTheirServerIsRemoved()
    {
        using var temporary = new TemporaryDatabase();
        var servers = new SqliteServerRepository(temporary.Database);
        var profile = CreateProfile(playerId: 1);
        servers.Upsert(profile);
        var repository = new SqliteMovementTrailRepository(temporary.Database);
        repository.Append(profile.Id, 1, new MovementTrailPoint(0, 0, FixedUtc));

        servers.Remove(profile.Id);

        Assert.Empty(repository.GetAll(profile.Id));
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
            Execute(connection, "DROP TABLE saved_cameras;");
            Execute(connection, "DROP TABLE paired_entities;");
            Execute(connection, "ALTER TABLE servers DROP COLUMN rust_plus_server_id;");
            Execute(connection, "DROP TABLE movement_trail_points;");
            Execute(connection, "ALTER TABLE servers DROP COLUMN wipe_cycle;");
            Execute(connection, "DROP TABLE personal_map_pins;");
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
                        [new MapWorldPoint(0, 0), new MapWorldPoint(4500, 4500)],
                        OuterPadding: 4)
                ],
                null,
                new MapRasterSnapshot(2, 2, Enumerable.Range(0, 16).Select(value => (byte)value).ToArray()),
                new MapRasterSnapshot(1, 1, [1, 2, 3, 4]),
                [new MapNoBuildZoneSnapshot("zone:1", "assets/test.prefab", "rectangle", [
                    new MapWorldPoint(10, 20), new MapWorldPoint(30, 20), new MapWorldPoint(30, 40)])],
                new MapNoBuildZoneEvidence("24181174", 1, 1, 1, "EXTERNAL RUST BUILD 24181174", "Snapshot warning."),
                new MapRasterSnapshot(1, 1, [53, 194, 111, 135]),
                new MapRasterSnapshot(1, 1, [53, 194, 111, 135]),
                new MapRasterSnapshot(1, 1, [63, 145, 82, 65]),
                new MapRasterSnapshot(1, 1, [48, 158, 204, 140]),
                MapTopologyDerivationVersions.BuildPlanning));

        repository.Upsert(topology);
        var restored = repository.Get(profile.Id);

        Assert.NotNull(restored);
        Assert.Equal(topology.ImportedAtUtc, restored.ImportedAtUtc);
        Assert.Equal(topology.Data.SourceFileName, restored.Data.SourceFileName);
        var restoredPath = Assert.Single(restored.Data.Paths);
        Assert.Equal("Road 0", restoredPath.Name);
        Assert.Equal(MapPathKind.Road, restoredPath.Kind);
        Assert.Equal(12, restoredPath.Width);
        Assert.Equal(4, restoredPath.OuterPadding);
        Assert.Equal(topology.Data.Paths[0].Nodes.ToArray(), restoredPath.Nodes.ToArray());
        Assert.Equal(topology.Data.TopologyRaster?.Rgba, restored.Data.TopologyRaster?.Rgba);
        Assert.Equal(topology.Data.ResourcePotentialRaster?.Rgba, restored.Data.ResourcePotentialRaster?.Rgba);
        var restoredZone = Assert.Single(restored.Data.NoBuildZones!);
        Assert.Equal(topology.Data.NoBuildZones![0].Id, restoredZone.Id);
        Assert.Equal(topology.Data.NoBuildZones[0].PrefabPath, restoredZone.PrefabPath);
        Assert.Equal(topology.Data.NoBuildZones[0].Shape, restoredZone.Shape);
        Assert.Equal(topology.Data.NoBuildZones[0].Boundary.ToArray(), restoredZone.Boundary.ToArray());
        Assert.Equal(topology.Data.NoBuildZoneEvidence, restored.Data.NoBuildZoneEvidence);
        Assert.Equal(topology.Data.TerrainSlopeRaster?.Rgba, restored.Data.TerrainSlopeRaster?.Rgba);
        Assert.Equal(topology.Data.BuildPlanningRaster?.Rgba, restored.Data.BuildPlanningRaster?.Rgba);
        Assert.Equal(topology.Data.ElevationRaster?.Rgba, restored.Data.ElevationRaster?.Rgba);
        Assert.Equal(topology.Data.WaterDepthRaster?.Rgba, restored.Data.WaterDepthRaster?.Rgba);
        Assert.Equal(MapTopologyDerivationVersions.BuildPlanning, restored.Data.BuildPlanningVersion);

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

        repository.Append(oldest, 2, DateTimeOffset.MinValue);
        repository.Append(middle, 2, DateTimeOffset.MinValue);
        repository.Append(newest, 2, DateTimeOffset.MinValue);

        Assert.Equal([newest, middle], repository.GetRecent(profile.Id, 10));
        Assert.True(servers.Remove(profile.Id));
        Assert.Empty(repository.GetRecent(profile.Id, 10));
    }

    [Fact]
    public void CompanionEventAppendPurgesRowsOlderThanMinRetainedRegardlessOfRowCountCap()
    {
        using var temporary = new TemporaryDatabase();
        var servers = new SqliteServerRepository(temporary.Database);
        var profile = CreateProfile();
        servers.Upsert(profile);
        var repository = new SqliteCompanionEventRepository(temporary.Database);
        var old = new CompanionEvent(
            Guid.Parse("40000000-0000-0000-0000-000000000000"),
            profile.Id,
            FixedUtc.AddDays(-31),
            CompanionEventKind.ConnectionEstablished,
            CompanionEventSource.Transport,
            "Old connection event");
        repository.Append(old, 200, DateTimeOffset.MinValue);

        var recent = new CompanionEvent(
            Guid.Parse("50000000-0000-0000-0000-000000000000"),
            profile.Id,
            FixedUtc,
            CompanionEventKind.ConnectionLost,
            CompanionEventSource.Transport,
            "Recent connection event");
        repository.Append(recent, 200, FixedUtc.AddDays(-30));

        var remaining = repository.GetRecent(profile.Id, 10);
        Assert.Single(remaining);
        Assert.Equal(recent.Id, remaining[0].Id);
    }

    [Fact]
    public void CompanionEventPurgeOlderThanSweepsEveryServerNotJustTheOneAppendedTo()
    {
        using var temporary = new TemporaryDatabase();
        var servers = new SqliteServerRepository(temporary.Database);
        var activeProfile = CreateProfile();
        servers.Upsert(activeProfile);
        var staleProfile = activeProfile with { Id = Guid.Parse("6ba7b812-9dad-11d1-80b4-00c04fd430c8") };
        servers.Upsert(staleProfile);
        var repository = new SqliteCompanionEventRepository(temporary.Database);

        var staleEvent = new CompanionEvent(
            Guid.Parse("60000000-0000-0000-0000-000000000000"),
            staleProfile.Id,
            FixedUtc.AddDays(-45),
            CompanionEventKind.ConnectionEstablished,
            CompanionEventSource.Transport,
            "Stale server's old event");
        // Appended with an effectively-disabled age cutoff, mirroring a server whose live session
        // hasn't run recently enough to have its own Append call trim it by age.
        repository.Append(staleEvent, 200, DateTimeOffset.MinValue);

        repository.PurgeOlderThan(FixedUtc.AddDays(-30));

        Assert.Empty(repository.GetRecent(staleProfile.Id, 10));
    }

    [Fact]
    public void SavedCamerasRoundTripOrderedByNicknameAndCascadeWithServer()
    {
        using var temporary = new TemporaryDatabase();
        var servers = new SqliteServerRepository(temporary.Database);
        var profile = CreateProfile();
        servers.Upsert(profile);
        var repository = new SqliteSavedCameraRepository(temporary.Database);
        var frontGate = new SavedCamera(Guid.NewGuid(), profile.Id, "CAM01", "Front gate", FixedUtc.AddMinutes(-1));
        var backDoor = new SavedCamera(Guid.NewGuid(), profile.Id, "CAM02", "Back door", FixedUtc);

        repository.Add(frontGate);
        repository.Add(backDoor);

        Assert.Equal([backDoor, frontGate], repository.GetAll(profile.Id));
        Assert.True(repository.Remove(profile.Id, backDoor.Id));
        Assert.Equal([frontGate], repository.GetAll(profile.Id));
        Assert.True(servers.Remove(profile.Id));
        Assert.Empty(repository.GetAll(profile.Id));
    }

    [Fact]
    public void PairedEntitiesRoundTripPreserveUlongIdAndCascadeWithServer()
    {
        using var temporary = new TemporaryDatabase();
        var servers = new SqliteServerRepository(temporary.Database);
        var profile = CreateProfile();
        servers.Upsert(profile);
        var repository = new SqlitePairedEntityRepository(temporary.Database);
        var frontDoorSwitch = new PairedEntity(
            Guid.NewGuid(), profile.Id, ulong.MaxValue, PairedEntityKind.Switch, "Front door", FixedUtc.AddMinutes(-1));
        var baseAlarm = new PairedEntity(
            Guid.NewGuid(), profile.Id, 123456789UL, PairedEntityKind.Alarm, "Base alarm", FixedUtc);

        repository.Add(frontDoorSwitch);
        repository.Add(baseAlarm);

        Assert.Equal([baseAlarm, frontDoorSwitch], repository.GetAll(profile.Id));
        Assert.True(repository.Remove(profile.Id, baseAlarm.Id));
        Assert.Equal([frontDoorSwitch], repository.GetAll(profile.Id));
        Assert.True(servers.Remove(profile.Id));
        Assert.Empty(repository.GetAll(profile.Id));
    }

    [Fact]
    public void PersonalMapPinsRoundTripOrderedByCreationAndCascadeWithServer()
    {
        using var temporary = new TemporaryDatabase();
        var servers = new SqliteServerRepository(temporary.Database);
        var profile = CreateProfile();
        servers.Upsert(profile);
        var repository = new SqlitePersonalMapPinRepository(temporary.Database);
        var first = new PersonalMapPin(Guid.NewGuid(), profile.Id, 1200.5f, 2400.25f, "Loot stash", FixedUtc.AddMinutes(-1));
        var second = new PersonalMapPin(Guid.NewGuid(), profile.Id, 800f, 900f, "Ambush spot", FixedUtc);

        repository.Add(first);
        repository.Add(second);

        Assert.Equal([first, second], repository.GetAll(profile.Id));
        Assert.True(repository.Remove(profile.Id, first.Id));
        Assert.Equal([second], repository.GetAll(profile.Id));
        Assert.True(servers.Remove(profile.Id));
        Assert.Empty(repository.GetAll(profile.Id));
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

    [Fact]
    public void DatabaseHealthCheckIsHealthyForAFreshlyInitializedDatabase()
    {
        using var temporary = new TemporaryDatabase();
        temporary.Database.Initialize();
        var check = new DatabaseHealthCheck(temporary.Database);

        var result = check.Check();

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public void DatabaseHealthCheckIsUnhealthyWhenSchemaVersionIsBehindLatest()
    {
        using var temporary = new TemporaryDatabase();
        temporary.Database.Initialize();
        using (var connection = temporary.Database.OpenConnection())
        {
            Execute(connection, "DELETE FROM schema_migrations WHERE version = (SELECT MAX(version) FROM schema_migrations);");
        }

        var result = new DatabaseHealthCheck(temporary.Database).Check();

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("does not match", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsExportProducesAZipWithSummaryServersAndRedactedLogsButNoHostOrPlayerId()
    {
        using var temporary = new TemporaryDatabase();
        temporary.Database.Initialize();
        new SqliteServerRepository(temporary.Database).Upsert(CreateProfile(playerId: 76561198000000123));

        using var logsDirectory = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(logsDirectory.Path, "app-20260817.log"),
            "2026-08-17T00:00:00Z [Information] Test: player_token=super-secret-value should be redacted");

        var exportService = new DiagnosticsExportService(
            [new InMemoryHealthCheck("Fake check", HealthStatus.Healthy, "All good.")],
            new SqliteServerRepository(temporary.Database),
            new FixedTimeProvider(FixedUtc),
            "1.2.3.4",
            logsDirectory.Path);

        using var zipStream = new MemoryStream();
        exportService.ExportTo(zipStream);

        zipStream.Position = 0;
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var summary = ReadEntry(archive, "summary.txt");
        var servers = ReadEntry(archive, "servers.txt");
        var log = ReadEntry(archive, "logs/app-20260817.log");

        Assert.Contains("1.2.3.4", summary, StringComparison.Ordinal);
        Assert.Contains("Fake check", summary, StringComparison.Ordinal);
        Assert.Contains("EU Main", servers, StringComparison.Ordinal);
        Assert.DoesNotContain("companion.example.invalid", servers, StringComparison.Ordinal);
        Assert.DoesNotContain("76561198000000123", servers, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-value", log, StringComparison.Ordinal);
    }

    private static string ReadEntry(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName) ?? throw new InvalidOperationException($"Missing entry {entryName}.");
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static ServerProfile CreateProfile(
        ulong? playerId = null,
        Guid? rustPlusServerId = null,
        WipeCycle wipeCycle = WipeCycle.Unknown) => new(
        Guid.Parse("349b4e9a-215f-4388-ad24-4df8fa572f1c"),
        "EU Main",
        "companion.example.invalid",
        28082,
        true,
        playerId,
        FixedUtc,
        FixedUtc,
        FixedUtc,
        rustPlusServerId,
        wipeCycle);

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

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "RustPlusHelper.Storage.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
