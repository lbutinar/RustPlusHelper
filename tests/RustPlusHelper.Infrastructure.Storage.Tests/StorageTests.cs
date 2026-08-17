using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using RustPlusHelper.Application.Security;
using RustPlusHelper.Application.Servers;
using RustPlusHelper.Infrastructure.Storage.Security;
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
        Assert.Equal(1L, ExecuteScalar<long>(connection, "SELECT MAX(version) FROM schema_migrations;"));
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
            using var cascadeConnection = temporary.Database.OpenConnection();
            Assert.Equal(0L, ExecuteScalar<long>(cascadeConnection, "SELECT COUNT(*) FROM pairings;"));
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
