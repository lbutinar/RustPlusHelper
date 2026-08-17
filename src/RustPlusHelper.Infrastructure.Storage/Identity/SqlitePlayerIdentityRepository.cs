using System.Globalization;
using Microsoft.Data.Sqlite;
using RustPlusHelper.Application.Identity;
using RustPlusHelper.Infrastructure.Storage.Sqlite;

namespace RustPlusHelper.Infrastructure.Storage.Identity;

public sealed class SqlitePlayerIdentityRepository(SqliteDatabase database) : IPlayerIdentityRepository
{
    public PlayerIdentity? Get()
    {
        database.Initialize();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT steam_id, updated_utc_ms
            FROM player_identity
            WHERE singleton_id = 1;
            """;

        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new PlayerIdentity(
                ulong.Parse(reader.GetString(0), CultureInfo.InvariantCulture),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)))
            : null;
    }

    public void Upsert(PlayerIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        database.Initialize();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO player_identity(singleton_id, steam_id, updated_utc_ms)
            VALUES (1, $steamId, $updatedUtcMs)
            ON CONFLICT(singleton_id) DO UPDATE SET
                steam_id = excluded.steam_id,
                updated_utc_ms = excluded.updated_utc_ms;
            """;
        command.Parameters.AddWithValue("$steamId", identity.SteamId.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updatedUtcMs", identity.UpdatedUtc.ToUnixTimeMilliseconds());
        command.ExecuteNonQuery();
    }
}
