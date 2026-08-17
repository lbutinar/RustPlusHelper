using System.Buffers.Binary;
using RustPlusHelper.Application.Map;
using RustPlusHelper.Infrastructure.Map;

namespace RustPlusHelper.Infrastructure.Map.Tests;

public sealed class RustMapCacheDiscoveryTests
{
    [Fact]
    public async Task MatchesCurrentClientCacheSuffixThroughServerConnectionLog()
    {
        using var install = TestRustInstall.Create();
        var cachedName = "proceduralmap.4500.1700000000000.287_0123456789abcdef0123456789abcdef_1234567890.map";
        install.AddMap(cachedName, sourceTimestamp: 1700000000000);
        install.WriteLog(
            "Connecting: 192.0.2.25:28015",
            "World cache proceduralmap.4500.1700000000000.287_0123456789abcdef0123456789abcdef.map");
        var discovery = new RustMapCacheDiscovery([install.Path]);

        var result = await discovery.DiscoverAsync(new MapTopologyDiscoveryRequest(
            "192.0.2.25",
            4500,
            1234,
            DateTimeOffset.UtcNow.AddDays(-1)));

        Assert.Equal(MapTopologyDiscoveryStatus.Matched, result.Status);
        Assert.Equal(MapTopologyMatchKind.RustClientLog, result.Match?.MatchKind);
        Assert.Equal(cachedName, result.Match?.FileName);
        Assert.DoesNotContain(install.Path, result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FallsBackToDocumentedProceduralSizeAndSeedName()
    {
        using var install = TestRustInstall.Create();
        install.AddMap("proceduralmap.4000.1234.map", sourceTimestamp: 10);
        var discovery = new RustMapCacheDiscovery([install.Path]);

        var result = await discovery.DiscoverAsync(new MapTopologyDiscoveryRequest(
            "server.example.invalid",
            4000,
            1234,
            null));

        Assert.Equal(MapTopologyDiscoveryStatus.Matched, result.Status);
        Assert.Equal(MapTopologyMatchKind.ProceduralSeed, result.Match?.MatchKind);
    }

    [Fact]
    public async Task DoesNotGuessFromNewestSameSizeMap()
    {
        using var install = TestRustInstall.Create();
        install.AddMap("proceduralmap.4500.111.map", sourceTimestamp: 11);
        install.AddMap("proceduralmap.4500.222.map", sourceTimestamp: 22);
        var discovery = new RustMapCacheDiscovery([install.Path]);

        var result = await discovery.DiscoverAsync(new MapTopologyDiscoveryRequest(
            "server.example.invalid",
            4500,
            333,
            null));

        Assert.Equal(MapTopologyDiscoveryStatus.NotFound, result.Status);
        Assert.Null(result.Match);
        Assert.Contains("none could be tied safely", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IgnoresCacheFilesOlderThanTheReportedWipe()
    {
        using var install = TestRustInstall.Create();
        var mapPath = install.AddMap("proceduralmap.4500.1234.map", sourceTimestamp: 10);
        File.SetLastWriteTimeUtc(mapPath, DateTime.UtcNow.AddDays(-10));
        var discovery = new RustMapCacheDiscovery([install.Path]);

        var result = await discovery.DiscoverAsync(new MapTopologyDiscoveryRequest(
            "server.example.invalid",
            4500,
            1234,
            DateTimeOffset.UtcNow.AddDays(-2)));

        Assert.Equal(MapTopologyDiscoveryStatus.NotFound, result.Status);
        Assert.Null(result.Match);
    }

    private sealed class TestRustInstall : IDisposable
    {
        private TestRustInstall(string path)
        {
            Path = path;
            Directory.CreateDirectory(System.IO.Path.Combine(path, "maps"));
        }

        public string Path { get; }

        public static TestRustInstall Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "RustPlusHelper.MapDiscovery.Tests",
                Guid.NewGuid().ToString("N"));
            return new TestRustInstall(path);
        }

        public string AddMap(string fileName, ulong sourceTimestamp)
        {
            var path = System.IO.Path.Combine(Path, "maps", fileName);
            Span<byte> header = stackalloc byte[12];
            BinaryPrimitives.WriteInt32LittleEndian(header[..4], 10);
            BinaryPrimitives.WriteUInt64LittleEndian(header[4..], sourceTimestamp);
            File.WriteAllBytes(path, header.ToArray());
            return path;
        }

        public void WriteLog(params string[] lines) =>
            File.WriteAllLines(System.IO.Path.Combine(Path, "output_log.txt"), lines);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
