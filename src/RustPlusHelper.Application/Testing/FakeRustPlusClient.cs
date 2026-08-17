using RustPlusHelper.Application.RustPlus;

namespace RustPlusHelper.Application.Testing;

/// <summary>Deterministic source for UI and application development without a live Rust server.</summary>
public sealed class FakeRustPlusClient : IRustPlusClient
{
    private static readonly DateTimeOffset FixedUtc = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    public bool IsConnected { get; private set; }

    public Task ConnectAsync(RustPlusConnectionOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task<RustPlusResult<ServerInfoSnapshot>> GetServerInfoAsync(CancellationToken cancellationToken = default) =>
        ConnectedResult(new ServerInfoSnapshot(
            "Fake EU Main",
            null,
            "https://example.invalid",
            "Procedural Map",
            4500,
            FixedUtc.AddDays(-3),
            87,
            200,
            12,
            123456789,
            987654321,
            null,
            null,
            null,
            null), cancellationToken);

    public Task<RustPlusResult<ServerMapSnapshot>> GetMapAsync(CancellationToken cancellationToken = default) =>
        ConnectedResult(new ServerMapSnapshot(
            1000,
            1000,
            50,
            "#FF1C3440",
            [
                new MapMonumentSnapshot("launch_site_1", 800, 1200),
                new MapMonumentSnapshot("oilrig_1", 3900, 3600)
            ],
            Convert.FromBase64String(
                "/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////2wBDAf//////////////////////////////////////////////////////////////////////////////////////wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAX/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIQAxAAAAF//8QAFBABAAAAAAAAAAAAAAAAAAAAAP/aAAgBAQABBQJ//8QAFBEBAAAAAAAAAAAAAAAAAAAAAP/aAAgBAwEBPwF//8QAFBEBAAAAAAAAAAAAAAAAAAAAAP/aAAgBAgEBPwF//8QAFBABAAAAAAAAAAAAAAAAAAAAAP/aAAgBAQAGPwJ//8QAFBABAAAAAAAAAAAAAAAAAAAAAP/aAAgBAQABPyF//9oADAMBAAIAAwAAABAf/8QAFBEBAAAAAAAAAAAAAAAAAAAAAP/aAAgBAwEBPxB//8QAFBEBAAAAAAAAAAAAAAAAAAAAAP/aAAgBAgEBPxB//8QAFBABAAAAAAAAAAAAAAAAAAAAAP/aAAgBAQABPxB//9k=")), cancellationToken);

    public Task<RustPlusResult<TeamSnapshot>> GetTeamAsync(CancellationToken cancellationToken = default) =>
        ConnectedResult(new TeamSnapshot(
            ulong.MaxValue - 42,
            [
                new TeamMemberSnapshot(
                    ulong.MaxValue - 42,
                    "Kakec",
                    1250.5f,
                    2250.25f,
                    true,
                    true,
                    FixedUtc.AddMinutes(-25),
                    FixedUtc.AddHours(-2)),
                new TeamMemberSnapshot(
                    76561198000000001,
                    "Marko",
                    1675,
                    2100,
                    false,
                    false,
                    FixedUtc.AddHours(-4),
                    FixedUtc.AddMinutes(-8))
            ],
            [new TeamNoteSnapshot(1400, 2200, "Meet here", 1, 2)],
            [],
            new MapPositionSnapshot(1675, 2100)), cancellationToken);

    public Task<RustPlusResult<TeamChatSnapshot>> GetTeamChatAsync(CancellationToken cancellationToken = default) =>
        ConnectedResult(new TeamChatSnapshot([
            new TeamChatMessageSnapshot(
                ulong.MaxValue - 42,
                "Kakec",
                "Fake message: heading to launch site",
                "#FFFFFFFF",
                FixedUtc.AddMinutes(-5))
        ]), cancellationToken);

    public Task<RustPlusResult<MapMarkersSnapshot>> GetMapMarkersAsync(CancellationToken cancellationToken = default) =>
        ConnectedResult(new MapMarkersSnapshot([
            new MapMarkerSnapshot(1, MapMarkerKind.CargoShip, 4000, 3500, Rotation: 90),
            new MapMarkerSnapshot(
                2,
                MapMarkerKind.VendingMachine,
                1350,
                2300,
                Name: "Fake Weapons Shop",
                IsOutOfStock: false,
                VendingOrders: [new VendingOrderSnapshot(-904863145, 1, -932201673, 85, 3, false, false, 1, 1, 1, 1)]),
            new MapMarkerSnapshot(ulong.MaxValue - 7, MapMarkerKind.Unknown, 100, 200, RawType: 777)
        ]), cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }

    private Task<RustPlusResult<T>> ConnectedResult<T>(T value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(IsConnected
            ? RustPlusResult<T>.Success(value)
            : RustPlusResult<T>.Failure("not_connected", "The fake Rust+ client is not connected."));
    }
}
