using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using RustPlusHelper.Application.Security;
using RustPlusHelper.Infrastructure.Storage.Sqlite;

namespace RustPlusHelper.Infrastructure.Storage.Security;

public sealed class SqliteSecretStore(
    SqliteDatabase database,
    ISecretProtector protector,
    TimeProvider timeProvider) : ISecretStore
{
    public void Store(Guid serverId, SecretKind kind, ReadOnlySpan<byte> secret)
    {
        database.Initialize();
        var context = CreateContext(serverId, kind);
        var protectedValue = protector.Protect(secret, context);
        try
        {
            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO pairings(
                    server_id, secret_kind, protected_value, created_utc_ms, updated_utc_ms)
                VALUES ($serverId, $secretKind, $protectedValue, $nowUtcMs, $nowUtcMs)
                ON CONFLICT(server_id, secret_kind) DO UPDATE SET
                    protected_value = excluded.protected_value,
                    updated_utc_ms = excluded.updated_utc_ms;
                """;
            command.Parameters.AddWithValue("$serverId", serverId.ToString("D"));
            command.Parameters.AddWithValue("$secretKind", ToStorageName(kind));
            command.Parameters.Add("$protectedValue", SqliteType.Blob).Value = protectedValue;
            command.Parameters.AddWithValue("$nowUtcMs", timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
            command.ExecuteNonQuery();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(context);
            CryptographicOperations.ZeroMemory(protectedValue);
        }
    }

    public bool Contains(Guid serverId, SecretKind kind)
    {
        database.Initialize();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1
                FROM pairings
                WHERE server_id = $serverId AND secret_kind = $secretKind);
            """;
        command.Parameters.AddWithValue("$serverId", serverId.ToString("D"));
        command.Parameters.AddWithValue("$secretKind", ToStorageName(kind));
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) == 1;
    }

    public byte[]? Retrieve(Guid serverId, SecretKind kind)
    {
        database.Initialize();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT protected_value
            FROM pairings
            WHERE server_id = $serverId AND secret_kind = $secretKind;
            """;
        command.Parameters.AddWithValue("$serverId", serverId.ToString("D"));
        command.Parameters.AddWithValue("$secretKind", ToStorageName(kind));

        var stored = command.ExecuteScalar() as byte[];
        if (stored is null)
        {
            return null;
        }

        var context = CreateContext(serverId, kind);
        try
        {
            return protector.Unprotect(stored, context);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(context);
            CryptographicOperations.ZeroMemory(stored);
        }
    }

    public bool Delete(Guid serverId, SecretKind kind)
    {
        database.Initialize();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM pairings
            WHERE server_id = $serverId AND secret_kind = $secretKind;
            """;
        command.Parameters.AddWithValue("$serverId", serverId.ToString("D"));
        command.Parameters.AddWithValue("$secretKind", ToStorageName(kind));
        return command.ExecuteNonQuery() == 1;
    }

    private static byte[] CreateContext(Guid serverId, SecretKind kind) =>
        Encoding.UTF8.GetBytes($"RustPlusHelper:v1:{serverId:D}:{ToStorageName(kind)}");

    private static string ToStorageName(SecretKind kind) => kind switch
    {
        SecretKind.RustPlusPlayerToken => "rustplus-player-token",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown secret kind.")
    };
}
