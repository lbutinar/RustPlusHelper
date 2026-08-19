using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using RustPlusHelper.Application.Security;
using RustPlusHelper.Infrastructure.Storage.Sqlite;

namespace RustPlusHelper.Infrastructure.Storage.Security;

public sealed class SqliteApplicationSecretStore(
    SqliteDatabase database,
    ISecretProtector protector,
    TimeProvider timeProvider) : IApplicationSecretStore
{
    public void Store(ApplicationSecretKind kind, ReadOnlySpan<byte> secret)
    {
        database.Initialize();
        var context = CreateContext(kind);
        var protectedValue = protector.Protect(secret, context);
        try
        {
            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO application_secrets(secret_kind, protected_value, created_utc_ms, updated_utc_ms)
                VALUES ($secretKind, $protectedValue, $nowUtcMs, $nowUtcMs)
                ON CONFLICT(secret_kind) DO UPDATE SET
                    protected_value = excluded.protected_value,
                    updated_utc_ms = excluded.updated_utc_ms;
                """;
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

    public bool Contains(ApplicationSecretKind kind)
    {
        database.Initialize();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM application_secrets WHERE secret_kind = $secretKind);";
        command.Parameters.AddWithValue("$secretKind", ToStorageName(kind));
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) == 1;
    }

    public byte[]? Retrieve(ApplicationSecretKind kind)
    {
        database.Initialize();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT protected_value FROM application_secrets WHERE secret_kind = $secretKind;";
        command.Parameters.AddWithValue("$secretKind", ToStorageName(kind));
        var stored = command.ExecuteScalar() as byte[];
        if (stored is null)
        {
            return null;
        }

        var context = CreateContext(kind);
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

    public bool Delete(ApplicationSecretKind kind)
    {
        database.Initialize();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM application_secrets WHERE secret_kind = $secretKind;";
        command.Parameters.AddWithValue("$secretKind", ToStorageName(kind));
        return command.ExecuteNonQuery() == 1;
    }

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
