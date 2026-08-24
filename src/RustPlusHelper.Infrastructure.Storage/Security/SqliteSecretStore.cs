using System.Text;
using RustPlusHelper.Application.Security;
using RustPlusHelper.Infrastructure.Storage.Sqlite;

namespace RustPlusHelper.Infrastructure.Storage.Security;

public sealed class SqliteSecretStore(
    SqliteDatabase database,
    ISecretProtector protector,
    TimeProvider timeProvider) : ISecretStore
{
    public void Store(Guid serverId, SecretKind kind, ReadOnlySpan<byte> secret) =>
        SqliteSecretRowStore.Store(
            database,
            protector,
            timeProvider,
            """
            INSERT INTO pairings(
                server_id, secret_kind, protected_value, created_utc_ms, updated_utc_ms)
            VALUES ($serverId, $secretKind, $protectedValue, $nowUtcMs, $nowUtcMs)
            ON CONFLICT(server_id, secret_kind) DO UPDATE SET
                protected_value = excluded.protected_value,
                updated_utc_ms = excluded.updated_utc_ms;
            """,
            command => BindKey(command, serverId, kind),
            CreateContext(serverId, kind),
            secret);

    public bool Contains(Guid serverId, SecretKind kind) =>
        SqliteSecretRowStore.Contains(
            database,
            """
            SELECT EXISTS(
                SELECT 1
                FROM pairings
                WHERE server_id = $serverId AND secret_kind = $secretKind);
            """,
            command => BindKey(command, serverId, kind));

    public byte[]? Retrieve(Guid serverId, SecretKind kind) =>
        SqliteSecretRowStore.Retrieve(
            database,
            protector,
            """
            SELECT protected_value
            FROM pairings
            WHERE server_id = $serverId AND secret_kind = $secretKind;
            """,
            command => BindKey(command, serverId, kind),
            CreateContext(serverId, kind));

    public bool Delete(Guid serverId, SecretKind kind) =>
        SqliteSecretRowStore.Delete(
            database,
            """
            DELETE FROM pairings
            WHERE server_id = $serverId AND secret_kind = $secretKind;
            """,
            command => BindKey(command, serverId, kind));

    private static void BindKey(Microsoft.Data.Sqlite.SqliteCommand command, Guid serverId, SecretKind kind)
    {
        command.Parameters.AddWithValue("$serverId", serverId.ToString("D"));
        command.Parameters.AddWithValue("$secretKind", ToStorageName(kind));
    }

    private static byte[] CreateContext(Guid serverId, SecretKind kind) =>
        Encoding.UTF8.GetBytes($"RustPlusHelper:v1:{serverId:D}:{ToStorageName(kind)}");

    private static string ToStorageName(SecretKind kind) => kind switch
    {
        SecretKind.RustPlusPlayerToken => "rustplus-player-token",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown secret kind.")
    };
}
