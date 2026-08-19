using Microsoft.Data.Sqlite;
using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Infrastructure.Storage.Sqlite;

namespace RustPlusHelper.Infrastructure.Storage.RustPlus;

public sealed class SqliteSavedCameraRepository(SqliteDatabase database) : ISavedCameraRepository
{
    public IReadOnlyList<SavedCamera> GetAll(Guid serverId)
    {
        database.Initialize();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, camera_code, nickname, created_utc_ms
            FROM saved_cameras
            WHERE server_id = $serverId
            ORDER BY nickname COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$serverId", serverId.ToString("D"));

        using var reader = command.ExecuteReader();
        var cameras = new List<SavedCamera>();
        while (reader.Read())
        {
            cameras.Add(new SavedCamera(
                Guid.ParseExact(reader.GetString(0), "D"),
                serverId,
                reader.GetString(1),
                reader.GetString(2),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3))));
        }

        return cameras;
    }

    public void Add(SavedCamera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        database.Initialize();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO saved_cameras(id, server_id, camera_code, nickname, created_utc_ms)
            VALUES ($id, $serverId, $cameraCode, $nickname, $createdUtcMs);
            """;
        command.Parameters.AddWithValue("$id", camera.Id.ToString("D"));
        command.Parameters.AddWithValue("$serverId", camera.ServerId.ToString("D"));
        command.Parameters.AddWithValue("$cameraCode", camera.Code);
        command.Parameters.AddWithValue("$nickname", camera.Nickname);
        command.Parameters.AddWithValue("$createdUtcMs", camera.CreatedUtc.ToUnixTimeMilliseconds());
        command.ExecuteNonQuery();
    }

    public bool Remove(Guid serverId, Guid id)
    {
        database.Initialize();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM saved_cameras
            WHERE id = $id AND server_id = $serverId;
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$serverId", serverId.ToString("D"));
        return command.ExecuteNonQuery() > 0;
    }
}
