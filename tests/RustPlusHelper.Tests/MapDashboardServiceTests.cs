using RustPlusHelper.Application.Map;
using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Application.Testing;
using RustPlusHelper.Application.Security;
using RustPlusHelper.Application.Servers;

namespace RustPlusHelper.Tests;

public sealed class MapDashboardServiceTests
{
    [Fact]
    public async Task LoadsMapFirstStateThroughOwnedClientBoundary()
    {
        await using var fixture = CreateFixture();
        var service = fixture.Service;

        await service.InitializeAsync();

        Assert.Equal(DashboardConnectionState.Ready, service.Current.ConnectionState);
        Assert.Equal("Fake EU Main", service.Current.Server?.Name);
        Assert.Equal(2, service.Current.Team?.Members.Count);
        Assert.Equal(3, service.Current.Markers?.Markers.Count);
    }

    [Fact]
    public async Task BuildsProjectedLayeredRenderModel()
    {
        await using var fixture = CreateFixture();
        var service = fixture.Service;
        await service.InitializeAsync();

        var model = MapRenderModelFactory.Create(service.Current);

        Assert.NotNull(model);
        Assert.Equal(1000, model.Width);
        Assert.Equal(9, model.Items.Count);
        Assert.Equal(30, model.Grid.CellCount);
        Assert.Equal("Launch Site", model.Items.Single(item => item.Id == "monument:0").Label);
        Assert.Equal("LS", model.Items.Single(item => item.Id == "monument:0").Glyph);
        Assert.Equal("F22", model.Items.Single(item => item.Id == "monument:0").GridReference);
        Assert.Equal("Small Oil Rig", model.Items.Single(item => item.Id == "monument:1").Label);
        Assert.Equal("SR", model.Items.Single(item => item.Id == "monument:1").Glyph);
        Assert.Contains(model.Items, item => item.Kind == "vending");
        Assert.Contains(model.Items, item => item.Kind == "death");
        Assert.Contains(model.Items, item => item.Kind == "unknown");
        Assert.True(model.LayerVisibility["team"]);
    }

    [Fact]
    public async Task UnavailableLayersCannotBeEnabled()
    {
        await using var fixture = CreateFixture();
        var service = fixture.Service;
        await service.InitializeAsync();

        service.SetLayerVisibility(MapLayerKind.Cameras, true);
        service.SetLayerVisibility(MapLayerKind.Team, false);

        Assert.False(service.Current.Layers.Single(layer => layer.Kind == MapLayerKind.Cameras).IsVisible);
        Assert.False(service.Current.Layers.Single(layer => layer.Kind == MapLayerKind.Team).IsVisible);
    }

    [Fact]
    public async Task ProjectsImportedTopologyRastersAndCenteredMapPathsIntoRustPlusImage()
    {
        await using var fixture = CreateFixture();
        await fixture.Service.InitializeAsync();
        var topology = new SavedMapTopology(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            new ImportedMapTopology(
                "procedural.map",
                new string('A', 64),
                10,
                1,
                4500,
                [new MapSourceLayerSnapshot("topology", 4)],
                0,
                [
                    new MapPathSnapshot(
                        "Road 0",
                        MapPathKind.Road,
                        12,
                        [new MapWorldPoint(0, 0), new MapWorldPoint(4500, 4500)])
                ],
                new MapRasterSnapshot(1, 1, [1, 2, 3, 4]),
                new MapRasterSnapshot(1, 1, [5, 6, 7, 8]),
                new MapRasterSnapshot(1, 1, [9, 10, 11, 12])));
        var state = fixture.Service.Current with
        {
            Topology = topology,
            Layers = MapDashboardState.CreateLiveMapLayers(true, true, topology)
        };

        var model = MapRenderModelFactory.Create(state);

        Assert.NotNull(model);
        Assert.Equal(3, model.Rasters.Count);
        var road = Assert.Single(model.Polylines);
        Assert.Equal(MapLayerKind.Roads, road.Layer);
        Assert.Equal(new ProjectedMapPoint(50, 950), road.Points[0]);
        Assert.Equal(new ProjectedMapPoint(950, 50), road.Points[1]);
    }

    [Fact]
    public async Task SelectedServerLoadsLiveMapAndNextDashboardReopensItFromCache()
    {
        var repository = new InMemoryServerRepository();
        using var secrets = new InMemorySecretStore();
        var servers = new ServerManager(repository, TimeProvider.System, secrets);
        var profile = servers.SaveWithPairing(
            new ServerProfileDraft(
                null,
                "Saved server",
                "companion.example.invalid",
                28082,
                false,
                76561198000000000),
            "123456789");
        var factory = new CountingFakeClientFactory();
        using var connections = new RustPlusConnectionManager(
            servers,
            secrets,
            factory,
            TimeProvider.System);
        var cache = new InMemoryMapCacheRepository();
        await using var liveSessionOne = CreateLiveSession(servers, secrets, factory);

        await using (var liveService = CreateService(servers, connections, liveSessionOne, cache))
        {
            await liveService.InitializeAsync();

            Assert.Equal(MapDashboardDataSource.Live, liveService.Current.DataSource);
            Assert.Equal(profile.Id, liveService.Current.ServerId);
            Assert.NotNull(cache.Get(profile.Id));
            Assert.Equal(2, liveService.Current.Team?.Members.Count);
            Assert.True(liveService.Current.Layers.Single(layer => layer.Kind == MapLayerKind.Team).IsAvailable);
            Assert.True(liveService.Current.Layers.Single(layer => layer.Kind == MapLayerKind.Monuments).IsAvailable);
        }

        await using var liveSessionTwo = CreateLiveSession(servers, secrets, factory);
        await using (var cachedService = CreateService(servers, connections, liveSessionTwo, cache))
        {
            await cachedService.InitializeAsync();
            await WaitUntilAsync(() => cachedService.Current.Team is not null);

            Assert.Equal(MapDashboardDataSource.Cache, cachedService.Current.DataSource);
            Assert.Equal("Live monitoring connected", cachedService.Current.ConnectionLabel);
            Assert.Equal(2, cachedService.Current.Team?.Members.Count);
            Assert.NotNull(cachedService.Current.LiveDataRetrievedAtUtc);

            var cachedMap = cachedService.Current.Map;
            await cachedService.RefreshLiveDataAsync();
            Assert.Same(cachedMap, cachedService.Current.Map);
            Assert.Equal(2, cachedService.Current.Team?.Members.Count);
        }

        Assert.Equal(3, factory.CreateCount);
    }

    private static DashboardFixture CreateFixture()
    {
        var repository = new InMemoryServerRepository();
        var secrets = new InMemorySecretStore();
        var servers = new ServerManager(repository, TimeProvider.System, secrets);
        var connections = new RustPlusConnectionManager(
            servers,
            secrets,
            new FakeClientFactory(),
            TimeProvider.System);
        var liveSession = CreateLiveSession(servers, secrets, new FakeClientFactory());
        var client = new FakeRustPlusClient();
        var service = new MapDashboardService(
            client,
            new RustPlusConnectionOptions("fake.invalid", 28082, 1, 2),
            servers,
            connections,
            liveSession,
            new InMemoryMapCacheRepository(),
            CreateTopologyManager(),
            TimeProvider.System);
        return new DashboardFixture(service, connections, liveSession, secrets);
    }

    private static MapDashboardService CreateService(
        ServerManager servers,
        RustPlusConnectionManager connections,
        RustPlusLiveSessionManager liveSession,
        IMapCacheRepository cache) =>
        new(
            new FakeRustPlusClient(),
            new RustPlusConnectionOptions("fake.invalid", 28082, 1, 2),
            servers,
            connections,
            liveSession,
            cache,
            CreateTopologyManager(),
            TimeProvider.System);

    private static MapTopologyManager CreateTopologyManager() =>
        new(
            new UnavailableMapTopologyProvider(),
            new UnavailableMapTopologyDiscovery(),
            new InMemoryMapTopologyRepository(),
            TimeProvider.System);

    private static RustPlusLiveSessionManager CreateLiveSession(
        ServerManager servers,
        ISecretStore secrets,
        IRustPlusClientFactory factory) =>
        new(
            new RustPlusSavedConnectionResolver(servers, secrets),
            factory,
            TimeProvider.System,
            RustPlusPollingOptions.Default,
            new InMemoryCompanionEventRepository());

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class FakeClientFactory : IRustPlusClientFactory
    {
        public IRustPlusClient Create() => new FakeRustPlusClient();
    }

    private sealed class CountingFakeClientFactory : IRustPlusClientFactory
    {
        public int CreateCount { get; private set; }

        public IRustPlusClient Create()
        {
            CreateCount++;
            return new FakeRustPlusClient();
        }
    }

    private sealed class DashboardFixture(
        MapDashboardService service,
        RustPlusConnectionManager connections,
        RustPlusLiveSessionManager liveSession,
        InMemorySecretStore secrets) : IAsyncDisposable
    {
        public MapDashboardService Service { get; } = service;

        public async ValueTask DisposeAsync()
        {
            await Service.DisposeAsync();
            await liveSession.DisposeAsync();
            connections.Dispose();
            secrets.Dispose();
        }
    }
}
