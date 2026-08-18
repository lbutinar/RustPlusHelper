using System.Security.Cryptography;
using System.Text;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using RustPlusHelper.Application.Identity;
using RustPlusHelper.Application.Map;
using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Application.Security;
using RustPlusHelper.Application.Servers;
using RustPlusHelper.Application.Testing;
using RustPlusHelper.Desktop;
using RustPlusHelper.Desktop.Components;
using RustPlusHelper.Desktop.Services;

namespace RustPlusHelper.Desktop.Tests;

public sealed class MainComponentTests : BunitContext
{
    private readonly MapDashboardService _dashboard;
    private readonly InMemoryServerRepository _serverRepository;
    private readonly InMemorySecretStore _secretStore;
    private readonly PlayerIdentityManager _identityManager;
    private readonly RustPlusConnectionManager _connections;

    public MainComponentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _serverRepository = new InMemoryServerRepository();
        _secretStore = new InMemorySecretStore();
        _identityManager = new PlayerIdentityManager(
            new InMemoryPlayerIdentityRepository(),
            TimeProvider.System,
            _serverRepository,
            _secretStore);
        var serverManager = new ServerManager(
            _serverRepository,
            TimeProvider.System,
            _secretStore,
            _identityManager);
        _connections = new RustPlusConnectionManager(
            serverManager,
            _secretStore,
            new FakeClientFactory(),
            TimeProvider.System);
        var liveSession = new RustPlusLiveSessionManager(
            new RustPlusSavedConnectionResolver(serverManager, _secretStore),
            new FakeClientFactory(),
            TimeProvider.System,
            RustPlusPollingOptions.Default,
            new InMemoryCompanionEventRepository());
        var client = new FakeRustPlusClient();
        var connection = new RustPlusConnectionOptions("fake.invalid", 28082, 1, 2);
        var mapCache = new InMemoryMapCacheRepository();
        var mapTopology = new MapTopologyManager(
            new UnavailableMapTopologyProvider(),
            new UnavailableMapTopologyDiscovery(),
            new InMemoryMapTopologyRepository(),
            TimeProvider.System);
        _dashboard = new MapDashboardService(
            client,
            connection,
            serverManager,
            _connections,
            liveSession,
            mapCache,
            mapTopology,
            TimeProvider.System);

        // Externally constructed instances are owned by this short-lived test process and keep the
        // component test independent from the Windows desktop composition root.
        Services.AddSingleton<IRustPlusClient>(client);
        Services.AddSingleton(connection);
        Services.AddSingleton(_dashboard);
        Services.AddSingleton<ISecretStore>(_secretStore);
        Services.AddSingleton(_identityManager);
        Services.AddSingleton(serverManager);
        Services.AddSingleton(_connections);
        Services.AddSingleton(liveSession);
        Services.AddSingleton<IMapCacheRepository>(mapCache);
        Services.AddSingleton(mapTopology);
        Services.AddSingleton<IMapFilePicker, NullMapFilePicker>();
    }

    [Fact]
    public void OpensOnMapWithTruthfulLayerControls()
    {
        var component = Render<Main>();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Server map", component.Markup, StringComparison.Ordinal);
            Assert.Contains("FAKE DATA", component.Markup, StringComparison.Ordinal);
            Assert.Contains("Map grid", component.Markup, StringComparison.Ordinal);
            Assert.Contains("Smart devices", component.Markup, StringComparison.Ordinal);
            Assert.Contains("Rust+ does not provide device positions", component.Markup, StringComparison.Ordinal);
            Assert.Single(JSInterop.Invocations, invocation => invocation.Identifier == "rustPlusMap.render");
        });
    }

    [Fact]
    public void UsesRustPlusBrandAssetAndMaterialNavigationIcons()
    {
        var component = Render<Main>();

        component.WaitForAssertion(() =>
        {
            var brandImage = component.Find(".brand-mark img");
            Assert.Equal("assets/rustplus-app-icon.png", brandImage.GetAttribute("src"));
            Assert.Contains("UNOFFICIAL PERSONAL COMPANION", component.Markup, StringComparison.Ordinal);

            var icons = component.FindAll(".nav-icon").Select(icon => icon.TextContent.Trim()).ToArray();
            Assert.Equal(
                ["map", "chat_bubble", "notifications_active", "storefront", "lightbulb", "dns", "settings"],
                icons);
            Assert.All(component.FindAll(".nav-icon"), icon =>
                Assert.Contains("material-icons", icon.ClassList));
        });
    }

    [Fact]
    public void NavigationSwitchesToTeamWithoutProtocolTypesInComponent()
    {
        var component = Render<Main>();
        component.WaitForElement(".map-page");

        component.FindAll("button.nav-item")
            .Single(button => button.TextContent.Contains("Team", StringComparison.Ordinal))
            .Click();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Recent team chat", component.Markup, StringComparison.Ordinal);
            Assert.Contains("Kakec", component.Markup, StringComparison.Ordinal);
            Assert.Contains("I14", component.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void LayerToggleUpdatesApplicationState()
    {
        var component = Render<Main>();
        component.WaitForElement(".layer-list");
        var teamLayer = component.FindAll("label.layer-row")
            .Single(label => label.QuerySelector("strong")?.TextContent == "Team");

        teamLayer.QuerySelector("input")!.Change(false);

        component.WaitForAssertion(() =>
            Assert.False(_dashboard.Current.Layers.Single(layer => layer.Kind == MapLayerKind.Team).IsVisible));
    }

    [Fact]
    public void MapToolbarTogglesGridLayer()
    {
        var component = Render<Main>();
        component.WaitForElement("[data-testid='toggle-grid']").Click();

        component.WaitForAssertion(() =>
            Assert.False(_dashboard.Current.Layers.Single(layer => layer.Kind == MapLayerKind.Grid).IsVisible));
    }

    [Fact]
    public void LiveMapCanvasPassesRustPlusJpegToLeafletAsLocalData()
    {
        var state = new MapDashboardState(
            DashboardConnectionState.Ready,
            "Live map · direct",
            MapDashboardDataSource.Live,
            Guid.Parse("bb1b670b-3711-42df-90d8-9f0ac9b65ea9"),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            false,
            "Live data refreshed",
            null,
            new ServerInfoSnapshot(
                "Live test", null, null, "Procedural Map", 4500, null,
                null, null, null, null, null, null, null, null, null),
            new ServerMapSnapshot(
                1000, 1000, 50, "#FF102030", [], [0xFF, 0xD8, 0xFF, 0xD9]),
            null,
            null,
            null,
            [],
            MapDashboardState.CreateLiveMapLayers(),
            null);

        Render<MapCanvas>(parameters => parameters.Add(component => component.State, state));

        var invocation = Assert.Single(
            JSInterop.Invocations,
            candidate => candidate.Identifier == "rustPlusMap.render");
        var imageSource = Assert.IsType<string>(invocation.Arguments[1]);
        Assert.StartsWith("data:image/jpeg;base64,", imageSource, StringComparison.Ordinal);
    }

    [Fact]
    public void MapCanvasChangesOnlyLeafletVisibilityWhenDataSnapshotsAreUnchanged()
    {
        var state = new MapDashboardState(
            DashboardConnectionState.Ready,
            "Live map · direct",
            MapDashboardDataSource.Live,
            Guid.Parse("bb1b670b-3711-42df-90d8-9f0ac9b65ea9"),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            false,
            "Live data refreshed",
            null,
            new ServerInfoSnapshot(
                "Live test", null, null, "Procedural Map", 4500, null,
                null, null, null, null, null, null, null, null, null),
            new ServerMapSnapshot(
                1000, 1000, 50, "#FF102030", [], [0xFF, 0xD8, 0xFF, 0xD9]),
            null,
            null,
            null,
            [],
            MapDashboardState.CreateLiveMapLayers(),
            null);
        var component = Render<MapCanvas>(parameters => parameters.Add(canvas => canvas.State, state));
        var layers = state.Layers
            .Select(layer => layer.Kind == MapLayerKind.Grid ? layer with { IsVisible = false } : layer)
            .ToArray();

        component.Render(parameters => parameters.Add(canvas => canvas.State, state with
        {
            Layers = layers,
            LiveDataStatus = "Status-only change"
        }));

        Assert.Single(JSInterop.Invocations, invocation => invocation.Identifier == "rustPlusMap.render");
        var visibilityInvocation = Assert.Single(
            JSInterop.Invocations,
            invocation => invocation.Identifier == "rustPlusMap.setLayerVisibility");
        var visibility = Assert.IsAssignableFrom<IReadOnlyDictionary<string, bool>>(
            visibilityInvocation.Arguments[1]);
        Assert.False(visibility["grid"]);
    }

    [Fact]
    public void MapCanvasPassesTopologyRasterThroughExplicitBase64Contract()
    {
        var topology = new SavedMapTopology(
            Guid.Parse("bb1b670b-3711-42df-90d8-9f0ac9b65ea9"),
            DateTimeOffset.UtcNow,
            new ImportedMapTopology(
                "proceduralmap.4500.123.map",
                new string('A', 64),
                10,
                1,
                4500,
                [],
                0,
                [],
                null,
                new MapRasterSnapshot(1, 1, [1, 2, 3, 4]),
                null));
        var state = new MapDashboardState(
            DashboardConnectionState.Ready,
            "Live map · direct",
            MapDashboardDataSource.Live,
            topology.ServerId,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            false,
            "Live data refreshed",
            null,
            new ServerInfoSnapshot(
                "Live test", null, null, "Procedural Map", 4500, null,
                null, null, null, null, null, null, null, null, null),
            new ServerMapSnapshot(
                1000, 1000, 50, "#FF102030", [], [0xFF, 0xD8, 0xFF, 0xD9]),
            null,
            null,
            null,
            [],
            MapDashboardState.CreateLiveMapLayers(topology: topology),
            null,
            topology);

        Render<MapCanvas>(parameters => parameters.Add(component => component.State, state));

        var invocation = Assert.Single(
            JSInterop.Invocations,
            candidate => candidate.Identifier == "rustPlusMap.render");
        var model = Assert.IsAssignableFrom<object>(invocation.Arguments[2]);
        var rasters = Assert.IsAssignableFrom<System.Collections.IEnumerable>(
            model.GetType().GetProperty("Rasters")?.GetValue(model));
        var raster = Assert.Single(rasters.Cast<object>());
        var rgba = Assert.IsType<string>(raster.GetType().GetProperty("Rgba")?.GetValue(raster));
        Assert.Equal("AQIDBA==", rgba);
    }

    [Fact]
    public void ServerPageSavesProfileThroughServerManager()
    {
        var component = Render<Main>();
        component.WaitForElement(".map-page");
        component.FindAll("button.nav-item")
            .Single(button => button.TextContent.Contains("Servers", StringComparison.Ordinal))
            .Click();

        component.Find("[data-testid='server-name']").Change("EU Medium");
        component.Find("[data-testid='server-host']").Change("companion.example.invalid");
        component.Find("[data-testid='server-port']").Change("28100");
        component.Find("[data-testid='server-proxy']").Change(false);
        Assert.Contains("Direct connection is not encrypted", component.Markup, StringComparison.Ordinal);
        component.Find("[data-testid='save-server']").Click();

        component.WaitForAssertion(() =>
        {
            var saved = Assert.Single(_serverRepository.GetAll());
            Assert.Equal("EU Medium", saved.DisplayName);
            Assert.Equal("companion.example.invalid", saved.Host);
            Assert.Equal(28100, saved.Port);
            Assert.False(saved.UseFacepunchProxy);
            Assert.Contains("SQLITE READY", component.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ServerPageMasksAndProtectsManualPairingDetails()
    {
        var component = Render<Main>();
        component.WaitForElement(".map-page");
        component.FindAll("button.nav-item")
            .Single(button => button.TextContent.Contains("Servers", StringComparison.Ordinal))
            .Click();

        component.Find("[data-testid='server-name']").Change("Test Dev");
        component.Find("[data-testid='server-host']").Change("companion.example.invalid");
        component.Find("[data-testid='player-identity-id']").Change("76561198000000000");
        component.Find("[data-testid='save-player-identity']").Click();
        component.Find("[data-testid='server-player-token']").Change("-123456789");
        component.Find("[data-testid='save-server']").Click();

        component.WaitForAssertion(() =>
        {
            var saved = Assert.Single(_serverRepository.GetAll());
            Assert.Equal(76561198000000000UL, saved.PlayerId);
            Assert.Equal(76561198000000000UL, _identityManager.Current?.SteamId);
            Assert.Contains("PAIRING SAVED", component.Markup, StringComparison.Ordinal);
            Assert.Single(component.FindAll("[data-testid='player-identity-id']"));
            Assert.DoesNotContain("-123456789", component.Markup, StringComparison.Ordinal);

            var restored = _secretStore.Retrieve(saved.Id, SecretKind.RustPlusPlayerToken);
            try
            {
                Assert.NotNull(restored);
                Assert.Equal("-123456789", Encoding.UTF8.GetString(restored));
            }
            finally
            {
                if (restored is not null)
                {
                    CryptographicOperations.ZeroMemory(restored);
                }
            }
        });
    }

    [Fact]
    public void ServerPageTestsSavedPairingWithReadOnlyServerInformation()
    {
        var component = Render<Main>();
        component.WaitForElement(".map-page");
        component.FindAll("button.nav-item")
            .Single(button => button.TextContent.Contains("Servers", StringComparison.Ordinal))
            .Click();

        component.Find("[data-testid='player-identity-id']").Change("76561198000000000");
        component.Find("[data-testid='save-player-identity']").Click();
        component.Find("[data-testid='server-name']").Change("Test Dev");
        component.Find("[data-testid='server-host']").Change("companion.example.invalid");
        component.Find("[data-testid='server-player-token']").Change("123456789");
        component.Find("[data-testid='save-server']").Click();
        component.Find("[data-testid='test-server-connection']").Click();

        component.WaitForAssertion(() =>
        {
            var result = component.Find("[data-testid='connection-test-result']");
            Assert.Contains("Connection verified", result.TextContent, StringComparison.Ordinal);
            Assert.Contains("Fake EU Main", result.TextContent, StringComparison.Ordinal);
            Assert.Contains("87 / 200 players", result.TextContent, StringComparison.Ordinal);
        });
    }

    private sealed class FakeClientFactory : IRustPlusClientFactory
    {
        public IRustPlusClient Create() => new FakeRustPlusClient();
    }
}
