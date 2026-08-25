using RustPlusHelper.Application.RustPlus;

namespace RustPlusHelper.Application.Testing;

/// <summary>Deterministic source for UI and application development without a live Rust server.</summary>
public sealed class FakeRustPlusClient : IRustPlusClient, IDisposable
{
    private static readonly DateTimeOffset FixedUtc = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    // A tiny deterministic 160x90 solid-color PNG (the app's existing dark panel background),
    // matching the fake map image's "deterministic development data" convention.
    private static readonly byte[] FakeCameraPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAKAAAABaCAIAAACwpMoFAAAA60lEQVR4nO3RUQkAIBTAwBfDb8H+FU0hwji4AIPNOpuw"
        + "+V7AUwbHGRxncJzBcQbHGRxncJzBcQbHGRxncJzBcQbHGRxncJzBcQbHGRxncJzBcQbHGRxncJzBcQbHGRxncJzBcQbH"
        + "GRxncJzBcQbHGRxncJzBcQbHGRxncJzBcQbHGRxncJzBcQbHGRxncJzBcQbHGRxncJzBcQbHGRxncJzBcQbHGRxncJzB"
        + "cQbHGRxncJzBcQbHGRxncJzBcQbHGRxncJzBcQbHGRxncJzBcQbHGRxncJzBcQbHGRxncJzBcQbHGRxncJzBcRcjCuDt"
        + "LJZNjgAAAABJRU5ErkJggg==");

    private readonly bool _cameraIsDrone;
    private bool _cameraSubscribed;
    private bool _fakeSwitchValue = true;

    /// <param name="cameraIsDrone">When true, the fake camera reports itself as a drone (move
    /// succeeds, zoom/shoot/reload are refused) instead of the default fake PTZ camera (zoom/look
    /// succeed, shoot/reload/move are refused) — lets held-key move behavior be tested without
    /// changing every other test's default fixture.</param>
    public FakeRustPlusClient(bool cameraIsDrone = false)
    {
        _cameraIsDrone = cameraIsDrone;
    }
    private readonly List<TeamChatMessageSnapshot> _fakeChatMessages =
    [
        new TeamChatMessageSnapshot(
            ulong.MaxValue - 42,
            "Kakec",
            "Fake message: heading to launch site",
            "#FFFFFFFF",
            FixedUtc.AddMinutes(-5))
    ];

    private readonly List<ClanChatMessageSnapshot> _fakeClanMessages =
    [
        new ClanChatMessageSnapshot(
            76561198000000001,
            "Marko",
            "Fake clan message: don't forget to recycle before the wipe",
            FixedUtc.AddMinutes(-20))
    ];

    public bool IsConnected { get; private set; }

    public event EventHandler<CameraFrameSnapshot>? CameraFrameReceived;

    public event EventHandler<EntityStateChangedSnapshot>? EntityStateChanged;

    // The deterministic fake client has no keep-alive concept to fail, so this never raises.
#pragma warning disable CS0067
    public event EventHandler<RustPlusError>? CameraSubscriptionFailed;
#pragma warning restore CS0067

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
        _cameraSubscribed = false;
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
        ConnectedResult(new TeamChatSnapshot(_fakeChatMessages.ToArray()), cancellationToken);

    public Task<RustPlusResult<TeamChatMessageSnapshot>> SendTeamMessageAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsConnected)
        {
            return Task.FromResult(
                RustPlusResult<TeamChatMessageSnapshot>.Failure("not_connected", "The fake Rust+ client is not connected."));
        }

        var sent = new TeamChatMessageSnapshot(ulong.MaxValue - 42, "Kakec", message, "#FFFFFFFF", DateTimeOffset.UtcNow);
        _fakeChatMessages.Add(sent);
        return Task.FromResult(RustPlusResult<TeamChatMessageSnapshot>.Success(sent));
    }

    public Task<RustPlusResult<ClanChatSnapshot>> GetClanChatAsync(CancellationToken cancellationToken = default) =>
        ConnectedResult(new ClanChatSnapshot(_fakeClanMessages.ToArray()), cancellationToken);

    public Task<RustPlusResult<bool>> SendClanMessageAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsConnected)
        {
            return Task.FromResult(RustPlusResult<bool>.Failure("not_connected", "The fake Rust+ client is not connected."));
        }

        _fakeClanMessages.Add(new ClanChatMessageSnapshot(ulong.MaxValue - 42, "Kakec", message, DateTimeOffset.UtcNow));
        return Task.FromResult(RustPlusResult<bool>.Success(true));
    }

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

    public Task<RustPlusResult<CameraInfoSnapshot>> SubscribeToCameraAsync(
        string cameraId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cameraId);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsConnected)
        {
            return Task.FromResult(
                RustPlusResult<CameraInfoSnapshot>.Failure("not_connected", "The fake Rust+ client is not connected."));
        }

        // A fake PTZ camera (the default): zoom/look succeed, shoot/reload/move are refused, exactly
        // like a real PTZ camera would refuse turret/drone-only actions. When constructed with
        // cameraIsDrone: true, this instead reports a fake drone: move succeeds, zoom/shoot/reload
        // are refused.
        _cameraSubscribed = true;
        CameraFrameReceived?.Invoke(this, new CameraFrameSnapshot(FakeCameraPng, VerticalFov: 65f, DateTimeOffset.UtcNow));
        return Task.FromResult(RustPlusResult<CameraInfoSnapshot>.Success(_cameraIsDrone
            ? new CameraInfoSnapshot(160, 90, 0.1f, 200f, IsStaticCamera: false, IsPtzCamera: false, IsAutoTurret: false, IsDrone: true)
            : new CameraInfoSnapshot(160, 90, 0.1f, 200f, IsStaticCamera: false, IsPtzCamera: true, IsAutoTurret: false, IsDrone: false)));
    }

    public Task<RustPlusResult<bool>> ZoomCameraAsync(CancellationToken cancellationToken = default) =>
        FakeCameraCommandAsync();

    public Task<RustPlusResult<bool>> ShootCameraAsync(CancellationToken cancellationToken = default) =>
        FakeCameraRefusedAsync("shoot is an auto-turret action; the fake camera is a PTZ camera");

    public Task<RustPlusResult<bool>> ReloadCameraAsync(CancellationToken cancellationToken = default) =>
        FakeCameraRefusedAsync("reload is an auto-turret action; the fake camera is a PTZ camera");

    public List<(float DeltaX, float DeltaY)> LookCalls { get; } = [];

    public Task<RustPlusResult<bool>> LookCameraAsync(
        float deltaX,
        float deltaY,
        CancellationToken cancellationToken = default)
    {
        LookCalls.Add((deltaX, deltaY));
        return FakeCameraCommandAsync();
    }

    public List<CameraMoveDirection> MoveCalls { get; } = [];

    public Task<RustPlusResult<bool>> MoveCameraAsync(
        CameraMoveDirection direction,
        CancellationToken cancellationToken = default)
    {
        MoveCalls.Add(direction);
        return _cameraIsDrone
            ? FakeCameraCommandAsync()
            : FakeCameraRefusedAsync("movement is a drone action; the fake camera is a PTZ camera");
    }

    public Task UnsubscribeFromCameraAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _cameraSubscribed = false;
        return Task.CompletedTask;
    }

    public Task<RustPlusResult<SmartDeviceStateSnapshot>> GetSmartSwitchInfoAsync(
        ulong entityId,
        CancellationToken cancellationToken = default) =>
        ConnectedResult(new SmartDeviceStateSnapshot(entityId, _fakeSwitchValue), cancellationToken);

    public Task<RustPlusResult<SmartDeviceStateSnapshot>> GetAlarmInfoAsync(
        ulong entityId,
        CancellationToken cancellationToken = default) =>
        ConnectedResult(new SmartDeviceStateSnapshot(entityId, _fakeSwitchValue), cancellationToken);

    public Task<RustPlusResult<StorageMonitorStateSnapshot>> GetStorageMonitorInfoAsync(
        ulong entityId,
        CancellationToken cancellationToken = default) =>
        ConnectedResult(new StorageMonitorStateSnapshot(
            entityId,
            Capacity: 24,
            HasProtection: true,
            Items: [new StorageItemSnapshot(-932201673, 400, false)]), cancellationToken); // Scrap

    public Task<RustPlusResult<SmartDeviceStateSnapshot>> SetSmartSwitchValueAsync(
        ulong entityId,
        bool value,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsConnected)
        {
            return Task.FromResult(RustPlusResult<SmartDeviceStateSnapshot>.Failure(
                "not_connected", "The fake Rust+ client is not connected."));
        }

        _fakeSwitchValue = value;
        EntityStateChanged?.Invoke(this, new EntityStateChangedSnapshot(entityId, _fakeSwitchValue, null, null, []));
        return Task.FromResult(
            RustPlusResult<SmartDeviceStateSnapshot>.Success(new SmartDeviceStateSnapshot(entityId, _fakeSwitchValue)));
    }

    public Task<RustPlusResult<SmartDeviceStateSnapshot>> ToggleSmartSwitchAsync(
        ulong entityId,
        CancellationToken cancellationToken = default) =>
        SetSmartSwitchValueAsync(entityId, !_fakeSwitchValue, cancellationToken);

    public Task<RustPlusResult<SmartDeviceStateSnapshot>> StrobeSmartSwitchAsync(
        ulong entityId,
        TimeSpan duration,
        bool value,
        CancellationToken cancellationToken = default) =>
        SetSmartSwitchValueAsync(entityId, value, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        IsConnected = false;
        _cameraSubscribed = false;
    }

    private Task<RustPlusResult<bool>> FakeCameraCommandAsync() =>
        Task.FromResult(_cameraSubscribed
            ? RustPlusResult<bool>.Success(true)
            : RustPlusResult<bool>.Failure("no_active_camera", "No camera subscription is active."));

    private Task<RustPlusResult<bool>> FakeCameraRefusedAsync(string reason) =>
        Task.FromResult(_cameraSubscribed
            ? RustPlusResult<bool>.Failure("not_supported", reason)
            : RustPlusResult<bool>.Failure("no_active_camera", "No camera subscription is active."));

    private Task<RustPlusResult<T>> ConnectedResult<T>(T value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(IsConnected
            ? RustPlusResult<T>.Success(value)
            : RustPlusResult<T>.Failure("not_connected", "The fake Rust+ client is not connected."));
    }
}
