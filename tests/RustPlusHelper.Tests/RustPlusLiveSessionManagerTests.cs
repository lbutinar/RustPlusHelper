using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Application.Servers;
using RustPlusHelper.Application.Testing;

namespace RustPlusHelper.Tests;

public sealed class RustPlusLiveSessionManagerTests
{
    [Fact]
    public async Task ReusesOneConnectionPollsWithoutMapAndDerivesTransitions()
    {
        using var secrets = new InMemorySecretStore();
        var servers = CreatePairedServer(secrets, out var profile);
        var factory = new ScriptedFactory(index => new ScriptedClient(
            [
                Team(online: true, alive: true),
                Team(online: false, alive: false),
                Team(online: true, alive: true)
            ],
            [Markers(1), Markers(2), Markers(2)]));
        await using var manager = CreateManager(servers, secrets, factory);

        await manager.StartAsync(profile.Id);
        await WaitUntilAsync(() => manager.Current.Events.Any(item =>
            item.Kind == CompanionEventKind.TeamMemberRespawned));

        Assert.Equal(RustPlusLiveSessionStatus.Connected, manager.Current.Status);
        Assert.Equal(1, factory.CreateCount);
        Assert.Equal(0, factory.Clients.Sum(client => client.MapCallCount));
        Assert.Contains(manager.Current.Events, item => item.Kind == CompanionEventKind.TeamMemberDisconnected);
        Assert.Contains(manager.Current.Events, item => item.Kind == CompanionEventKind.TeamMemberDied);
        var death = Assert.Single(manager.Current.Events, item => item.Kind == CompanionEventKind.TeamMemberDied);
        Assert.Equal(new MapPositionSnapshot(100, 200), death.Position);
        Assert.Equal(CompanionEventSource.SnapshotDiff, death.Source);
        Assert.Contains(manager.Current.Events, item => item.Kind == CompanionEventKind.TeamMemberRespawned);
        Assert.Contains(manager.Current.Events, item => item.Kind == CompanionEventKind.MarkerAppeared);
        Assert.Contains(manager.Current.Events, item => item.Kind == CompanionEventKind.MarkerDisappeared);
    }

    [Fact]
    public async Task ReconnectsWithBackoffAndEmitsTransportLifecycle()
    {
        using var secrets = new InMemorySecretStore();
        var servers = CreatePairedServer(secrets, out var profile);
        var factory = new ScriptedFactory(index => new ScriptedClient(
            [Team(online: true, alive: true)],
            [Markers(1)],
            disconnectAfterFirstTeam: index == 0));
        await using var manager = CreateManager(servers, secrets, factory);

        await manager.StartAsync(profile.Id);
        await WaitUntilAsync(() => manager.Current.Events.Any(item =>
            item.Kind == CompanionEventKind.ConnectionRestored));

        Assert.True(factory.CreateCount >= 2);
        Assert.Contains(manager.Current.Events, item => item.Kind == CompanionEventKind.ConnectionLost);
        Assert.Contains(manager.Current.Events, item => item.Kind == CompanionEventKind.ConnectionRestored);
        Assert.Equal(RustPlusLiveSessionStatus.Connected, manager.Current.Status);
    }

    [Fact]
    public async Task EmitsGridCrossingOncePerMemberWithinCooldown()
    {
        using var secrets = new InMemorySecretStore();
        var servers = CreatePairedServer(secrets, out var profile);
        var factory = new ScriptedFactory(_ => new ScriptedClient(
            [
                Team(online: true, alive: true, x: 100),
                Team(online: true, alive: true, x: 200),
                Team(online: true, alive: true, x: 400)
            ],
            [Markers(1)]));
        await using var manager = CreateManager(servers, secrets, factory);

        await manager.StartAsync(profile.Id);
        await WaitUntilAsync(() => manager.Current.Events.Any(item =>
            item.Kind == CompanionEventKind.TeamMemberChangedGrid));
        await WaitUntilAsync(() => factory.Clients[0].TeamCallCount >= 3);

        var movement = Assert.Single(
            manager.Current.Events,
            item => item.Kind == CompanionEventKind.TeamMemberChangedGrid);
        Assert.Equal("Test teammate entered B28", movement.Title);
        Assert.Equal("Moved from A28 to B28.", movement.Detail);
    }

    [Fact]
    public async Task EmitsVendingPriceChangedWhenOfferCostChangesAndNothingElse()
    {
        using var secrets = new InMemorySecretStore();
        var servers = CreatePairedServer(secrets, out var profile);
        var factory = new ScriptedFactory(_ => new ScriptedClient(
            [Team(online: true, alive: true)],
            [
                VendingMarkers(1, Offer(RifleSemiAuto, Scrap, cost: 100, stock: 5)),
                VendingMarkers(1, Offer(RifleSemiAuto, Scrap, cost: 150, stock: 5))
            ]));
        await using var manager = CreateManager(servers, secrets, factory);

        await manager.StartAsync(profile.Id);
        await WaitUntilAsync(() => manager.Current.Events.Any(item => item.Kind == CompanionEventKind.VendingPriceChanged));

        var changed = Assert.Single(manager.Current.Events, item => item.Kind == CompanionEventKind.VendingPriceChanged);
        Assert.Equal(CompanionEventSource.SnapshotDiff, changed.Source);
        Assert.Equal("Semi-Automatic Rifle price changed at Test Shop", changed.Title);
        Assert.Equal("100 → 150 Scrap", changed.Detail);
        Assert.DoesNotContain(manager.Current.Events, item => item.Kind == CompanionEventKind.VendingStockChanged);
        Assert.DoesNotContain(manager.Current.Events, item => item.Kind == CompanionEventKind.VendingOfferAdded);
        Assert.DoesNotContain(manager.Current.Events, item => item.Kind == CompanionEventKind.VendingOfferRemoved);
    }

    [Fact]
    public async Task EmitsVendingStockChangedWhenOfferStockChangesAndNothingElse()
    {
        using var secrets = new InMemorySecretStore();
        var servers = CreatePairedServer(secrets, out var profile);
        var factory = new ScriptedFactory(_ => new ScriptedClient(
            [Team(online: true, alive: true)],
            [
                VendingMarkers(1, Offer(RifleSemiAuto, Scrap, cost: 100, stock: 5)),
                VendingMarkers(1, Offer(RifleSemiAuto, Scrap, cost: 100, stock: 2))
            ]));
        await using var manager = CreateManager(servers, secrets, factory);

        await manager.StartAsync(profile.Id);
        await WaitUntilAsync(() => manager.Current.Events.Any(item => item.Kind == CompanionEventKind.VendingStockChanged));

        var changed = Assert.Single(manager.Current.Events, item => item.Kind == CompanionEventKind.VendingStockChanged);
        Assert.Equal("Semi-Automatic Rifle stock changed at Test Shop", changed.Title);
        Assert.Equal("5 → 2 in stock", changed.Detail);
        Assert.DoesNotContain(manager.Current.Events, item => item.Kind == CompanionEventKind.VendingPriceChanged);
    }

    [Fact]
    public async Task EmitsVendingOfferAddedForANewSlotSignatureOnAnExistingMarker()
    {
        using var secrets = new InMemorySecretStore();
        var servers = CreatePairedServer(secrets, out var profile);
        var factory = new ScriptedFactory(_ => new ScriptedClient(
            [Team(online: true, alive: true)],
            [
                VendingMarkers(1, Offer(RifleSemiAuto, Scrap, cost: 100, stock: 5)),
                VendingMarkers(
                    1,
                    Offer(RifleSemiAuto, Scrap, cost: 100, stock: 5),
                    Offer(Wood, Scrap, cost: 10, stock: 20))
            ]));
        await using var manager = CreateManager(servers, secrets, factory);

        await manager.StartAsync(profile.Id);
        await WaitUntilAsync(() => manager.Current.Events.Any(item => item.Kind == CompanionEventKind.VendingOfferAdded));

        var added = Assert.Single(manager.Current.Events, item => item.Kind == CompanionEventKind.VendingOfferAdded);
        Assert.Equal("New offer at Test Shop", added.Title);
        Assert.Equal("Wood for 10 Scrap", added.Detail);
        Assert.DoesNotContain(manager.Current.Events, item => item.Kind == CompanionEventKind.VendingPriceChanged);
        Assert.DoesNotContain(manager.Current.Events, item => item.Kind == CompanionEventKind.VendingStockChanged);
    }

    [Fact]
    public async Task EmitsVendingOfferRemovedForAMissingSlotSignatureOnAnExistingMarker()
    {
        using var secrets = new InMemorySecretStore();
        var servers = CreatePairedServer(secrets, out var profile);
        var factory = new ScriptedFactory(_ => new ScriptedClient(
            [Team(online: true, alive: true)],
            [
                VendingMarkers(
                    1,
                    Offer(RifleSemiAuto, Scrap, cost: 100, stock: 5),
                    Offer(Wood, Scrap, cost: 10, stock: 20)),
                VendingMarkers(1, Offer(RifleSemiAuto, Scrap, cost: 100, stock: 5))
            ]));
        await using var manager = CreateManager(servers, secrets, factory);

        await manager.StartAsync(profile.Id);
        await WaitUntilAsync(() => manager.Current.Events.Any(item => item.Kind == CompanionEventKind.VendingOfferRemoved));

        var removed = Assert.Single(manager.Current.Events, item => item.Kind == CompanionEventKind.VendingOfferRemoved);
        Assert.Equal("Offer removed at Test Shop", removed.Title);
        Assert.Equal("Wood for 10 Scrap", removed.Detail);
    }

    [Fact]
    public async Task DoesNotEmitOfferLevelEventsForABrandNewVendingMarker()
    {
        using var secrets = new InMemorySecretStore();
        var servers = CreatePairedServer(secrets, out var profile);
        var factory = new ScriptedFactory(_ => new ScriptedClient(
            [Team(online: true, alive: true)],
            [
                new MapMarkersSnapshot([]),
                VendingMarkers(1, Offer(RifleSemiAuto, Scrap, cost: 100, stock: 5))
            ]));
        await using var manager = CreateManager(servers, secrets, factory);

        await manager.StartAsync(profile.Id);
        await WaitUntilAsync(() => manager.Current.Events.Any(item => item.Kind == CompanionEventKind.MarkerAppeared));

        Assert.DoesNotContain(manager.Current.Events, item => item.Kind == CompanionEventKind.VendingOfferAdded);
        Assert.DoesNotContain(manager.Current.Events, item => item.Kind == CompanionEventKind.VendingPriceChanged);
        Assert.DoesNotContain(manager.Current.Events, item => item.Kind == CompanionEventKind.VendingStockChanged);
        Assert.DoesNotContain(manager.Current.Events, item => item.Kind == CompanionEventKind.VendingOfferRemoved);
    }

    [Fact]
    public async Task ViewCameraAsyncFailsWhenNotConnected()
    {
        using var secrets = new InMemorySecretStore();
        var servers = CreatePairedServer(secrets, out var profile);
        var factory = new ScriptedFactory(_ => new ScriptedClient([Team(online: true, alive: true)], [Markers(1)]));
        await using var manager = CreateManager(servers, secrets, factory);

        var result = await manager.ViewCameraAsync("CAM01");

        Assert.False(result.IsSuccess);
        Assert.Equal("not_connected", result.Error?.Code);
        Assert.Equal(CameraSessionStatus.Inactive, manager.CurrentCamera.Status);
    }

    [Fact]
    public async Task ViewCameraAsyncSubscribesAndUpdatesCameraState()
    {
        using var secrets = new InMemorySecretStore();
        var servers = CreatePairedServer(secrets, out var profile);
        var cameraInfo = new CameraInfoSnapshot(160, 90, 0.1f, 200f, false, true, false, false);
        var factory = new ScriptedFactory(_ => new ScriptedClient(
            [Team(online: true, alive: true)],
            [Markers(1)],
            cameraInfo: cameraInfo));
        await using var manager = CreateManager(servers, secrets, factory);
        await manager.StartAsync(profile.Id);
        await WaitUntilAsync(() => manager.Current.Status == RustPlusLiveSessionStatus.Connected);

        var result = await manager.ViewCameraAsync("CAM01");

        Assert.True(result.IsSuccess);
        Assert.Equal(cameraInfo, result.Data);
        Assert.Equal(CameraSessionStatus.Active, manager.CurrentCamera.Status);
        Assert.Equal("CAM01", manager.CurrentCamera.CameraCode);
        Assert.Equal(cameraInfo, manager.CurrentCamera.Info);
        Assert.Equal(1, factory.Clients[0].CameraSubscribeCallCount);
        Assert.Equal("CAM01", factory.Clients[0].LastSubscribedCameraId);
    }

    [Fact]
    public async Task ViewCameraAsyncPreservesAFrameThatArrivesWhileSubscribingInFlight()
    {
        using var secrets = new InMemorySecretStore();
        var servers = CreatePairedServer(secrets, out var profile);
        var cameraInfo = new CameraInfoSnapshot(160, 90, 0.1f, 200f, false, true, false, false);
        var raceFrame = new CameraFrameSnapshot([9], 65f, DateTimeOffset.UtcNow);
        var factory = new ScriptedFactory(_ => new ScriptedClient(
            [Team(online: true, alive: true)],
            [Markers(1)],
            cameraInfo: cameraInfo,
            frameDuringSubscribe: raceFrame));
        await using var manager = CreateManager(servers, secrets, factory);
        await manager.StartAsync(profile.Id);
        await WaitUntilAsync(() => manager.Current.Status == RustPlusLiveSessionStatus.Connected);

        await manager.ViewCameraAsync("CAM01");

        Assert.Equal(CameraSessionStatus.Active, manager.CurrentCamera.Status);
        Assert.Same(raceFrame, manager.CurrentCamera.LatestFrame);
    }

    [Fact]
    public async Task ViewCameraAsyncFailsWhenNoCameraIsConfigured()
    {
        using var secrets = new InMemorySecretStore();
        var servers = CreatePairedServer(secrets, out var profile);
        var factory = new ScriptedFactory(_ => new ScriptedClient([Team(online: true, alive: true)], [Markers(1)]));
        await using var manager = CreateManager(servers, secrets, factory);
        await manager.StartAsync(profile.Id);
        await WaitUntilAsync(() => manager.Current.Status == RustPlusLiveSessionStatus.Connected);

        var result = await manager.ViewCameraAsync("UNKNOWN");

        Assert.False(result.IsSuccess);
        Assert.Equal(CameraSessionStatus.Failed, manager.CurrentCamera.Status);
    }

    [Fact]
    public async Task CameraFrameEventsThrottleHowOftenLatestFrameUpdates()
    {
        using var secrets = new InMemorySecretStore();
        var servers = CreatePairedServer(secrets, out var profile);
        var cameraInfo = new CameraInfoSnapshot(160, 90, 0.1f, 200f, false, true, false, false);
        var factory = new ScriptedFactory(_ => new ScriptedClient(
            [Team(online: true, alive: true)],
            [Markers(1)],
            cameraInfo: cameraInfo));
        await using var manager = CreateManager(servers, secrets, factory);
        await manager.StartAsync(profile.Id);
        await WaitUntilAsync(() => manager.Current.Status == RustPlusLiveSessionStatus.Connected);
        await manager.ViewCameraAsync("CAM01");
        var client = factory.Clients[0];

        var frameA = new CameraFrameSnapshot([1], 65f, DateTimeOffset.UtcNow);
        client.RaiseCameraFrame(frameA);
        Assert.Same(frameA, manager.CurrentCamera.LatestFrame);

        var frameB = new CameraFrameSnapshot([2], 65f, DateTimeOffset.UtcNow);
        client.RaiseCameraFrame(frameB);
        Assert.Same(frameA, manager.CurrentCamera.LatestFrame); // still within the throttle window

        await Task.Delay(TimeSpan.FromMilliseconds(250));
        var frameC = new CameraFrameSnapshot([3], 65f, DateTimeOffset.UtcNow);
        client.RaiseCameraFrame(frameC);
        Assert.Same(frameC, manager.CurrentCamera.LatestFrame);
    }

    [Fact]
    public async Task StopViewingCameraAsyncResetsCameraState()
    {
        using var secrets = new InMemorySecretStore();
        var servers = CreatePairedServer(secrets, out var profile);
        var cameraInfo = new CameraInfoSnapshot(160, 90, 0.1f, 200f, false, true, false, false);
        var factory = new ScriptedFactory(_ => new ScriptedClient(
            [Team(online: true, alive: true)],
            [Markers(1)],
            cameraInfo: cameraInfo));
        await using var manager = CreateManager(servers, secrets, factory);
        await manager.StartAsync(profile.Id);
        await WaitUntilAsync(() => manager.Current.Status == RustPlusLiveSessionStatus.Connected);
        await manager.ViewCameraAsync("CAM01");
        Assert.Equal(CameraSessionStatus.Active, manager.CurrentCamera.Status);

        await manager.StopViewingCameraAsync();

        Assert.Equal(CameraSessionState.Inactive, manager.CurrentCamera);
    }

    [Fact]
    public async Task CameraSubscriptionFailedEventMarksCameraStateFailed()
    {
        using var secrets = new InMemorySecretStore();
        var servers = CreatePairedServer(secrets, out var profile);
        var cameraInfo = new CameraInfoSnapshot(160, 90, 0.1f, 200f, false, true, false, false);
        var factory = new ScriptedFactory(_ => new ScriptedClient(
            [Team(online: true, alive: true)],
            [Markers(1)],
            cameraInfo: cameraInfo));
        await using var manager = CreateManager(servers, secrets, factory);
        await manager.StartAsync(profile.Id);
        await WaitUntilAsync(() => manager.Current.Status == RustPlusLiveSessionStatus.Connected);
        await manager.ViewCameraAsync("CAM01");
        var client = factory.Clients[0];

        client.RaiseCameraSubscriptionFailed(new RustPlusError("no_player", "Camera entity destroyed."));

        Assert.Equal(CameraSessionStatus.Failed, manager.CurrentCamera.Status);
        Assert.Equal("Camera entity destroyed.", manager.CurrentCamera.Error);
    }

    private static RustPlusLiveSessionManager CreateManager(
        ServerManager servers,
        InMemorySecretStore secrets,
        IRustPlusClientFactory factory) =>
        new(
            new RustPlusSavedConnectionResolver(servers, secrets),
            factory,
            TimeProvider.System,
            new RustPlusPollingOptions(
                TimeSpan.FromMilliseconds(15),
                TimeSpan.FromMilliseconds(20),
                TimeSpan.FromMilliseconds(15),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(1),
                [TimeSpan.FromMilliseconds(5)]),
            new InMemoryCompanionEventRepository());

    [Fact]
    public async Task ReloadsPersistedEventsWhenMonitoringRestarts()
    {
        using var secrets = new InMemorySecretStore();
        var servers = CreatePairedServer(secrets, out var profile);
        var history = new InMemoryCompanionEventRepository();
        var persisted = new CompanionEvent(
            Guid.NewGuid(),
            profile.Id,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            CompanionEventKind.MarkerAppeared,
            CompanionEventSource.SnapshotDiff,
            "Cargo ship appeared");
        history.Append(persisted, 200);
        await using var manager = new RustPlusLiveSessionManager(
            new RustPlusSavedConnectionResolver(servers, secrets),
            new ScriptedFactory(_ => new ScriptedClient([Team(true, true)], [Markers(1)])),
            TimeProvider.System,
            new RustPlusPollingOptions(
                TimeSpan.FromHours(1),
                TimeSpan.FromHours(1),
                TimeSpan.FromHours(1),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(1),
                [TimeSpan.FromMilliseconds(5)]),
            history);

        await manager.StartAsync(profile.Id);

        Assert.Contains(manager.Current.Events, item => item.Id == persisted.Id);
    }

    private static ServerManager CreatePairedServer(
        InMemorySecretStore secrets,
        out ServerProfile profile)
    {
        var servers = new ServerManager(new InMemoryServerRepository(), TimeProvider.System, secrets);
        profile = servers.SaveWithPairing(
            new ServerProfileDraft(
                null,
                "Test server",
                "companion.example.invalid",
                28082,
                false,
                76561198000000000),
            "193746281");
        return servers;
    }

    private static TeamSnapshot Team(bool online, bool alive, float x = 100) => new(
        76561198000000000,
        [
            new TeamMemberSnapshot(
                76561198000000000,
                "Test teammate",
                x,
                200,
                online,
                alive,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch)
        ],
        [],
        [],
        null);

    private static MapMarkersSnapshot Markers(ulong id) => new(
        [new MapMarkerSnapshot(id, MapMarkerKind.CargoShip, 100, 200)]);

    // Stable, catalogue-verified Rust item IDs (see ItemCatalogTests): rifle.semiauto, scrap, wood.
    private const int RifleSemiAuto = -904863145;
    private const int Scrap = -932201673;
    private const int Wood = -151838493;

    private static MapMarkersSnapshot VendingMarkers(ulong id, params VendingOrderSnapshot[] offers) => new(
        [new MapMarkerSnapshot(id, MapMarkerKind.VendingMachine, 150, 300, Name: "Test Shop", VendingOrders: offers)]);

    private static VendingOrderSnapshot Offer(int itemId, int currencyId, int cost, int stock) =>
        new(itemId, 1, currencyId, cost, stock, false, false, 1, 1, null, null);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    private sealed class ScriptedFactory(Func<int, ScriptedClient> create) : IRustPlusClientFactory
    {
        public List<ScriptedClient> Clients { get; } = [];

        public int CreateCount => Clients.Count;

        public IRustPlusClient Create()
        {
            var client = create(Clients.Count);
            Clients.Add(client);
            return client;
        }
    }

    private sealed class ScriptedClient(
        IReadOnlyList<TeamSnapshot> teams,
        IReadOnlyList<MapMarkersSnapshot> markerSnapshots,
        bool disconnectAfterFirstTeam = false,
        CameraInfoSnapshot? cameraInfo = null,
        CameraFrameSnapshot? frameDuringSubscribe = null) : IRustPlusClient
    {
        private int _teamIndex;
        private int _markerIndex;
        private bool _cameraSubscribed;

        public bool IsConnected { get; private set; }

        public int MapCallCount { get; private set; }

        public int TeamCallCount => Volatile.Read(ref _teamIndex);

        public int CameraSubscribeCallCount { get; private set; }

        public string? LastSubscribedCameraId { get; private set; }

        public int ZoomCallCount { get; private set; }

        public event EventHandler<CameraFrameSnapshot>? CameraFrameReceived;

        public event EventHandler<RustPlusError>? CameraSubscriptionFailed;

        /// <summary>Test hook: simulates a camera ray broadcast arriving for the active subscription.</summary>
        public void RaiseCameraFrame(CameraFrameSnapshot frame) => CameraFrameReceived?.Invoke(this, frame);

        /// <summary>Test hook: simulates the keep-alive renewal failing (e.g. camera destroyed).</summary>
        public void RaiseCameraSubscriptionFailed(RustPlusError error) => CameraSubscriptionFailed?.Invoke(this, error);

        public Task ConnectAsync(RustPlusConnectionOptions options, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public Task<RustPlusResult<ServerInfoSnapshot>> GetServerInfoAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(IsConnected
                ? RustPlusResult<ServerInfoSnapshot>.Success(new ServerInfoSnapshot(
                    "Test server", null, null, "Procedural Map", 4500, null,
                    null, null, null, null, null, null, null, null, null))
                : RustPlusResult<ServerInfoSnapshot>.Failure("not_connected", "Disconnected."));

        public Task<RustPlusResult<ServerMapSnapshot>> GetMapAsync(CancellationToken cancellationToken = default)
        {
            MapCallCount++;
            return Task.FromResult(RustPlusResult<ServerMapSnapshot>.Failure("unexpected", "Map must not be polled."));
        }

        public Task<RustPlusResult<TeamSnapshot>> GetTeamAsync(CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
            {
                return Task.FromResult(RustPlusResult<TeamSnapshot>.Failure("not_connected", "Disconnected."));
            }

            var result = teams[Math.Min(Interlocked.Increment(ref _teamIndex) - 1, teams.Count - 1)];
            if (disconnectAfterFirstTeam && _teamIndex == 1)
            {
                IsConnected = false;
            }

            return Task.FromResult(RustPlusResult<TeamSnapshot>.Success(result));
        }

        public Task<RustPlusResult<TeamChatSnapshot>> GetTeamChatAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(IsConnected
                ? RustPlusResult<TeamChatSnapshot>.Success(new TeamChatSnapshot([]))
                : RustPlusResult<TeamChatSnapshot>.Failure("not_connected", "Disconnected."));

        public Task<RustPlusResult<MapMarkersSnapshot>> GetMapMarkersAsync(CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
            {
                return Task.FromResult(RustPlusResult<MapMarkersSnapshot>.Failure("not_connected", "Disconnected."));
            }

            var result = markerSnapshots[Math.Min(Interlocked.Increment(ref _markerIndex) - 1, markerSnapshots.Count - 1)];
            return Task.FromResult(RustPlusResult<MapMarkersSnapshot>.Success(result));
        }

        public Task<RustPlusResult<CameraInfoSnapshot>> SubscribeToCameraAsync(
            string cameraId,
            CancellationToken cancellationToken = default)
        {
            CameraSubscribeCallCount++;
            LastSubscribedCameraId = cameraId;
            if (cameraInfo is null)
            {
                return Task.FromResult(
                    RustPlusResult<CameraInfoSnapshot>.Failure("no_camera_configured", "No test camera configured."));
            }

            _cameraSubscribed = true;
            if (frameDuringSubscribe is not null)
            {
                // Simulates a ray broadcast arriving on the shared connection while the subscribe
                // request/response is still in flight.
                CameraFrameReceived?.Invoke(this, frameDuringSubscribe);
            }

            return Task.FromResult(RustPlusResult<CameraInfoSnapshot>.Success(cameraInfo));
        }

        public Task<RustPlusResult<bool>> ZoomCameraAsync(CancellationToken cancellationToken = default)
        {
            ZoomCallCount++;
            return Task.FromResult(_cameraSubscribed
                ? RustPlusResult<bool>.Success(true)
                : RustPlusResult<bool>.Failure("no_active_camera", "No camera subscription is active."));
        }

        public Task<RustPlusResult<bool>> ShootCameraAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_cameraSubscribed
                ? RustPlusResult<bool>.Success(true)
                : RustPlusResult<bool>.Failure("no_active_camera", "No camera subscription is active."));

        public Task<RustPlusResult<bool>> ReloadCameraAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_cameraSubscribed
                ? RustPlusResult<bool>.Success(true)
                : RustPlusResult<bool>.Failure("no_active_camera", "No camera subscription is active."));

        public Task<RustPlusResult<bool>> LookCameraAsync(
            float deltaX,
            float deltaY,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_cameraSubscribed
                ? RustPlusResult<bool>.Success(true)
                : RustPlusResult<bool>.Failure("no_active_camera", "No camera subscription is active."));

        public Task<RustPlusResult<bool>> MoveCameraAsync(
            CameraMoveDirection direction,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_cameraSubscribed
                ? RustPlusResult<bool>.Success(true)
                : RustPlusResult<bool>.Failure("no_active_camera", "No camera subscription is active."));

        public Task UnsubscribeFromCameraAsync(CancellationToken cancellationToken = default)
        {
            _cameraSubscribed = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            return ValueTask.CompletedTask;
        }
    }
}
