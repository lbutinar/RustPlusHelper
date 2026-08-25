using Microsoft.Data.Sqlite;
using RustPlusHelper.Application.Map;
using RustPlusHelper.Infrastructure.Storage.Sqlite;

namespace RustPlusHelper.Infrastructure.Storage.Map;

public sealed class SqlitePersonalMapPinRepository(SqliteDatabase database) : IPersonalMapPinRepository
{
    public IReadOnlyList<PersonalMapPin> GetAll(Guid serverId)
    {
        database.Initialize();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, world_x, world_y, note, created_utc_ms
            FROM personal_map_pins
            WHERE server_id = $serverId
            ORDER BY created_utc_ms;
            """;
        command.Parameters.AddWithValue("$serverId", serverId.ToString("D"));

        using var reader = command.ExecuteReader();
        var pins = new List<PersonalMapPin>();
        while (reader.Read())
        {
            pins.Add(new PersonalMapPin(
                Guid.ParseExact(reader.GetString(0), "D"),
                serverId,
                (float)reader.GetDouble(1),
                (float)reader.GetDouble(2),
                reader.GetString(3),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(4))));
        }

        return pins;
    }

    public void Add(PersonalMapPin pin)
    {
        ArgumentNullException.ThrowIfNull(pin);
        database.Initialize();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO personal_map_pins(id, server_id, world_x, world_y, note, created_utc_ms)
            VALUES ($id, $serverId, $worldX, $worldY, $note, $createdUtcMs);
            """;
        command.Parameters.AddWithValue("$id", pin.Id.ToString("D"));
        command.Parameters.AddWithValue("$serverId", pin.ServerId.ToString("D"));
        command.Parameters.AddWithValue("$worldX", pin.WorldX);
        command.Parameters.AddWithValue("$worldY", pin.WorldY);
        command.Parameters.AddWithValue("$note", pin.Note);
        command.Parameters.AddWithValue("$createdUtcMs", pin.CreatedUtc.ToUnixTimeMilliseconds());
        command.ExecuteNonQuery();
    }

    public bool Remove(Guid serverId, Guid id)
    {
        database.Initialize();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM personal_map_pins
            WHERE id = $id AND server_id = $serverId;
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$serverId", serverId.ToString("D"));
        return command.ExecuteNonQuery() > 0;
    }
}
