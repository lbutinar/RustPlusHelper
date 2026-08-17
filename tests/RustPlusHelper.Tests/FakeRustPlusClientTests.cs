using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Application.Testing;

namespace RustPlusHelper.Tests;

public sealed class FakeRustPlusClientTests
{
    [Fact]
    public async Task ReturnsDeterministicDataAndPreservesUnsignedIdentifiers()
    {
        await using var client = new FakeRustPlusClient();
        await client.ConnectAsync(new RustPlusConnectionOptions("fake.invalid", 28082, 1, 2));

        var team = await client.GetTeamAsync();
        var markers = await client.GetMapMarkersAsync();

        Assert.True(team.IsSuccess);
        Assert.NotNull(team.Data);
        Assert.True(team.Data.LeaderSteamId > long.MaxValue);
        Assert.Contains(markers.Data!.Markers, marker => marker.Id > (ulong)long.MaxValue);
    }

    [Fact]
    public async Task DisconnectMakesSubsequentRequestsFailCleanly()
    {
        await using var client = new FakeRustPlusClient();
        await client.ConnectAsync(new RustPlusConnectionOptions("fake.invalid", 28082, 1, 2));

        await client.DisconnectAsync();
        var result = await client.GetServerInfoAsync();

        Assert.False(client.IsConnected);
        Assert.False(result.IsSuccess);
        Assert.Equal("not_connected", result.Error?.Code);
    }
}
