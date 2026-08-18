using Microsoft.Data.Sqlite;
using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Infrastructure.Storage.Sqlite;

namespace RustPlusHelper.Infrastructure.Storage.RustPlus;

public sealed class SqliteCompanionEventRepository(SqliteDatabase database) : ICompanionEventRepository
{
    public IReadOnlyList<CompanionEvent> GetRecent(Guid serverId, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        database.Initialize();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, occurred_utc_ms, kind, source, title, detail
            FROM companion_events
            WHERE server_id = $serverId
            ORDER BY occurred_utc_ms DESC, id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$serverId", serverId.ToString("D"));
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var events = new List<CompanionEvent>();
        while (reader.Read())
        {
            events.Add(new CompanionEvent(
                Guid.ParseExact(reader.GetString(0), "D"),
                serverId,
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)),
                Enum.Parse<CompanionEventKind>(reader.GetString(2), ignoreCase: false),
                Enum.Parse<CompanionEventSource>(reader.GetString(3), ignoreCase: false),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return events;
    }

    public void Append(CompanionEvent companionEvent, int retentionLimit)
    {
        ArgumentNullException.ThrowIfNull(companionEvent);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(retentionLimit);
        database.Initialize();
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO companion_events(id, server_id, occurred_utc_ms, kind, source, title, detail)
                VALUES ($id, $serverId, $occurredUtcMs, $kind, $source, $title, $detail);
                """;
            insert.Parameters.AddWithValue("$id", companionEvent.Id.ToString("D"));
            insert.Parameters.AddWithValue("$serverId", companionEvent.ServerId.ToString("D"));
            insert.Parameters.AddWithValue("$occurredUtcMs", companionEvent.OccurredAtUtc.ToUnixTimeMilliseconds());
            insert.Parameters.AddWithValue("$kind", companionEvent.Kind.ToString());
            insert.Parameters.AddWithValue("$source", companionEvent.Source.ToString());
            insert.Parameters.AddWithValue("$title", companionEvent.Title);
            insert.Parameters.AddWithValue("$detail", companionEvent.Detail is null ? DBNull.Value : companionEvent.Detail);
            insert.ExecuteNonQuery();
        }

        using (var trim = connection.CreateCommand())
        {
            trim.Transaction = transaction;
            trim.CommandText = """
                DELETE FROM companion_events
                WHERE server_id = $serverId
                  AND id NOT IN (
                      SELECT id
                      FROM companion_events
                      WHERE server_id = $serverId
                      ORDER BY occurred_utc_ms DESC, id DESC
                      LIMIT $retentionLimit
                  );
                """;
            trim.Parameters.AddWithValue("$serverId", companionEvent.ServerId.ToString("D"));
            trim.Parameters.AddWithValue("$retentionLimit", retentionLimit);
            trim.ExecuteNonQuery();
        }

        transaction.Commit();
    }
}
