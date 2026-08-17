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

public enum MapTopologyDiscoveryStatus
{
    Matched,
    NotFound,
    Ambiguous
}

public enum MapTopologyMatchKind
{
    RustClientLog,
    ProceduralSeed
}

public sealed record MapTopologyDiscoveryRequest(
    string ServerHost,
    uint? WorldSize,
    uint? Seed,
    DateTimeOffset? WipeTimeUtc);

public sealed record DiscoveredMapTopology(
    string FilePath,
    string FileName,
    ulong SourceTimestamp,
    MapTopologyMatchKind MatchKind);

public sealed record MapTopologyDiscoveryResult(
    MapTopologyDiscoveryStatus Status,
    DiscoveredMapTopology? Match,
    string Message)
{
    public static MapTopologyDiscoveryResult Matched(DiscoveredMapTopology match, string message) =>
        new(MapTopologyDiscoveryStatus.Matched, match, message);

    public static MapTopologyDiscoveryResult NotFound(string message) =>
        new(MapTopologyDiscoveryStatus.NotFound, null, message);

    public static MapTopologyDiscoveryResult Ambiguous(string message) =>
        new(MapTopologyDiscoveryStatus.Ambiguous, null, message);
}

public sealed record MapTopologyAutoImportResult(
    SavedMapTopology? Topology,
    bool WasImported,
    string Message,
    bool IsError = false);

/// <summary>
/// Reads an external Rust world file into application-owned, display-ready data. Implementations
/// must not retain the source file or expose its full local path.
/// </summary>
public interface IMapTopologyProvider
{
    Task<ImportedMapTopology> ReadAsync(string filePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Finds a locally cached Rust world without exposing Steam-specific discovery to the UI.
/// Implementations must only return a match when it can be tied to the selected server.
/// </summary>
public interface IMapTopologyDiscovery
{
    Task<MapTopologyDiscoveryResult> DiscoverAsync(
        MapTopologyDiscoveryRequest request,
        CancellationToken cancellationToken = default);
}

public interface IMapTopologyRepository
{
    SavedMapTopology? Get(Guid serverId);

    void Upsert(SavedMapTopology topology);
}

public sealed class MapTopologyManager(
    IMapTopologyProvider provider,
    IMapTopologyDiscovery discovery,
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

    public async Task<MapTopologyAutoImportResult> TryAutoImportAsync(
        Guid serverId,
        string serverHost,
        RustPlusHelper.Application.RustPlus.ServerInfoSnapshot server,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverHost);
        ArgumentNullException.ThrowIfNull(server);

        SavedMapTopology? existing;
        try
        {
            existing = repository.Get(serverId);
        }
        catch (InvalidDataException)
        {
            existing = null;
        }

        MapTopologyDiscoveryResult discovered;
        try
        {
            discovered = await discovery.DiscoverAsync(
                new MapTopologyDiscoveryRequest(
                    serverHost,
                    server.MapSize,
                    server.Seed,
                    server.WipeTimeUtc),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new MapTopologyAutoImportResult(
                existing,
                false,
                "Rust's local map cache could not be inspected automatically.",
                true);
        }

        if (discovered.Status != MapTopologyDiscoveryStatus.Matched || discovered.Match is null)
        {
            return new MapTopologyAutoImportResult(
                existing,
                false,
                discovered.Message,
                discovered.Status == MapTopologyDiscoveryStatus.Ambiguous);
        }

        var match = discovered.Match;
        if (existing is not null
            && string.Equals(existing.Data.SourceFileName, match.FileName, StringComparison.OrdinalIgnoreCase)
            && existing.Data.SourceTimestamp == match.SourceTimestamp)
        {
            return new MapTopologyAutoImportResult(
                existing,
                false,
                $"Automatically matched {match.FileName} in Rust's local map cache.");
        }

        var imported = await ImportAsync(
            serverId,
            match.FilePath,
            server.MapSize,
            cancellationToken).ConfigureAwait(false);
        if (!imported.IsSuccess || imported.Topology is null)
        {
            return new MapTopologyAutoImportResult(null, false, imported.Message, true);
        }

        var evidence = match.MatchKind == MapTopologyMatchKind.RustClientLog
            ? "the Rust client connection log"
            : "the procedural map size and seed";
        return new MapTopologyAutoImportResult(
            imported.Topology,
            true,
            $"Automatically imported {match.FileName} after matching {evidence}.");
    }
}
