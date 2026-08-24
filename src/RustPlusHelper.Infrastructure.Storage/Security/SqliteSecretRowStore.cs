using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using RustPlusHelper.Application.Security;
using RustPlusHelper.Infrastructure.Storage.Sqlite;

namespace RustPlusHelper.Infrastructure.Storage.Security;

/// <summary>
/// Shared protect/store/retrieve/delete mechanics for a single-secret-per-key row backed by SQLite,
/// used identically by <see cref="SqliteSecretStore"/> (keyed by server + secret kind) and
/// <see cref="SqliteApplicationSecretStore"/> (keyed by secret kind alone). Callers own their own SQL
/// (table/column names) and key-parameter binding; this type only owns the protect/zero-memory
/// lifecycle both stores previously duplicated verbatim.
/// </summary>
internal static class SqliteSecretRowStore
{
    public static void Store(
        SqliteDatabase database,
        ISecretProtector protector,
        TimeProvider timeProvider,
        string insertSql,
        Action<SqliteCommand> bindKeyParameters,
        byte[] context,
        ReadOnlySpan<byte> secret)
    {
        database.Initialize();
        var protectedValue = protector.Protect(secret, context);
        try
        {
            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = insertSql;
            bindKeyParameters(command);
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

    public static bool Contains(SqliteDatabase database, string existsSql, Action<SqliteCommand> bindKeyParameters)
    {
        database.Initialize();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = existsSql;
        bindKeyParameters(command);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
    }

    public static byte[]? Retrieve(
        SqliteDatabase database,
        ISecretProtector protector,
        string selectSql,
        Action<SqliteCommand> bindKeyParameters,
        byte[] context)
    {
        database.Initialize();
        try
        {
            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = selectSql;
            bindKeyParameters(command);

            var stored = command.ExecuteScalar() as byte[];
            if (stored is null)
            {
                return null;
            }

            try
            {
                return protector.Unprotect(stored, context);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(stored);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(context);
        }
    }

    public static bool Delete(SqliteDatabase database, string deleteSql, Action<SqliteCommand> bindKeyParameters)
    {
        database.Initialize();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = deleteSql;
        bindKeyParameters(command);
        return command.ExecuteNonQuery() == 1;
    }
}
