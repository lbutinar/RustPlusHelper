using System.Text.Json;
using Microsoft.Data.Sqlite;
using RustPlusHelper.Application.Map;
using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Infrastructure.Storage.Sqlite;

namespace RustPlusHelper.Infrastructure.Storage.Map;

public sealed class SqliteMapCacheRepository(SqliteDatabase database) : IMapCacheRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

    public CachedServerMap? Get(Guid serverId)
    {
        database.Initialize();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT retrieved_utc_ms, metadata_json, jpeg_image
            FROM map_cache
            WHERE server_id = $serverId;
            """;
        command.Parameters.AddWithValue("$serverId", serverId.ToString("D"));

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var metadata = SqliteJsonMetadata.DeserializeOrThrow<MapCacheMetadata>(
            reader.GetString(1), JsonOptions, "The cached Rust+ map metadata is invalid.");
        var image = (byte[])reader.GetValue(2);
        return new CachedServerMap(
            serverId,
            DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
            metadata.Server,
            new ServerMapSnapshot(
                metadata.Width,
                metadata.Height,
                metadata.OceanMargin,
                metadata.BackgroundArgb,
                metadata.Monuments,
                image));
    }

    public void Upsert(CachedServerMap cachedMap)
    {
        ArgumentNullException.ThrowIfNull(cachedMap);
        if (cachedMap.Map.JpegImage.Length == 0)
        {
            throw new ArgumentException("A cached map must contain a JPEG image.", nameof(cachedMap));
        }

        var metadata = new MapCacheMetadata(
            cachedMap.Server,
            cachedMap.Map.Width,
            cachedMap.Map.Height,
            cachedMap.Map.OceanMargin,
            cachedMap.Map.BackgroundArgb,
            cachedMap.Map.Monuments);

        database.Initialize();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO map_cache(server_id, retrieved_utc_ms, metadata_json, jpeg_image)
            VALUES ($serverId, $retrievedUtcMs, $metadataJson, $jpegImage)
            ON CONFLICT(server_id) DO UPDATE SET
                retrieved_utc_ms = excluded.retrieved_utc_ms,
                metadata_json = excluded.metadata_json,
                jpeg_image = excluded.jpeg_image;
            """;
        command.Parameters.AddWithValue("$serverId", cachedMap.ServerId.ToString("D"));
        command.Parameters.AddWithValue("$retrievedUtcMs", cachedMap.RetrievedAtUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$metadataJson", JsonSerializer.Serialize(metadata, JsonOptions));
        command.Parameters.Add("$jpegImage", SqliteType.Blob).Value = cachedMap.Map.JpegImage;
        command.ExecuteNonQuery();
    }

    private sealed record MapCacheMetadata(
        ServerInfoSnapshot Server,
        uint? Width,
        uint? Height,
        int? OceanMargin,
        string BackgroundArgb,
        IReadOnlyList<MapMonumentSnapshot> Monuments);
}
