using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using RustPlusHelper.Application.Map;
using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Application.Testing;
using RustPlusHelper.Desktop;

namespace RustPlusHelper.Desktop.Tests;

public sealed class MainComponentTests : BunitContext
{
    private readonly MapDashboardService _dashboard;

    public MainComponentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var client = new FakeRustPlusClient();
        var connection = new RustPlusConnectionOptions("fake.invalid", 28082, 1, 2);
        _dashboard = new MapDashboardService(client, connection);

        // Externally constructed instances are owned by this short-lived test process. Registering
        // the async-only service as an instance keeps bUnit's synchronous container cleanup from
        // attempting an invalid synchronous dispose.
        Services.AddSingleton<IRustPlusClient>(client);
        Services.AddSingleton(connection);
        Services.AddSingleton(_dashboard);
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
}
