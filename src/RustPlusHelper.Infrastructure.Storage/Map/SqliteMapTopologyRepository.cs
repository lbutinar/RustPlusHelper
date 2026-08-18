using System.Text.Json;
using Microsoft.Data.Sqlite;
using RustPlusHelper.Application.Map;
using RustPlusHelper.Infrastructure.Storage.Sqlite;

namespace RustPlusHelper.Infrastructure.Storage.Map;

public sealed class SqliteMapTopologyRepository(SqliteDatabase database) : IMapTopologyRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

    public SavedMapTopology? Get(Guid serverId)
    {
        database.Initialize();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT imported_utc_ms, metadata_json, biome_rgba, topology_rgba, resource_potential_rgba
            FROM map_topology
            WHERE server_id = $serverId;
            """;
        command.Parameters.AddWithValue("$serverId", serverId.ToString("D"));

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        MapTopologyMetadata metadata;
        try
        {
            metadata = JsonSerializer.Deserialize<MapTopologyMetadata>(reader.GetString(1), JsonOptions)
                ?? throw new InvalidDataException("The saved map topology metadata is invalid.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The saved map topology metadata is invalid.", exception);
        }

        var imported = new ImportedMapTopology(
            metadata.SourceFileName,
            metadata.Sha256,
            metadata.SerializationVersion,
            metadata.SourceTimestamp,
            metadata.WorldSize,
            metadata.SourceLayers,
            metadata.PrefabCount,
            metadata.Paths,
            ReadRaster(reader, 2, metadata.BiomeSize),
            ReadRaster(reader, 3, metadata.TopologySize),
            ReadRaster(reader, 4, metadata.ResourcePotentialSize),
            metadata.NoBuildZones,
            metadata.NoBuildZoneEvidence);
        return new SavedMapTopology(
            serverId,
            DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
            imported);
    }

    public void Upsert(SavedMapTopology topology)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ValidateRaster(topology.Data.BiomeRaster);
        ValidateRaster(topology.Data.TopologyRaster);
        ValidateRaster(topology.Data.ResourcePotentialRaster);

        var metadata = new MapTopologyMetadata(
            topology.Data.SourceFileName,
            topology.Data.Sha256,
            topology.Data.SerializationVersion,
            topology.Data.SourceTimestamp,
            topology.Data.WorldSize,
            topology.Data.SourceLayers,
            topology.Data.PrefabCount,
            topology.Data.Paths,
            SizeOf(topology.Data.BiomeRaster),
            SizeOf(topology.Data.TopologyRaster),
            SizeOf(topology.Data.ResourcePotentialRaster),
            topology.Data.NoBuildZones,
            topology.Data.NoBuildZoneEvidence);

        database.Initialize();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO map_topology(
                server_id, imported_utc_ms, metadata_json, biome_rgba, topology_rgba, resource_potential_rgba)
            VALUES (
                $serverId, $importedUtcMs, $metadataJson, $biomeRgba, $topologyRgba, $resourcePotentialRgba)
            ON CONFLICT(server_id) DO UPDATE SET
                imported_utc_ms = excluded.imported_utc_ms,
                metadata_json = excluded.metadata_json,
                biome_rgba = excluded.biome_rgba,
                topology_rgba = excluded.topology_rgba,
                resource_potential_rgba = excluded.resource_potential_rgba;
            """;
        command.Parameters.AddWithValue("$serverId", topology.ServerId.ToString("D"));
        command.Parameters.AddWithValue("$importedUtcMs", topology.ImportedAtUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$metadataJson", JsonSerializer.Serialize(metadata, JsonOptions));
        AddBlob(command, "$biomeRgba", topology.Data.BiomeRaster?.Rgba);
        AddBlob(command, "$topologyRgba", topology.Data.TopologyRaster?.Rgba);
        AddBlob(command, "$resourcePotentialRgba", topology.Data.ResourcePotentialRaster?.Rgba);
        command.ExecuteNonQuery();
    }

    private static MapRasterSnapshot? ReadRaster(
        SqliteDataReader reader,
        int ordinal,
        RasterSize? size)
    {
        if (reader.IsDBNull(ordinal))
        {
            if (size is not null)
            {
                throw new InvalidDataException("Saved map topology raster metadata has no image data.");
            }

            return null;
        }

        if (size is null)
        {
            throw new InvalidDataException("Saved map topology image data has no dimensions.");
        }

        var raster = new MapRasterSnapshot(size.Width, size.Height, (byte[])reader.GetValue(ordinal));
        ValidateRaster(raster);
        return raster;
    }

    private static void ValidateRaster(MapRasterSnapshot? raster)
    {
        if (raster is null)
        {
            return;
        }

        if (raster.Width <= 0 || raster.Height <= 0 || raster.Rgba.Length != raster.ExpectedByteCount)
        {
            throw new InvalidDataException("A saved map topology raster has invalid dimensions or data length.");
        }
    }

    private static RasterSize? SizeOf(MapRasterSnapshot? raster) =>
        raster is null ? null : new RasterSize(raster.Width, raster.Height);

    private static void AddBlob(SqliteCommand command, string name, byte[]? value)
    {
        var parameter = command.Parameters.Add(name, SqliteType.Blob);
        parameter.Value = value is null ? DBNull.Value : value;
    }

    private sealed record RasterSize(int Width, int Height);

    private sealed record MapTopologyMetadata(
        string SourceFileName,
        string Sha256,
        int SerializationVersion,
        ulong SourceTimestamp,
        uint WorldSize,
        IReadOnlyList<MapSourceLayerSnapshot> SourceLayers,
        int PrefabCount,
        IReadOnlyList<MapPathSnapshot> Paths,
        RasterSize? BiomeSize,
        RasterSize? TopologySize,
        RasterSize? ResourcePotentialSize,
        IReadOnlyList<MapNoBuildZoneSnapshot>? NoBuildZones = null,
        MapNoBuildZoneEvidence? NoBuildZoneEvidence = null);
}
