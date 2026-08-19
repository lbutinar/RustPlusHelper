using Microsoft.Data.Sqlite;
using RustPlusHelper.Application.Pairing;
using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Infrastructure.Storage.Sqlite;

namespace RustPlusHelper.Infrastructure.Storage.RustPlus;

public sealed class SqlitePairedEntityRepository(SqliteDatabase database) : IPairedEntityRepository
{
    public IReadOnlyList<PairedEntity> GetAll(Guid serverId)
    {
        database.Initialize();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, entity_id, entity_type, nickname, created_utc_ms
            FROM paired_entities
            WHERE server_id = $serverId
            ORDER BY nickname COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$serverId", serverId.ToString("D"));

        using var reader = command.ExecuteReader();
        var entities = new List<PairedEntity>();
        while (reader.Read())
        {
            entities.Add(new PairedEntity(
                Guid.ParseExact(reader.GetString(0), "D"),
                serverId,
                ulong.Parse(reader.GetString(1), System.Globalization.CultureInfo.InvariantCulture),
                (PairedEntityKind)reader.GetInt32(2),
                reader.GetString(3),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(4))));
        }

        return entities;
    }

    public void Add(PairedEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        database.Initialize();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO paired_entities(id, server_id, entity_id, entity_type, nickname, created_utc_ms)
            VALUES ($id, $serverId, $entityId, $entityType, $nickname, $createdUtcMs);
            """;
        command.Parameters.AddWithValue("$id", entity.Id.ToString("D"));
        command.Parameters.AddWithValue("$serverId", entity.ServerId.ToString("D"));
        command.Parameters.AddWithValue(
            "$entityId",
            entity.EntityId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$entityType", (int)entity.Kind);
        command.Parameters.AddWithValue("$nickname", entity.Nickname);
        command.Parameters.AddWithValue("$createdUtcMs", entity.CreatedUtc.ToUnixTimeMilliseconds());
        command.ExecuteNonQuery();
    }

    public bool Remove(Guid serverId, Guid id)
    {
        database.Initialize();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM paired_entities
            WHERE id = $id AND server_id = $serverId;
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$serverId", serverId.ToString("D"));
        return command.ExecuteNonQuery() > 0;
    }
}
