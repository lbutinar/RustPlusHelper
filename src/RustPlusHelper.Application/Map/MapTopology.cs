namespace RustPlusHelper.Application.Map;

public enum MapPathKind
{
    Road,
    Railway,
    River,
    Other
}

public sealed record MapWorldPoint(float X, float Y);

public sealed record MapPathSnapshot(
    string Name,
    MapPathKind Kind,
    float Width,
    IReadOnlyList<MapWorldPoint> Nodes);

public sealed record MapRasterSnapshot(int Width, int Height, byte[] Rgba)
{
    public int ExpectedByteCount => checked(Width * Height * 4);
}

public sealed record MapSourceLayerSnapshot(string Name, int ByteCount);

public sealed record ImportedMapTopology(
    string SourceFileName,
    string Sha256,
    int SerializationVersion,
    ulong SourceTimestamp,
    uint WorldSize,
    IReadOnlyList<MapSourceLayerSnapshot> SourceLayers,
    int PrefabCount,
    IReadOnlyList<MapPathSnapshot> Paths,
    MapRasterSnapshot? BiomeRaster,
    MapRasterSnapshot? TopologyRaster,
    MapRasterSnapshot? ResourcePotentialRaster);

public sealed record SavedMapTopology(
    Guid ServerId,
    DateTimeOffset ImportedAtUtc,
    ImportedMapTopology Data);

public sealed record MapTopologyImportResult(
    bool IsSuccess,
    SavedMapTopology? Topology,
    string Message)
{
    public static MapTopologyImportResult Success(SavedMapTopology topology, string message) =>
        new(true, topology, message);

    public static MapTopologyImportResult Failure(string message) =>
        new(false, null, message);
}

/// <summary>
/// Reads an external Rust world file into application-owned, display-ready data. Implementations
/// must not retain the source file or expose its full local path.
/// </summary>
public interface IMapTopologyProvider
{
    Task<ImportedMapTopology> ReadAsync(string filePath, CancellationToken cancellationToken = default);
}

public interface IMapTopologyRepository
{
    SavedMapTopology? Get(Guid serverId);

    void Upsert(SavedMapTopology topology);
}

public sealed class MapTopologyManager(
    IMapTopologyProvider provider,
    IMapTopologyRepository repository,
    TimeProvider timeProvider)
{
    public SavedMapTopology? Get(Guid serverId) => repository.Get(serverId);

    public async Task<MapTopologyImportResult> ImportAsync(
        Guid serverId,
        string filePath,
        uint? expectedWorldSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        ImportedMapTopology imported;
        try
        {
            imported = await provider.ReadAsync(filePath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException
            or IOException
            or UnauthorizedAccessException)
        {
            return MapTopologyImportResult.Failure(exception.Message);
        }

        if (expectedWorldSize is { } expected && imported.WorldSize != expected)
        {
            return MapTopologyImportResult.Failure(
                $"This file is a {imported.WorldSize} m world, but Rust+ reports {expected} m for the selected server. Nothing was imported.");
        }

        var saved = new SavedMapTopology(serverId, timeProvider.GetUtcNow(), imported);
        try
        {
            repository.Upsert(saved);
        }
        catch (Exception)
        {
            return MapTopologyImportResult.Failure(
                "The Rust map was decoded, but its derived layers could not be saved locally.");
        }
        var validation = expectedWorldSize is null
            ? "Rust+ did not provide a map size, so the file could not be matched to this server."
            : "World size matches Rust+. Rust+ exposes no map checksum, so wipe identity cannot be proven.";
        return MapTopologyImportResult.Success(
            saved,
            $"Imported {imported.SourceFileName}. {validation}");
    }
}
