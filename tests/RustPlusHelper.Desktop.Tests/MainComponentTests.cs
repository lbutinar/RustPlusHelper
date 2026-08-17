using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using RustPlusHelper.Application.Map;
using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Application.Servers;
using RustPlusHelper.Application.Testing;
using RustPlusHelper.Desktop;

namespace RustPlusHelper.Desktop.Tests;

public sealed class MainComponentTests : BunitContext
{
    private readonly MapDashboardService _dashboard;
    private readonly InMemoryServerRepository _serverRepository;

    public MainComponentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var client = new FakeRustPlusClient();
        var connection = new RustPlusConnectionOptions("fake.invalid", 28082, 1, 2);
        _dashboard = new MapDashboardService(client, connection);
        _serverRepository = new InMemoryServerRepository();
        var serverManager = new ServerManager(_serverRepository, TimeProvider.System);

        // Externally constructed instances are owned by this short-lived test process and keep the
        // component test independent from the Windows desktop composition root.
        Services.AddSingleton<IRustPlusClient>(client);
        Services.AddSingleton(connection);
        Services.AddSingleton(_dashboard);
        Services.AddSingleton(serverManager);
    }

    [Fact]
    public void OpensOnMapWithTruthfulLayerControls()
    {
        var component = Render<Main>();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Server map", component.Markup, StringComparison.Ordinal);
            Assert.Contains("FAKE DATA", component.Markup, StringComparison.Ordinal);
            Assert.Contains("Smart devices", component.Markup, StringComparison.Ordinal);
            Assert.Contains("Rust+ does not provide device positions", component.Markup, StringComparison.Ordinal);
            Assert.Single(JSInterop.Invocations, invocation => invocation.Identifier == "rustPlusMap.render");
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
        component.Find("[data-testid='save-server']").Click();

        component.WaitForAssertion(() =>
        {
            var saved = Assert.Single(_serverRepository.GetAll());
            Assert.Equal("EU Medium", saved.DisplayName);
            Assert.Equal("companion.example.invalid", saved.Host);
            Assert.Equal(28100, saved.Port);
            Assert.Contains("SQLITE READY", component.Markup, StringComparison.Ordinal);
        });
    }
}
