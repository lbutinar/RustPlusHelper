using RustPlusHelper.Application.Map;
using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Application.Testing;

namespace RustPlusHelper.Tests;

public sealed class MapDashboardServiceTests
{
    [Fact]
    public async Task LoadsMapFirstStateThroughOwnedClientBoundary()
    {
        await using var client = new FakeRustPlusClient();
        await using var service = new MapDashboardService(
            client,
            new RustPlusConnectionOptions("fake.invalid", 28082, 1, 2));

        await service.InitializeAsync();

        Assert.Equal(DashboardConnectionState.Ready, service.Current.ConnectionState);
        Assert.Equal("Fake EU Main", service.Current.Server?.Name);
        Assert.Equal(2, service.Current.Team?.Members.Count);
        Assert.Equal(3, service.Current.Markers?.Markers.Count);
    }

    [Fact]
    public async Task BuildsProjectedLayeredRenderModel()
    {
        await using var client = new FakeRustPlusClient();
        await using var service = new MapDashboardService(
            client,
            new RustPlusConnectionOptions("fake.invalid", 28082, 1, 2));
        await service.InitializeAsync();

        var model = MapRenderModelFactory.Create(service.Current);

        Assert.NotNull(model);
        Assert.Equal(1000, model.Width);
        Assert.Equal(8, model.Items.Count);
        Assert.Contains(model.Items, item => item.Kind == "vending");
        Assert.Contains(model.Items, item => item.Kind == "unknown");
        Assert.True(model.LayerVisibility["team"]);
    }

    [Fact]
    public async Task UnavailableLayersCannotBeEnabled()
    {
        await using var client = new FakeRustPlusClient();
        await using var service = new MapDashboardService(
            client,
            new RustPlusConnectionOptions("fake.invalid", 28082, 1, 2));
        await service.InitializeAsync();

        service.SetLayerVisibility(MapLayerKind.Cameras, true);
        service.SetLayerVisibility(MapLayerKind.Team, false);

        Assert.False(service.Current.Layers.Single(layer => layer.Kind == MapLayerKind.Cameras).IsVisible);
        Assert.False(service.Current.Layers.Single(layer => layer.Kind == MapLayerKind.Team).IsVisible);
    }
}
