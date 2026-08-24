using System.Text;
using RustPlusHelper.Application.Security;
using RustPlusHelper.Infrastructure.Storage.Sqlite;

namespace RustPlusHelper.Infrastructure.Storage.Security;

public sealed class SqliteApplicationSecretStore(
    SqliteDatabase database,
    ISecretProtector protector,
    TimeProvider timeProvider) : IApplicationSecretStore
{
    public void Store(ApplicationSecretKind kind, ReadOnlySpan<byte> secret) =>
        SqliteSecretRowStore.Store(
            database,
            protector,
            timeProvider,
            """
            INSERT INTO application_secrets(secret_kind, protected_value, created_utc_ms, updated_utc_ms)
            VALUES ($secretKind, $protectedValue, $nowUtcMs, $nowUtcMs)
            ON CONFLICT(secret_kind) DO UPDATE SET
                protected_value = excluded.protected_value,
                updated_utc_ms = excluded.updated_utc_ms;
            """,
            command => BindKey(command, kind),
            CreateContext(kind),
            secret);

    public bool Contains(ApplicationSecretKind kind) =>
        SqliteSecretRowStore.Contains(
            database,
            "SELECT EXISTS(SELECT 1 FROM application_secrets WHERE secret_kind = $secretKind);",
            command => BindKey(command, kind));

    public byte[]? Retrieve(ApplicationSecretKind kind) =>
        SqliteSecretRowStore.Retrieve(
            database,
            protector,
            "SELECT protected_value FROM application_secrets WHERE secret_kind = $secretKind;",
            command => BindKey(command, kind),
            CreateContext(kind));

    public bool Delete(ApplicationSecretKind kind) =>
        SqliteSecretRowStore.Delete(
            database,
            "DELETE FROM application_secrets WHERE secret_kind = $secretKind;",
            command => BindKey(command, kind));

    private static void BindKey(Microsoft.Data.Sqlite.SqliteCommand command, ApplicationSecretKind kind) =>
        command.Parameters.AddWithValue("$secretKind", ToStorageName(kind));

    private static byte[] CreateContext(ApplicationSecretKind kind) =>
        Encoding.UTF8.GetBytes($"RustPlusHelper:v1:application:{ToStorageName(kind)}");

    private static string ToStorageName(ApplicationSecretKind kind) => kind switch
    {
        ApplicationSecretKind.RustPlusFcmCredentials => "rustplus-fcm-credentials",
        ApplicationSecretKind.AlarmFcmPersistentIds => "alarm-fcm-persistent-ids",
        ApplicationSecretKind.NotificationPreferences => "notification-preferences",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown application secret kind.")
    };
}
