using System.IO;
using System.Security.Cryptography;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using RustPlusHelper.Application.Diagnostics;
using RustPlusHelper.Application.Identity;
using RustPlusHelper.Application.Map;
using RustPlusHelper.Application.Notifications;
using RustPlusHelper.Application.Pairing;
using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Application.Security;
using RustPlusHelper.Application.Servers;
using RustPlusHelper.Application.Testing;
using RustPlusHelper.Desktop;
using RustPlusHelper.Desktop.Components;
using RustPlusHelper.Desktop.Services;
using RustPlusHelper.Infrastructure.Storage.Diagnostics;

namespace RustPlusHelper.Desktop.Tests;

public sealed class MainComponentTests : BunitContext
{
    private readonly MapDashboardService _dashboard;
    private readonly InMemoryServerRepository _serverRepository;
    private readonly InMemorySecretStore _secretStore;
    private readonly PlayerIdentityManager _identityManager;
    private readonly RustPlusConnectionManager _connections;
    private readonly InMemoryPairedEntityRepository _pairedEntities;
    private readonly RecordingDiagnosticsExportFilePicker _diagnosticsExportFilePicker;
    private readonly FakeClientFactory _liveSessionClientFactory;
    private readonly RecordingEventExportFilePicker _eventExportFilePicker;

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
        var applicationSecrets = new InMemoryApplicationSecretStore();
        var pairing = new RustPlusPairingManager(
            new UnavailablePairingProvider(),
            applicationSecrets,
            _identityManager,
            serverManager);
        pairing.Load();
        _connections = new RustPlusConnectionManager(
            serverManager,
            _secretStore,
            new FakeClientFactory(),
            TimeProvider.System);
        _pairedEntities = new InMemoryPairedEntityRepository();
        _liveSessionClientFactory = new FakeClientFactory();
        var eventHistory = new InMemoryCompanionEventRepository();
        var liveSession = new RustPlusLiveSessionManager(
            new RustPlusSavedConnectionResolver(serverManager, _secretStore),
            _liveSessionClientFactory,
            TimeProvider.System,
            RustPlusPollingOptions.Default,
            eventHistory,
            _pairedEntities,
            new InMemoryMovementTrailRepository());
        var entityPairing = new RustPlusEntityPairingManager(
            new UnavailablePairingProvider(),
            applicationSecrets,
            _identityManager,
            _pairedEntities,
            TimeProvider.System);
        entityPairing.Load();
        var client = new FakeRustPlusClient();
        var connection = new RustPlusConnectionOptions("fake.invalid", 28082, 1, 2);
        var mapCache = new InMemoryMapCacheRepository();
        var mapTopology = new MapTopologyManager(
            new UnavailableMapTopologyProvider(),
            new UnavailableMapTopologyDiscovery(),
            new InMemoryMapTopologyRepository(),
            TimeProvider.System);
        var personalMapPins = new InMemoryPersonalMapPinRepository();
        _dashboard = new MapDashboardService(
            client,
            connection,
            serverManager,
            _connections,
            liveSession,
            mapCache,
            mapTopology,
            personalMapPins,
            TimeProvider.System);

        // Externally constructed instances are owned by this short-lived test process and keep the
        // component test independent from the Windows desktop composition root.
        Services.AddSingleton<IRustPlusClient>(client);
        Services.AddSingleton(connection);
        Services.AddSingleton(_dashboard);
        Services.AddSingleton<ISecretStore>(_secretStore);
        Services.AddSingleton(_identityManager);
        Services.AddSingleton(serverManager);
        Services.AddSingleton(pairing);
        Services.AddSingleton(_connections);
        Services.AddSingleton(liveSession);
        Services.AddSingleton<ISavedCameraRepository>(new InMemorySavedCameraRepository());
        Services.AddSingleton<IPairedEntityRepository>(_pairedEntities);
        Services.AddSingleton(entityPairing);
        Services.AddSingleton<IMapCacheRepository>(mapCache);
        Services.AddSingleton<IPersonalMapPinRepository>(personalMapPins);
        Services.AddSingleton<ICompanionEventRepository>(eventHistory);
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton(mapTopology);
        Services.AddSingleton<IMapFilePicker, NullMapFilePicker>();
        Services.AddSingleton<IStartupRegistration>(new InMemoryStartupRegistration());
        Services.AddSingleton(new NotificationPreferencesStore(applicationSecrets));

        _diagnosticsExportFilePicker = new RecordingDiagnosticsExportFilePicker();
        Services.AddSingleton<IDiagnosticsExportFilePicker>(_diagnosticsExportFilePicker);
        _eventExportFilePicker = new RecordingEventExportFilePicker();
        Services.AddSingleton<IEventExportFilePicker>(_eventExportFilePicker);
        Services.AddSingleton(new DiagnosticsExportService(
            [new InMemoryHealthCheck("Fake check", HealthStatus.Healthy, "All good.")],
            _serverRepository,
            TimeProvider.System,
            "test-version",
            Path.Combine(Path.GetTempPath(), "RustPlusHelper.Desktop.Tests.NoLogsHere", Guid.NewGuid().ToString("N"))));
    }

    private sealed class UnavailablePairingProvider : IRustPlusPairingProvider
    {
        public Task<byte[]> RegisterAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Not available in component tests.");

        public Task<CapturedRustPlusPairing> WaitForServerPairingAsync(
            ReadOnlyMemory<byte> credentials,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Not available in component tests.");

        public Task<CapturedEntityPairing> WaitForEntityPairingAsync(
            ReadOnlyMemory<byte> credentials,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Not available in component tests.");
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
    public void GridSearchFocusesTheRequestedCellAndRejectsAnInvalidReference()
    {
        var component = Render<Main>();
        component.WaitForElement(".map-page");
        component.WaitForAssertion(() =>
            Assert.Single(JSInterop.Invocations, invocation => invocation.Identifier == "rustPlusMap.render"));

        component.Find("[data-testid='grid-search-input']").Input("A0");
        component.Find("[data-testid='grid-search-submit']").Click();

        component.WaitForAssertion(() =>
        {
            var focus = Assert.Single(
                JSInterop.Invocations,
                invocation => invocation.Identifier == "rustPlusMap.focusPixel");
            Assert.IsType<double>(focus.Arguments[1]);
            Assert.IsType<double>(focus.Arguments[2]);
        });
        Assert.DoesNotContain("data-testid=\"grid-search-error\"", component.Markup, StringComparison.Ordinal);

        component.Find("[data-testid='grid-search-input']").Input("ZZ999");
        component.Find("[data-testid='grid-search-submit']").Click();

        component.WaitForAssertion(() =>
            Assert.Contains("isn't a valid grid reference", component.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void PersonalPinsCanBeAddedAndRemovedFromTheMapPage()
    {
        var serverManager = Services.GetRequiredService<ServerManager>();
        var profile = serverManager.SaveWithPairing(
            new ServerProfileDraft(null, "Test server", "companion.example.invalid", 28082, false, 76561198000000000),
            "193746281");

        var component = Render<Main>();
        component.WaitForElement(".map-page");
        component.WaitForElement("[data-testid='add-pin']");

        component.Find("[data-testid='pin-grid-input']").Input("ZZ999");
        component.Find("[data-testid='pin-note-input']").Input("Bad grid");
        component.Find("form.pin-add-form").Submit();
        component.WaitForAssertion(() =>
            Assert.Contains("isn't a valid grid reference", component.Markup, StringComparison.Ordinal));

        component.Find("[data-testid='pin-grid-input']").Input("A0");
        component.Find("[data-testid='pin-note-input']").Input("Ambush spot");
        component.Find("form.pin-add-form").Submit();

        component.WaitForAssertion(() =>
            Assert.Contains("Ambush spot", component.Markup, StringComparison.Ordinal));
        var pinRepository = Services.GetRequiredService<IPersonalMapPinRepository>();
        var pin = Assert.Single(pinRepository.GetAll(profile.Id));
        Assert.Equal("Ambush spot", pin.Note);

        component.Find("[data-testid='remove-pin']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Ambush spot", component.Markup, StringComparison.Ordinal);
            Assert.Empty(pinRepository.GetAll(profile.Id));
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
                ["map", "chat_bubble", "notifications_active", "storefront", "lightbulb", "dns", "settings", "health_and_safety"],
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
    public void VendingSearchFiltersDirectNumericOfferIds()
    {
        var state = MapDashboardState.NotStarted with
        {
            Server = new ServerInfoSnapshot(
                "Fake", null, null, "Procedural Map", 4500, null,
                null, null, null, null, null, null, null, null, null),
            Markers = new MapMarkersSnapshot([
                new MapMarkerSnapshot(
                    1,
                    MapMarkerKind.VendingMachine,
                    150,
                    300,
                    Name: "Weapons",
                    VendingOrders: [
                        new VendingOrderSnapshot(-904863145, 1, -932201673, 85, 3, false, false, 1, 1, null, null)
                    ]),
                new MapMarkerSnapshot(
                    2,
                    MapMarkerKind.VendingMachine,
                    300,
                    300,
                    Name: "Resources",
                    VendingOrders: [
                        new VendingOrderSnapshot(-151838493, 5, -932201673, 20, 7, false, false, 1, 1, null, null)
                    ])
            ])
        };
        var component = Render<VendingPage>(parameters =>
            parameters.Add(page => page.State, state));

        component.Find("[data-testid='vending-search']").Input("-151838493");

        Assert.DoesNotContain("Weapons", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Resources", component.Markup, StringComparison.Ordinal);
        Assert.Contains("GRID C28", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void VendingSearchFiltersByFriendlyItemNameAndRendersResolvedNames()
    {
        var state = MapDashboardState.NotStarted with
        {
            Server = new ServerInfoSnapshot(
                "Fake", null, null, "Procedural Map", 4500, null,
                null, null, null, null, null, null, null, null, null),
            Markers = new MapMarkersSnapshot([
                new MapMarkerSnapshot(
                    1,
                    MapMarkerKind.VendingMachine,
                    150,
                    300,
                    Name: "Weapons",
                    VendingOrders: [
                        new VendingOrderSnapshot(-904863145, 1, -932201673, 85, 3, false, false, 1, 1, null, null)
                    ]),
                new MapMarkerSnapshot(
                    2,
                    MapMarkerKind.VendingMachine,
                    300,
                    300,
                    Name: "Resources",
                    VendingOrders: [
                        new VendingOrderSnapshot(-151838493, 5, -932201673, 20, 7, false, false, 1, 1, null, null)
                    ])
            ])
        };
        var component = Render<VendingPage>(parameters =>
            parameters.Add(page => page.State, state));

        Assert.Contains("Semi-Automatic Rifle", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Wood", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Scrap", component.Markup, StringComparison.Ordinal);

        component.Find("[data-testid='vending-search']").Input("wood");

        Assert.DoesNotContain("Weapons", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Resources", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void VendingLocateReturnsToMapAndFocusesVerifiedMarker()
    {
        var component = Render<Main>();
        component.WaitForElement(".map-page");
        component.FindAll("button.nav-item")
            .Single(button => button.TextContent.Contains("Vending", StringComparison.Ordinal))
            .Click();
        component.WaitForElement("[data-testid='locate-vending']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.NotNull(component.Find(".map-page"));
            var focus = Assert.Single(
                JSInterop.Invocations,
                invocation => invocation.Identifier == "rustPlusMap.focusItem");
            Assert.Equal("marker:2", focus.Arguments[1]);
        });
    }

    [Fact]
    public async Task DevicesPageAddsListsAndViewsASavedCameraWithGatedControls()
    {
        var serverManager = Services.GetRequiredService<ServerManager>();
        var profile = serverManager.SaveWithPairing(
            new ServerProfileDraft(null, "Test server", "companion.example.invalid", 28082, false, 76561198000000000),
            "193746281");
        var serverId = profile.Id;

        var liveSession = Services.GetRequiredService<RustPlusLiveSessionManager>();
        await liveSession.StartAsync(serverId);
        await WaitUntilAsync(() => liveSession.Current.Status == RustPlusLiveSessionStatus.Connected);

        var state = MapDashboardState.NotStarted with { ServerId = serverId };
        var component = Render<DevicesPage>(parameters => parameters.Add(page => page.State, state));

        Assert.Contains("No saved cameras yet.", component.Markup, StringComparison.Ordinal);

        component.Find("[data-testid='camera-code-input']").Input("CAM01");
        component.Find("[data-testid='camera-nickname-input']").Input("Front gate");
        component.Find("form.camera-add-form").Submit();

        Assert.Contains("Front gate", component.Markup, StringComparison.Ordinal);
        Assert.Contains("CAM01", component.Markup, StringComparison.Ordinal);

        component.Find("[data-testid='view-camera']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("VIEWING", component.Markup, StringComparison.Ordinal);
            Assert.Contains("Zoom", component.Markup, StringComparison.Ordinal);
        });
        Assert.DoesNotContain("Shoot", component.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Forward", component.Markup, StringComparison.Ordinal);

        component.Find("[data-testid='stop-viewing-camera']").Click();
        component.WaitForAssertion(() =>
            Assert.DoesNotContain("VIEWING", component.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public async Task DraggingTheCameraFrameSendsThrottledLookCommands()
    {
        var serverManager = Services.GetRequiredService<ServerManager>();
        var profile = serverManager.SaveWithPairing(
            new ServerProfileDraft(null, "Test server", "companion.example.invalid", 28082, false, 76561198000000000),
            "193746281");
        var serverId = profile.Id;

        var liveSession = Services.GetRequiredService<RustPlusLiveSessionManager>();
        await liveSession.StartAsync(serverId);
        await WaitUntilAsync(() => liveSession.Current.Status == RustPlusLiveSessionStatus.Connected);

        var state = MapDashboardState.NotStarted with { ServerId = serverId };
        var component = Render<DevicesPage>(parameters => parameters.Add(page => page.State, state));

        component.Find("[data-testid='camera-code-input']").Input("CAM01");
        component.Find("[data-testid='camera-nickname-input']").Input("Front gate");
        component.Find("form.camera-add-form").Submit();
        component.Find("[data-testid='view-camera']").Click();
        component.WaitForElement("[data-testid='camera-frame']");

        var frame = component.Find("[data-testid='camera-frame']");
        frame.TriggerEvent("onpointerdown", new PointerEventArgs { ClientX = 100, ClientY = 100 });
        frame.TriggerEvent("onpointermove", new PointerEventArgs { ClientX = 130, ClientY = 100 });
        frame.TriggerEvent("onpointerup", new PointerEventArgs { ClientX = 130, ClientY = 100 });

        var client = _liveSessionClientFactory.Clients[^1];
        var call = Assert.Single(client.LookCalls);
        Assert.Equal(30, call.DeltaX);
        Assert.Equal(0, call.DeltaY);
    }

    [Fact]
    public async Task HeldKeyDoesNotMoveANonDroneCamera()
    {
        var serverManager = Services.GetRequiredService<ServerManager>();
        var profile = serverManager.SaveWithPairing(
            new ServerProfileDraft(null, "Test server", "companion.example.invalid", 28082, false, 76561198000000000),
            "193746281");
        var serverId = profile.Id;

        var liveSession = Services.GetRequiredService<RustPlusLiveSessionManager>();
        await liveSession.StartAsync(serverId);
        await WaitUntilAsync(() => liveSession.Current.Status == RustPlusLiveSessionStatus.Connected);

        var state = MapDashboardState.NotStarted with { ServerId = serverId };
        var component = Render<DevicesPage>(parameters => parameters.Add(page => page.State, state));

        component.Find("[data-testid='camera-code-input']").Input("CAM01");
        component.Find("[data-testid='camera-nickname-input']").Input("Front gate");
        component.Find("form.camera-add-form").Submit();
        component.Find("[data-testid='view-camera']").Click();
        component.WaitForElement("[data-testid='camera-frame']");

        var frame = component.Find("[data-testid='camera-frame']");
        frame.TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "w" });
        await Task.Delay(50);
        frame.TriggerEvent("onkeyup", new KeyboardEventArgs { Key = "w" });

        var client = _liveSessionClientFactory.Clients[^1];
        Assert.Empty(client.MoveCalls);
    }

    [Fact]
    public async Task HoldingAMoveKeyRepeatsTheMoveCommandUntilKeyUp()
    {
        var serverManager = Services.GetRequiredService<ServerManager>();
        var profile = serverManager.SaveWithPairing(
            new ServerProfileDraft(null, "Test server", "companion.example.invalid", 28082, false, 76561198000000000),
            "193746281");
        var serverId = profile.Id;

        _liveSessionClientFactory.NextClientIsDrone = true;
        var liveSession = Services.GetRequiredService<RustPlusLiveSessionManager>();
        await liveSession.StartAsync(serverId);
        await WaitUntilAsync(() => liveSession.Current.Status == RustPlusLiveSessionStatus.Connected);

        var state = MapDashboardState.NotStarted with { ServerId = serverId };
        var component = Render<DevicesPage>(parameters => parameters.Add(page => page.State, state));

        component.Find("[data-testid='camera-code-input']").Input("DRONE1");
        component.Find("[data-testid='camera-nickname-input']").Input("Drone");
        component.Find("form.camera-add-form").Submit();
        component.Find("[data-testid='view-camera']").Click();
        component.WaitForElement("[data-testid='camera-frame']");

        var frame = component.Find("[data-testid='camera-frame']");
        frame.TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "w" });

        var client = _liveSessionClientFactory.Clients[^1];
        await AsyncTestHelpers.WaitUntilAsync(
            () => client.MoveCalls.Count(direction => direction == CameraMoveDirection.Forward) >= 2,
            TimeSpan.FromSeconds(3),
            TimeSpan.FromMilliseconds(10));

        frame.TriggerEvent("onkeyup", new KeyboardEventArgs { Key = "w" });
        var countAtKeyUp = client.MoveCalls.Count;
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.Equal(countAtKeyUp, client.MoveCalls.Count);
        Assert.All(client.MoveCalls, direction => Assert.Equal(CameraMoveDirection.Forward, direction));
    }

    [Fact]
    public async Task DevicesPageArmsAndTogglesAPairedSmartSwitch()
    {
        var serverManager = Services.GetRequiredService<ServerManager>();
        var profile = serverManager.SaveWithPairing(
            new ServerProfileDraft(null, "Test server", "companion.example.invalid", 28082, false, 76561198000000000),
            "193746281");
        var serverId = profile.Id;
        _pairedEntities.Add(new PairedEntity(
            Guid.NewGuid(), serverId, 555UL, PairedEntityKind.Switch, "Front gate switch", DateTimeOffset.UtcNow));

        var liveSession = Services.GetRequiredService<RustPlusLiveSessionManager>();
        await liveSession.StartAsync(serverId);
        await WaitUntilAsync(() => liveSession.Current.Status == RustPlusLiveSessionStatus.Connected);
        await WaitUntilAsync(() => liveSession.PairedEntityStates.ContainsKey(555UL));

        var state = MapDashboardState.NotStarted with { ServerId = serverId };
        var component = Render<DevicesPage>(parameters => parameters.Add(page => page.State, state));

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Front gate switch", component.Markup, StringComparison.Ordinal);
            Assert.Contains("SMART SWITCH", component.Markup, StringComparison.Ordinal);
            Assert.Contains("On", component.Markup, StringComparison.Ordinal);
        });

        component.Find("[data-testid='toggle-switch']").Click();

        component.WaitForAssertion(() =>
            Assert.Contains("Off", component.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public async Task DevicesPageShowsRecentAlarmActivityWhenAnAlarmIsPaired()
    {
        var serverManager = Services.GetRequiredService<ServerManager>();
        var profile = serverManager.SaveWithPairing(
            new ServerProfileDraft(null, "Test server", "companion.example.invalid", 28082, false, 76561198000000000),
            "193746281");
        var serverId = profile.Id;
        _pairedEntities.Add(new PairedEntity(
            Guid.NewGuid(), serverId, 777UL, PairedEntityKind.Alarm, "Front door alarm", DateTimeOffset.UtcNow));

        var liveSession = Services.GetRequiredService<RustPlusLiveSessionManager>();
        await liveSession.StartAsync(serverId);
        await WaitUntilAsync(() => liveSession.Current.Status == RustPlusLiveSessionStatus.Connected);

        var triggeredEvent = new CompanionEvent(
            Guid.NewGuid(),
            serverId,
            DateTimeOffset.UtcNow,
            CompanionEventKind.AlarmTriggered,
            CompanionEventSource.Transport,
            "Front Door Alarm",
            "Someone is here!");
        var state = MapDashboardState.NotStarted with { ServerId = serverId, Events = [triggeredEvent] };
        var component = Render<DevicesPage>(parameters => parameters.Add(page => page.State, state));

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Recent alarm activity", component.Markup, StringComparison.Ordinal);
            Assert.Contains("Front Door Alarm", component.Markup, StringComparison.Ordinal);
            Assert.Contains("Someone is here!", component.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task DevicesPageHidesAlarmActivityWhenNoAlarmIsPaired()
    {
        var serverManager = Services.GetRequiredService<ServerManager>();
        var profile = serverManager.SaveWithPairing(
            new ServerProfileDraft(null, "Test server", "companion.example.invalid", 28082, false, 76561198000000000),
            "193746281");
        var serverId = profile.Id;

        var liveSession = Services.GetRequiredService<RustPlusLiveSessionManager>();
        await liveSession.StartAsync(serverId);
        await WaitUntilAsync(() => liveSession.Current.Status == RustPlusLiveSessionStatus.Connected);

        var state = MapDashboardState.NotStarted with { ServerId = serverId };
        var component = Render<DevicesPage>(parameters => parameters.Add(page => page.State, state));

        component.WaitForAssertion(() =>
            Assert.DoesNotContain("Recent alarm activity", component.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TeamPageSendsAChatMessageThroughTheLiveSession()
    {
        var serverManager = Services.GetRequiredService<ServerManager>();
        var profile = serverManager.SaveWithPairing(
            new ServerProfileDraft(null, "Test server", "companion.example.invalid", 28082, false, 76561198000000000),
            "193746281");
        var serverId = profile.Id;

        var liveSession = Services.GetRequiredService<RustPlusLiveSessionManager>();
        await liveSession.StartAsync(serverId);
        await WaitUntilAsync(() => liveSession.Current.Status == RustPlusLiveSessionStatus.Connected);

        var state = MapDashboardState.NotStarted with { ServerId = serverId };
        var component = Render<TeamPage>(parameters => parameters.Add(page => page.State, state));

        component.Find("[data-testid='chat-compose-input']").Input("Heading to launch site");
        component.Find("[data-testid='chat-compose-send']").Click();

        await WaitUntilAsync(() => liveSession.Current.Chat?.Messages.Any(
            message => message.Message == "Heading to launch site") == true);

        component.WaitForAssertion(() =>
        {
            var input = component.Find("[data-testid='chat-compose-input']");
            Assert.Equal(string.Empty, input.GetAttribute("value") ?? string.Empty);
        });
    }

    [Fact]
    public async Task TeamPageLoadsAndSendsClanChatOnExplicitRequestOnly()
    {
        var serverManager = Services.GetRequiredService<ServerManager>();
        var profile = serverManager.SaveWithPairing(
            new ServerProfileDraft(null, "Test server", "companion.example.invalid", 28082, false, 76561198000000000),
            "193746281");
        var serverId = profile.Id;

        var liveSession = Services.GetRequiredService<RustPlusLiveSessionManager>();
        await liveSession.StartAsync(serverId);
        await WaitUntilAsync(() => liveSession.Current.Status == RustPlusLiveSessionStatus.Connected);

        var state = MapDashboardState.NotStarted with { ServerId = serverId };
        var component = Render<TeamPage>(parameters => parameters.Add(page => page.State, state));

        Assert.Contains("Not loaded automatically", component.Markup, StringComparison.Ordinal);
        Assert.Null(liveSession.Current.ClanChat);

        component.Find("[data-testid='clan-chat-refresh']").Click();
        component.WaitForAssertion(() =>
            Assert.Contains("Fake clan message", component.Markup, StringComparison.Ordinal));

        component.Find("[data-testid='clan-compose-input']").Input("regroup at base");
        component.Find("[data-testid='clan-compose-send']").Click();

        await WaitUntilAsync(() => liveSession.Current.ClanChat?.Messages.Any(
            message => message.Message == "regroup at base") == true);
        component.WaitForAssertion(() =>
            Assert.Contains("regroup at base", component.Markup, StringComparison.Ordinal));
    }

    private static Task WaitUntilAsync(Func<bool> condition) =>
        AsyncTestHelpers.WaitUntilAsync(condition, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(5));

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
    public void MapCanvasPassesDerivedDeathHeatSpotsToLeaflet()
    {
        var serverId = Guid.Parse("bb1b670b-3711-42df-90d8-9f0ac9b65ea9");
        var state = new MapDashboardState(
            DashboardConnectionState.Ready,
            "Live map · direct",
            MapDashboardDataSource.Live,
            serverId,
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
            [new CompanionEvent(
                Guid.NewGuid(),
                serverId,
                DateTimeOffset.UtcNow,
                CompanionEventKind.TeamMemberDied,
                CompanionEventSource.SnapshotDiff,
                "Sanitized teammate died",
                Position: new MapPositionSnapshot(100, 200))],
            MapDashboardState.CreateLiveMapLayers(deathHistoryAvailable: true),
            null);

        Render<MapCanvas>(parameters => parameters.Add(component => component.State, state));

        var invocation = Assert.Single(
            JSInterop.Invocations,
            candidate => candidate.Identifier == "rustPlusMap.render");
        var model = Assert.IsAssignableFrom<object>(invocation.Arguments[2]);
        var heatSpots = Assert.IsAssignableFrom<System.Collections.IEnumerable>(
            model.GetType().GetProperty("HeatSpots")?.GetValue(model));
        var spot = Assert.Single(heatSpots.Cast<object>());
        Assert.Equal(1, spot.GetType().GetProperty("Count")?.GetValue(spot));
        Assert.Equal("deathHistory", spot.GetType().GetProperty("Layer")?.GetValue(spot));
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
    public async Task ServersPageShowsCachedStatusSummaryAndSavesWipeCycleEstimate()
    {
        var serverManager = Services.GetRequiredService<ServerManager>();
        var profile = serverManager.SaveWithPairing(
            new ServerProfileDraft(null, "Test server", "companion.example.invalid", 28082, false, 76561198000000000),
            "193746281");

        var mapCache = Services.GetRequiredService<IMapCacheRepository>();
        // Comfortably clear of both truncation boundaries this test checks (Xd ago / next wipe in ~Yd):
        // 2.5 days elapsed always floors to "2d ago", and a 7-day weekly cycle leaves 4.5 days
        // remaining, which always floors to "~4d" — neither is within a day of flipping due to the
        // small amount of real time this test takes to run.
        var wipeTimeUtc = DateTimeOffset.UtcNow.AddDays(-2.5);
        mapCache.Upsert(new CachedServerMap(
            profile.Id,
            DateTimeOffset.UtcNow,
            new ServerInfoSnapshot(
                "Test server", null, null, "Procedural Map", 4500, wipeTimeUtc,
                42, 200, 0, null, null, null, null, null, null),
            new ServerMapSnapshot(1000, 1000, 50, "#FF1C3440", [], [1, 2, 3])));

        var liveSession = Services.GetRequiredService<RustPlusLiveSessionManager>();
        var component = Render<Main>();
        component.WaitForElement(".map-page");
        await WaitUntilAsync(() => liveSession.Current.Status == RustPlusLiveSessionStatus.Connected);

        // Opening the app auto-connects to the last-selected server and records its own
        // "connected" companion event, so seed the test event afterwards to keep it
        // unambiguously the most recent one for this assertion.
        var eventHistory = Services.GetRequiredService<ICompanionEventRepository>();
        eventHistory.Append(
            new CompanionEvent(
                Guid.NewGuid(), profile.Id, DateTimeOffset.UtcNow,
                CompanionEventKind.MarkerAppeared, CompanionEventSource.Transport, "Loot marker placed"),
            200,
            DateTimeOffset.MinValue);

        component.FindAll("button.nav-item")
            .Single(button => button.TextContent.Contains("Servers", StringComparison.Ordinal))
            .Click();
        component.WaitForElement("[data-testid='server-status-summary']");

        Assert.Contains("42 / 200 players", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Wiped 2d ago", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Last event: Loot marker placed", component.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("next wipe", component.Markup, StringComparison.Ordinal);

        component.Find("[data-testid='server-wipe-cycle']").Change("Weekly");

        component.WaitForAssertion(() =>
        {
            Assert.Equal(WipeCycle.Weekly, _serverRepository.GetById(profile.Id)!.WipeCycle);
            Assert.Contains("next wipe in ~4d (your estimate)", component.Markup, StringComparison.Ordinal);
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

    [Fact]
    public void SettingsPageTogglesStartWithWindowsAndNotificationPreferences()
    {
        var component = Render<Main>();
        component.WaitForElement(".map-page");
        component.FindAll("button.nav-item")
            .Single(button => button.TextContent.Contains("Settings", StringComparison.Ordinal))
            .Click();
        component.WaitForElement("[data-testid='start-with-windows']");

        var startupRegistration = Services.GetRequiredService<IStartupRegistration>();
        Assert.False(startupRegistration.IsEnabled);
        component.Find("[data-testid='start-with-windows']").Change(true);
        Assert.True(startupRegistration.IsEnabled);

        var preferencesStore = Services.GetRequiredService<NotificationPreferencesStore>();
        Assert.True(preferencesStore.Get().MarkerEvents);
        component.Find("[data-testid='notify-markers']").Change(false);
        Assert.False(preferencesStore.Get().MarkerEvents);
        Assert.True(preferencesStore.Get().AlarmEvents, "Toggling one category must not affect another.");

        Assert.True(preferencesStore.Get().PlaySound);
        component.Find("[data-testid='notify-sound']").Change(false);
        Assert.False(preferencesStore.Get().PlaySound);
        Assert.False(preferencesStore.Get().MarkerEvents, "Toggling sound must not affect a category.");

        Assert.False(preferencesStore.Get().QuietHoursEnabled);
        Assert.DoesNotContain("notify-quiet-hours-start", component.Markup, StringComparison.Ordinal);
        component.Find("[data-testid='notify-quiet-hours-enabled']").Change(true);
        Assert.True(preferencesStore.Get().QuietHoursEnabled);

        component.Find("[data-testid='notify-quiet-hours-start']").Change("22:00");
        component.Find("[data-testid='notify-quiet-hours-end']").Change("07:00");
        Assert.Equal(new TimeOnly(22, 0), preferencesStore.Get().QuietHoursStart);
        Assert.Equal(new TimeOnly(7, 0), preferencesStore.Get().QuietHoursEnd);
    }

    [Fact]
    public void DiagnosticsPageShowsHealthChecksAndExportsToThePickedLocation()
    {
        var component = Render<Main>();
        component.WaitForElement(".map-page");
        component.FindAll("button.nav-item")
            .Single(button => button.TextContent.Contains("Diagnostics", StringComparison.Ordinal))
            .Click();
        component.WaitForElement("[data-testid='health-check-row']");

        Assert.Contains("Fake check", component.Find("[data-testid='health-check-row']").TextContent, StringComparison.Ordinal);

        var exportPath = Path.Combine(Path.GetTempPath(), $"rustplushelper-diagnostics-test-{Guid.NewGuid():N}.zip");
        _diagnosticsExportFilePicker.NextPath = exportPath;
        try
        {
            component.Find("[data-testid='export-diagnostics']").Click();
            component.WaitForAssertion(() =>
                Assert.Contains(exportPath, component.Find("[data-testid='export-result']").TextContent, StringComparison.Ordinal));
            Assert.True(File.Exists(exportPath));
        }
        finally
        {
            File.Delete(exportPath);
        }
    }

    [Fact]
    public void EventsPageExportsHistoryToThePickedLocation()
    {
        var component = Render<Main>();
        component.WaitForElement(".map-page");
        component.FindAll("button.nav-item")
            .Single(button => button.TextContent.Contains("Events", StringComparison.Ordinal))
            .Click();
        component.WaitForElement("[data-testid='export-events']");

        var exportPath = Path.Combine(Path.GetTempPath(), $"rustplushelper-events-test-{Guid.NewGuid():N}.csv");
        _eventExportFilePicker.NextPath = exportPath;
        try
        {
            component.Find("[data-testid='export-events']").Click();
            component.WaitForAssertion(() =>
                Assert.Contains(exportPath, component.Find("[data-testid='export-events-result']").TextContent, StringComparison.Ordinal));
            Assert.True(File.Exists(exportPath));
            Assert.StartsWith("OccurredAtUtc,Kind,Source,Title,Detail,WorldX,WorldY", File.ReadAllText(exportPath), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(exportPath);
        }
    }

    private sealed class FakeClientFactory : IRustPlusClientFactory
    {
        public List<FakeRustPlusClient> Clients { get; } = [];

        /// <summary>Test hook: the next created client reports a drone camera instead of the default
        /// fake PTZ camera. Set before starting a live session, not mid-session.</summary>
        public bool NextClientIsDrone { get; set; }

        public IRustPlusClient Create()
        {
            var client = new FakeRustPlusClient(NextClientIsDrone);
            Clients.Add(client);
            return client;
        }
    }

    private sealed class InMemoryStartupRegistration : IStartupRegistration
    {
        public bool IsEnabled { get; private set; }

        public void SetEnabled(bool enabled) => IsEnabled = enabled;
    }

    private sealed class RecordingDiagnosticsExportFilePicker : IDiagnosticsExportFilePicker
    {
        public string? NextPath { get; set; }

        public Task<string?> PickSaveLocationAsync(string suggestedFileName) => Task.FromResult(NextPath);
    }

    private sealed class RecordingEventExportFilePicker : IEventExportFilePicker
    {
        public string? NextPath { get; set; }

        public Task<string?> PickSaveLocationAsync(string suggestedFileName) => Task.FromResult(NextPath);
    }
}
