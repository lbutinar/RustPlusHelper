using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using RustPlusHelper.Application.Map;

namespace RustPlusHelper.Infrastructure.Map;

/// <summary>
/// Correlates Rust's local world cache with a saved server. A Rust client log association is
/// preferred because current Facepunch cache names do not always contain the Rust+ seed.
/// </summary>
public sealed class RustMapCacheDiscovery(IEnumerable<string> rustInstallDirectories) : IMapTopologyDiscovery
{
    private const int HeaderSize = 12;
    private const int MaximumLogTailBytes = 8 * 1024 * 1024;
    private const int MaximumConnectionScopeLines = 1_000;
    private static readonly TimeSpan WipeTimestampTolerance = TimeSpan.FromDays(1);
    private static readonly Regex ProceduralNamePattern = new(
        @"^.+?\.(?<size>\d{3,5})\.(?<seed>\d+)(?:[._].*)?\.map$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex MapReferencePattern = new(
        "(?<map>[A-Za-z0-9][^\\s\\\"'<>]*?\\.map)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private readonly string[] _rustInstallDirectories = rustInstallDirectories
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(Path.GetFullPath)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public async Task<MapTopologyDiscoveryResult> DiscoverAsync(
        MapTopologyDiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ServerHost);

        var candidates = ReadCandidates(request, cancellationToken);
        if (candidates.Count == 0)
        {
            return MapTopologyDiscoveryResult.NotFound(
                "No compatible Rust .map files were found in the local Steam cache. Join this server in Rust once, then refresh.");
        }

        var logMatches = new List<CachedMapCandidate>();
        foreach (var installDirectory in _rustInstallDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var logPath = Path.Combine(installDirectory, "output_log.txt");
            var mapReference = await FindLatestServerMapReferenceAsync(
                logPath,
                request.ServerHost,
                cancellationToken).ConfigureAwait(false);
            if (mapReference is null)
            {
                continue;
            }

            logMatches.AddRange(candidates.Where(candidate =>
                IsWithinInstall(candidate.FilePath, installDirectory)
                && FileNameMatchesLogReference(candidate.FileName, mapReference)));
        }

        var distinctLogMatches = DistinctFiles(logMatches);
        if (distinctLogMatches.Count == 1)
        {
            return Matched(distinctLogMatches[0], MapTopologyMatchKind.RustClientLog);
        }

        if (distinctLogMatches.Count > 1)
        {
            return MapTopologyDiscoveryResult.Ambiguous(
                "Rust's client log matched more than one cached map. Choose the current .map file manually.");
        }

        if (request.WorldSize is { } expectedSize && request.Seed is { } expectedSeed)
        {
            var seedMatches = DistinctFiles(candidates.Where(candidate =>
                candidate.WorldSize == expectedSize && candidate.Seed == expectedSeed));
            if (seedMatches.Count == 1)
            {
                return Matched(seedMatches[0], MapTopologyMatchKind.ProceduralSeed);
            }

            if (seedMatches.Count > 1)
            {
                return MapTopologyDiscoveryResult.Ambiguous(
                    "Multiple cached Rust maps have the server's procedural size and seed. Choose the current file manually.");
            }
        }

        return MapTopologyDiscoveryResult.NotFound(
            candidates.Count == 1
                ? "A same-size Rust map exists locally, but it could not be tied safely to this server. Join the server in Rust once, then refresh."
                : $"Rust has {candidates.Count.ToString(CultureInfo.InvariantCulture)} possible cached maps, but none could be tied safely to this server. Join it in Rust once, then refresh.");
    }

    private List<CachedMapCandidate> ReadCandidates(
        MapTopologyDiscoveryRequest request,
        CancellationToken cancellationToken)
    {
        var candidates = new List<CachedMapCandidate>();
        foreach (var installDirectory in _rustInstallDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mapsDirectory = Path.Combine(installDirectory, "maps");
            if (!Directory.Exists(mapsDirectory))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(mapsDirectory, "*.map", SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var filePath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryReadCandidate(filePath, out var candidate)
                    || request.WorldSize is { } expectedSize
                        && candidate.WorldSize is { } namedSize
                        && namedSize != expectedSize
                    || request.WipeTimeUtc is { } wipeTime
                        && candidate.LastWriteTimeUtc < wipeTime - WipeTimestampTolerance)
                {
                    continue;
                }

                candidates.Add(candidate);
            }
        }

        return DistinctFiles(candidates);
    }

    private static bool TryReadCandidate(string filePath, out CachedMapCandidate candidate)
    {
        candidate = default!;
        try
        {
            using var file = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                HeaderSize,
                FileOptions.SequentialScan);
            if (file.Length < HeaderSize)
            {
                return false;
            }

            Span<byte> header = stackalloc byte[HeaderSize];
            file.ReadExactly(header);
            var fileName = Path.GetFileName(filePath);
            var nameMatch = ProceduralNamePattern.Match(fileName);
            uint? worldSize = null;
            ulong? seed = null;
            if (nameMatch.Success
                && uint.TryParse(nameMatch.Groups["size"].Value, CultureInfo.InvariantCulture, out var parsedSize)
                && ulong.TryParse(nameMatch.Groups["seed"].Value, CultureInfo.InvariantCulture, out var parsedSeed))
            {
                worldSize = parsedSize;
                seed = parsedSeed;
            }

            candidate = new CachedMapCandidate(
                Path.GetFullPath(filePath),
                fileName,
                BinaryPrimitives.ReadUInt64LittleEndian(header[4..]),
                File.GetLastWriteTimeUtc(filePath),
                worldSize,
                seed);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task<string?> FindLatestServerMapReferenceAsync(
        string logPath,
        string serverHost,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(logPath))
        {
            return null;
        }

        string text;
        try
        {
            text = await ReadTailAsync(logPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        for (var connectionIndex = lines.Length - 1; connectionIndex >= 0; connectionIndex--)
        {
            var connectionLine = lines[connectionIndex];
            if (!connectionLine.Contains("Connecting:", StringComparison.OrdinalIgnoreCase)
                || !ContainsHost(connectionLine, serverHost))
            {
                continue;
            }

            var end = Math.Min(lines.Length, connectionIndex + MaximumConnectionScopeLines);
            string? latestReference = null;
            for (var lineIndex = connectionIndex + 1; lineIndex < end; lineIndex++)
            {
                if (lines[lineIndex].Contains("Connecting:", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                var reference = MapReferencePattern.Match(lines[lineIndex]);
                if (reference.Success)
                {
                    latestReference = FileNameFromReference(reference.Groups["map"].Value);
                }
            }

            if (latestReference is not null)
            {
                return latestReference;
            }
        }

        return null;
    }

    private static bool ContainsHost(string line, string host)
    {
        var searchStart = 0;
        while (searchStart < line.Length)
        {
            var index = line.IndexOf(host, searchStart, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            var beforeIsHostCharacter = index > 0 && IsHostCharacter(line[index - 1]);
            var afterIndex = index + host.Length;
            var afterIsHostCharacter = afterIndex < line.Length && IsHostCharacter(line[afterIndex]);
            if (!beforeIsHostCharacter && !afterIsHostCharacter)
            {
                return true;
            }

            searchStart = index + host.Length;
        }

        return false;
    }

    private static bool IsHostCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '.' or '-';

    private static async Task<string> ReadTailAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var bytesToRead = (int)Math.Min(file.Length, MaximumLogTailBytes);
        if (file.Length > bytesToRead)
        {
            file.Seek(-bytesToRead, SeekOrigin.End);
        }

        var buffer = new byte[bytesToRead];
        await file.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
        var text = Encoding.UTF8.GetString(buffer);
        if (file.Length <= bytesToRead)
        {
            return text;
        }

        var firstLineBreak = text.IndexOf('\n', StringComparison.Ordinal);
        return firstLineBreak >= 0 ? text[(firstLineBreak + 1)..] : string.Empty;
    }

    private static bool FileNameMatchesLogReference(string fileName, string mapReference)
    {
        if (string.Equals(fileName, mapReference, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var cachedStem = Path.GetFileNameWithoutExtension(fileName);
        var referenceStem = Path.GetFileNameWithoutExtension(mapReference);
        return cachedStem.StartsWith(referenceStem + "_", StringComparison.OrdinalIgnoreCase);
    }

    private static string FileNameFromReference(string reference)
    {
        var normalized = reference.Replace('\\', '/');
        var lastSeparator = normalized.LastIndexOf('/');
        return lastSeparator >= 0 ? normalized[(lastSeparator + 1)..] : normalized;
    }

    private static bool IsWithinInstall(string filePath, string installDirectory)
    {
        var mapsDirectory = Path.GetFullPath(Path.Combine(installDirectory, "maps"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return Path.GetFullPath(filePath).StartsWith(mapsDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static List<CachedMapCandidate> DistinctFiles(IEnumerable<CachedMapCandidate> candidates) =>
        candidates
            .GroupBy(candidate => candidate.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

    private static MapTopologyDiscoveryResult Matched(
        CachedMapCandidate candidate,
        MapTopologyMatchKind matchKind) =>
        MapTopologyDiscoveryResult.Matched(
            new DiscoveredMapTopology(
                candidate.FilePath,
                candidate.FileName,
                candidate.SourceTimestamp,
                matchKind),
            "Rust's local map cache was matched to the selected server.");

    private sealed record CachedMapCandidate(
        string FilePath,
        string FileName,
        ulong SourceTimestamp,
        DateTime LastWriteTimeUtc,
        uint? WorldSize,
        ulong? Seed);
}
