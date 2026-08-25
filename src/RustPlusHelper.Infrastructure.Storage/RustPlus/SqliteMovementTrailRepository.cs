using System.Globalization;
using Microsoft.Data.Sqlite;
using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Infrastructure.Storage.Sqlite;

namespace RustPlusHelper.Infrastructure.Storage.RustPlus;

public sealed class SqliteMovementTrailRepository(SqliteDatabase database) : IMovementTrailRepository
{
    public IReadOnlyDictionary<ulong, IReadOnlyList<MovementTrailPoint>> GetAll(Guid serverId)
    {
        database.Initialize();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT steam_id, sampled_utc_ms, world_x, world_y
            FROM movement_trail_points
            WHERE server_id = $serverId
            ORDER BY steam_id, sampled_utc_ms ASC;
            """;
        command.Parameters.AddWithValue("$serverId", serverId.ToString("D"));

        using var reader = command.ExecuteReader();
        var trails = new Dictionary<ulong, List<MovementTrailPoint>>();
        while (reader.Read())
        {
            var steamId = ulong.Parse(reader.GetString(0), CultureInfo.InvariantCulture);
            var point = new MovementTrailPoint(
                reader.GetFloat(2),
                reader.GetFloat(3),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)));
            if (!trails.TryGetValue(steamId, out var points))
            {
                points = [];
                trails[steamId] = points;
            }

            points.Add(point);
        }

        return trails.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<MovementTrailPoint>)pair.Value);
    }

    public void Append(Guid serverId, ulong steamId, MovementTrailPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        database.Initialize();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO movement_trail_points(server_id, steam_id, sampled_utc_ms, world_x, world_y)
            VALUES ($serverId, $steamId, $sampledUtcMs, $worldX, $worldY);
            """;
        command.Parameters.AddWithValue("$serverId", serverId.ToString("D"));
        command.Parameters.AddWithValue("$steamId", steamId.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$sampledUtcMs", point.SampledAtUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$worldX", point.X);
        command.Parameters.AddWithValue("$worldY", point.Y);
        command.ExecuteNonQuery();
    }

    public void PurgeOlderThan(DateTimeOffset cutoffUtc)
    {
        database.Initialize();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM movement_trail_points WHERE sampled_utc_ms < $cutoffUtcMs;";
        command.Parameters.AddWithValue("$cutoffUtcMs", cutoffUtc.ToUnixTimeMilliseconds());
        command.ExecuteNonQuery();
    }
}
