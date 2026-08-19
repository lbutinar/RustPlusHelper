using System.Globalization;
using Microsoft.Data.Sqlite;
using RustPlusHelper.Application.Servers;
using RustPlusHelper.Infrastructure.Storage.Sqlite;

namespace RustPlusHelper.Infrastructure.Storage.Servers;

public sealed class SqliteServerRepository(SqliteDatabase database) : IServerRepository
{
    public IReadOnlyList<ServerProfile> GetAll()
    {
        database.Initialize();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, display_name, host, port, use_facepunch_proxy, player_id,
                   created_utc_ms, updated_utc_ms, last_selected_utc_ms, rust_plus_server_id
            FROM servers
            ORDER BY last_selected_utc_ms DESC NULLS LAST, display_name COLLATE NOCASE, id;
            """;

        using var reader = command.ExecuteReader();
        var results = new List<ServerProfile>();
        while (reader.Read())
        {
            results.Add(ReadProfile(reader));
        }

        return results;
    }

    public ServerProfile? GetById(Guid id)
    {
        database.Initialize();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, display_name, host, port, use_facepunch_proxy, player_id,
                   created_utc_ms, updated_utc_ms, last_selected_utc_ms, rust_plus_server_id
            FROM servers
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadProfile(reader) : null;
    }

    public void Upsert(ServerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        database.Initialize();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO servers(
                id, display_name, host, port, use_facepunch_proxy, player_id,
                created_utc_ms, updated_utc_ms, last_selected_utc_ms, rust_plus_server_id)
            VALUES (
                $id, $displayName, $host, $port, $useFacepunchProxy, $playerId,
                $createdUtcMs, $updatedUtcMs, $lastSelectedUtcMs, $rustPlusServerId)
            ON CONFLICT(id) DO UPDATE SET
                display_name = excluded.display_name,
                host = excluded.host,
                port = excluded.port,
                use_facepunch_proxy = excluded.use_facepunch_proxy,
                player_id = excluded.player_id,
                updated_utc_ms = excluded.updated_utc_ms,
                last_selected_utc_ms = excluded.last_selected_utc_ms,
                rust_plus_server_id = excluded.rust_plus_server_id;
            """;
        command.Parameters.AddWithValue("$id", profile.Id.ToString("D"));
        command.Parameters.AddWithValue("$displayName", profile.DisplayName);
        command.Parameters.AddWithValue("$host", profile.Host);
        command.Parameters.AddWithValue("$port", profile.Port);
        command.Parameters.AddWithValue("$useFacepunchProxy", profile.UseFacepunchProxy ? 1 : 0);
        command.Parameters.AddWithValue("$playerId", profile.PlayerId is null
            ? DBNull.Value
            : profile.PlayerId.Value.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$createdUtcMs", profile.CreatedUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$updatedUtcMs", profile.UpdatedUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$lastSelectedUtcMs", profile.LastSelectedUtc is null
            ? DBNull.Value
            : profile.LastSelectedUtc.Value.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$rustPlusServerId", profile.RustPlusServerId is null
            ? DBNull.Value
            : profile.RustPlusServerId.Value.ToString("D"));
        command.ExecuteNonQuery();
    }

    public bool Remove(Guid id)
    {
        database.Initialize();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM servers WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        return command.ExecuteNonQuery() == 1;
    }

    private static ServerProfile ReadProfile(SqliteDataReader reader)
    {
        ulong? playerId = reader.IsDBNull(5)
            ? null
            : ulong.Parse(reader.GetString(5), CultureInfo.InvariantCulture);
        DateTimeOffset? lastSelected = reader.IsDBNull(8)
            ? null
            : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(8));
        Guid? rustPlusServerId = reader.IsDBNull(9)
            ? null
            : Guid.ParseExact(reader.GetString(9), "D");

        return new ServerProfile(
            Guid.ParseExact(reader.GetString(0), "D"),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.GetInt32(4) == 1,
            playerId,
            DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(6)),
            DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(7)),
            lastSelected,
            rustPlusServerId);
    }
}
