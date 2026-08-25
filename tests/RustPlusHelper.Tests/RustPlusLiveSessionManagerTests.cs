using RustPlusHelper.Application.Pairing;
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
        // A manual clock instead of a real Task.Delay: the throttle window is compared against
        // timeProvider.GetUtcNow(), so advancing this fake clock past it is deterministic, whereas a
        // real 250ms sleep against a 200ms window leaves only a 50ms margin that CI/GC jitter can eat.
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        await using var manager = CreateManager(servers, secrets, factory, timeProvider: clock);
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

        clock.Advance(TimeSpan.FromMilliseconds(250));
        var frameC = new CameraFrameSnapshot([3], 65f, DateTimeOffset.UtcNow);
        client.RaiseCameraFrame(frameC);
        Assert.Same(frameC, manager.CurrentCamera.LatestFrame);
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
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

    [Fact]
    public async Task ArmsEachPairedEntityOnceOnConnectAndPopulatesLiveState()
    {
        using var secrets = new InMemorySecretStore();
        var servers = CreatePairedServer(secrets, out var profile);
        var pairedEntities = new InMemoryPairedEntityRepository();
        pairedEntities.Add(new PairedEntity(Guid.NewGuid(), profile.Id, 111, PairedEntityKind.Switch, "Front gate", DateTimeOffset.UnixEpoch));
        var factory = new ScriptedFactory(_ =>
        {
            var client = new ScriptedClient([Team(online: true, alive: true)], [Markers(1)]);
            client.ConfigureSmartDevice(111, value: true);
            return client;
        });
        await using var manager = CreateManager(servers, secrets, factory, pairedEntities);

        await manager.StartAsync(profile.Id);
        await WaitUntilAsync(() => manager.PairedEntityStates.ContainsKey(111));

        var state = manager.PairedEntityStates[111];
        Assert.Equal(PairedEntityKind.Switch, state.Kind);
        Assert.True(state.Value);
        Assert.Equal(1, factory.Clients[0].SmartSwitchInfoRequests.Count(id => id == 111));
    }

    [Fact]
    public async Task RoutesEntityBroadcastByStoredKindNotPayloadShape()
    {
        using var secrets = new InMemorySecretStore();
        var servers = CreatePairedServer(secrets, out var profile);
        var pairedEntities = new InMemoryPairedEntityRepository();
        pairedEntities.Add(new PairedEntity(
            Guid.NewGuid(), profile.Id, 222, PairedEntityKind.StorageMonitor, "Base storage", DateTimeOffset.UnixEpoch));
        var factory = new ScriptedFactory(_ =>
        {
            var client = new ScriptedClient([Team(online: true, alive: true)], [Markers(1)]);
            client.ConfigureStorageMonitor(222, capacity: 24, [new StorageItemSnapshot(-932201673, 400, false)]);
            return client;
        });
        await using var manager = CreateManager(servers, secrets, factory, pairedEntities);
        await manager.StartAsync(profile.Id);
        await WaitUntilAsync(() => manager.PairedEntityStates.ContainsKey(222));

        // A broadcast that looks switch-shaped (only Value set, no Capacity/Items) must not be
        // mistaken for this entity's real kind — RustPlusApi itself cannot tell them apart from the
        // payload alone; only our own paired-entity record can.
        factory.Clients[0].RaiseEntityStateChanged(new EntityStateChangedSnapshot(222, true, null, null, []));

        var state = manager.PairedEntityStates[222];
        Assert.Equal(PairedEntityKind.StorageMonitor, state.Kind);
        Assert.Null(state.Value);
        Assert.Equal(24, state.Capacity);
        Assert.Single(state.Items);
    }

    [Fact]
    public async Task EntityBroadcastUpdatesASwitchsLiveValue()
    {
        using var secrets = new InMemorySecretStore();
        var servers = CreatePairedServer(secrets, out var profile);
        var pairedEntities = new InMemoryPairedEntityRepository();
        pairedEntities.Add(new PairedEntity(Guid.NewGuid(), profile.Id, 333, PairedEntityKind.Switch, "Front gate", DateTimeOffset.UnixEpoch));
        var factory = new ScriptedFactory(_ =>
        {
            var client = new ScriptedClient([Team(online: true, alive: true)], [Markers(1)]);
            client.ConfigureSmartDevice(333, value: false);
            return client;
        });
        await using var manager = CreateManager(servers, secrets, factory, pairedEntities);
        await manager.StartAsync(profile.Id);
        await WaitUntilAsync(() => manager.PairedEntityStates.ContainsKey(333));

        factory.Clients[0].RaiseEntityStateChanged(new EntityStateChangedSnapshot(333, true, null, null, []));

        Assert.True(manager.PairedEntityStates[333].Value);
    }

    [Fact]
    public async Task ToggleSmartSwitchAsyncFlipsTheSwitchAndUpdatesLiveState()
    {
        using var secrets = new InMemorySecretStore();
        var servers = CreatePairedServer(secrets, out var profile);
        var pairedEntities = new InMemoryPairedEntityRepository();
        pairedEntities.Add(new PairedEntity(Guid.NewGuid(), profile.Id, 444, PairedEntityKind.Switch, "Front gate", DateTimeOffset.UnixEpoch));
        var factory = new ScriptedFactory(_ =>
        {
            var client = new ScriptedClient([Team(online: true, alive: true)], [Markers(1)]);
            client.ConfigureSmartDevice(444, value: false);
            return client;
        });
        await using var manager = CreateManager(servers, secrets, factory, pairedEntities);
        await manager.StartAsync(profile.Id);
        await WaitUntilAsync(() => manager.PairedEntityStates.ContainsKey(444));

        var result = await manager.ToggleSmartSwitchAsync(444);

        Assert.True(result.IsSuccess);
        Assert.True(result.Data!.Value);
        Assert.True(manager.PairedEntityStates[444].Value);
    }

    [Fact]
    public async Task SetSmartSwitchAsyncFailsWhenNotConnected()
    {
        using var secrets = new InMemorySecretStore();
        var servers = CreatePairedServer(secrets, out var profile);
        var factory = new ScriptedFactory(_ => new ScriptedClient([Team(online: true, alive: true)], [Markers(1)]));
        await using var manager = CreateManager(servers, secrets, factory);

        var result = await manager.SetSmartSwitchAsync(555, true);

        Assert.False(result.IsSuccess);
        Assert.Equal("not_connected", result.Error?.Code);
    }

    [Fact]
    public async Task SendTeamMessageAsyncAppendsTheSentMessageToLiveChatState()
    {
        using var secrets = new InMemorySecretStore();
        var servers = CreatePairedServer(secrets, out var profile);
        var factory = new ScriptedFactory(_ => new ScriptedClient([Team(online: true, alive: true)], [Markers(1)]));
        await using var manager = CreateManager(servers, secrets, factory);
        await manager.StartAsync(profile.Id);
        await WaitUntilAsync(() => manager.Current.Status == RustPlusLiveSessionStatus.Connected);

        var result = await manager.SendTeamMessageAsync("heading to launch site");

        Assert.True(result.IsSuccess);
        Assert.Equal("heading to launch site", result.Data!.Message);
        Assert.Contains(factory.Clients[0].SentTeamMessages, message => message == "heading to launch site");
        Assert.Contains(manager.Current.Chat!.Messages, message => message.Message == "heading to launch site");
    }

    [Fact]
    public async Task SendTeamMessageAsyncFailsWhenNotConnected()
    {
        using var secrets = new InMemorySecretStore();
        var servers = CreatePairedServer(secrets, out var profile);
        var factory = new ScriptedFactory(_ => new ScriptedClient([Team(online: true, alive: true)], [Markers(1)]));
        await using var manager = CreateManager(servers, secrets, factory);

        var result = await manager.SendTeamMessageAsync("hello");

        Assert.False(result.IsSuccess);
        Assert.Equal("not_connected", result.Error?.Code);
    }

    [Fact]
    public async Task RecordExternalEventUpdatesLiveStateOnlyForTheActiveServerButAlwaysPersists()
    {
        using var secrets = new InMemorySecretStore();
        var servers = CreatePairedServer(secrets, out var profile);
        var history = new InMemoryCompanionEventRepository();
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
            history,
            new InMemoryPairedEntityRepository());
        await manager.StartAsync(profile.Id);
        await WaitUntilAsync(() => manager.Current.Status == RustPlusLiveSessionStatus.Connected);

        var recorded = new List<CompanionEvent>();
        manager.EventRecorded += (_, item) => recorded.Add(item);

        manager.RecordExternalEvent(profile.Id, CompanionEventKind.AlarmTriggered, "Base alarm", "Raiders detected");

        Assert.Contains(manager.Current.Events, item => item.Title == "Base alarm");
        Assert.Contains(history.GetRecent(profile.Id, 200), item => item.Title == "Base alarm");
        Assert.Single(recorded, item => item.Title == "Base alarm");

        var otherServerId = Guid.NewGuid();
        manager.RecordExternalEvent(otherServerId, CompanionEventKind.AlarmTriggered, "Other server alarm");

        Assert.DoesNotContain(manager.Current.Events, item => item.Title == "Other server alarm");
        Assert.Contains(history.GetRecent(otherServerId, 200), item => item.Title == "Other server alarm");
        Assert.Single(recorded, item => item.Title == "Other server alarm");
    }

    private static RustPlusLiveSessionManager CreateManager(
        ServerManager servers,
        InMemorySecretStore secrets,
        IRustPlusClientFactory factory,
        IPairedEntityRepository? pairedEntities = null,
        TimeProvider? timeProvider = null) =>
        new(
            new RustPlusSavedConnectionResolver(servers, secrets),
            factory,
            timeProvider ?? TimeProvider.System,
            new RustPlusPollingOptions(
                TimeSpan.FromMilliseconds(15),
                TimeSpan.FromMilliseconds(20),
                TimeSpan.FromMilliseconds(15),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(1),
                [TimeSpan.FromMilliseconds(5)]),
            new InMemoryCompanionEventRepository(),
            pairedEntities ?? new InMemoryPairedEntityRepository());

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
        history.Append(persisted, 200, DateTimeOffset.MinValue);
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
            history,
            new InMemoryPairedEntityRepository());

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

    private static Task WaitUntilAsync(Func<bool> condition) =>
        AsyncTestHelpers.WaitUntilAsync(condition, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(5));

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

        private readonly Dictionary<ulong, SmartDeviceStateSnapshot> _smartDeviceInfo = [];
        private readonly Dictionary<ulong, StorageMonitorStateSnapshot> _storageMonitorInfo = [];

        public event EventHandler<CameraFrameSnapshot>? CameraFrameReceived;

        public event EventHandler<RustPlusError>? CameraSubscriptionFailed;

        public event EventHandler<EntityStateChangedSnapshot>? EntityStateChanged;

        /// <summary>Test hook: simulates a camera ray broadcast arriving for the active subscription.</summary>
        public void RaiseCameraFrame(CameraFrameSnapshot frame) => CameraFrameReceived?.Invoke(this, frame);

        /// <summary>Test hook: simulates the keep-alive renewal failing (e.g. camera destroyed).</summary>
        public void RaiseCameraSubscriptionFailed(RustPlusError error) => CameraSubscriptionFailed?.Invoke(this, error);

        public List<ulong> SmartSwitchInfoRequests { get; } = [];

        public List<ulong> AlarmInfoRequests { get; } = [];

        public List<ulong> StorageMonitorInfoRequests { get; } = [];

        /// <summary>Test hook: configures the value Get*InfoAsync returns for a given entity.</summary>
        public void ConfigureSmartDevice(ulong entityId, bool value) =>
            _smartDeviceInfo[entityId] = new SmartDeviceStateSnapshot(entityId, value);

        /// <summary>Test hook: configures the value GetStorageMonitorInfoAsync returns for a given entity.</summary>
        public void ConfigureStorageMonitor(ulong entityId, int? capacity, IReadOnlyList<StorageItemSnapshot> items) =>
            _storageMonitorInfo[entityId] = new StorageMonitorStateSnapshot(entityId, capacity, true, items);

        /// <summary>Test hook: simulates an entity-changed broadcast for the given entity.</summary>
        public void RaiseEntityStateChanged(EntityStateChangedSnapshot snapshot) =>
            EntityStateChanged?.Invoke(this, snapshot);

        public Task<RustPlusResult<SmartDeviceStateSnapshot>> GetSmartSwitchInfoAsync(
            ulong entityId,
            CancellationToken cancellationToken = default)
        {
            SmartSwitchInfoRequests.Add(entityId);
            return Task.FromResult(_smartDeviceInfo.TryGetValue(entityId, out var value)
                ? RustPlusResult<SmartDeviceStateSnapshot>.Success(value)
                : RustPlusResult<SmartDeviceStateSnapshot>.Failure("not_configured", "No test device configured."));
        }

        public Task<RustPlusResult<SmartDeviceStateSnapshot>> GetAlarmInfoAsync(
            ulong entityId,
            CancellationToken cancellationToken = default)
        {
            AlarmInfoRequests.Add(entityId);
            return Task.FromResult(_smartDeviceInfo.TryGetValue(entityId, out var value)
                ? RustPlusResult<SmartDeviceStateSnapshot>.Success(value)
                : RustPlusResult<SmartDeviceStateSnapshot>.Failure("not_configured", "No test device configured."));
        }

        public Task<RustPlusResult<StorageMonitorStateSnapshot>> GetStorageMonitorInfoAsync(
            ulong entityId,
            CancellationToken cancellationToken = default)
        {
            StorageMonitorInfoRequests.Add(entityId);
            return Task.FromResult(_storageMonitorInfo.TryGetValue(entityId, out var value)
                ? RustPlusResult<StorageMonitorStateSnapshot>.Success(value)
                : RustPlusResult<StorageMonitorStateSnapshot>.Failure("not_configured", "No test device configured."));
        }

        public Task<RustPlusResult<SmartDeviceStateSnapshot>> SetSmartSwitchValueAsync(
            ulong entityId,
            bool value,
            CancellationToken cancellationToken = default)
        {
            var snapshot = new SmartDeviceStateSnapshot(entityId, value);
            _smartDeviceInfo[entityId] = snapshot;
            return Task.FromResult(RustPlusResult<SmartDeviceStateSnapshot>.Success(snapshot));
        }

        public Task<RustPlusResult<SmartDeviceStateSnapshot>> ToggleSmartSwitchAsync(
            ulong entityId,
            CancellationToken cancellationToken = default)
        {
            var current = _smartDeviceInfo.TryGetValue(entityId, out var existing) && existing.Value;
            return SetSmartSwitchValueAsync(entityId, !current, cancellationToken);
        }

        public Task<RustPlusResult<SmartDeviceStateSnapshot>> StrobeSmartSwitchAsync(
            ulong entityId,
            TimeSpan duration,
            bool value,
            CancellationToken cancellationToken = default) =>
            SetSmartSwitchValueAsync(entityId, value, cancellationToken);

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

        public List<string> SentTeamMessages { get; } = [];

        /// <summary>Test hook: makes the next/every <see cref="SendTeamMessageAsync"/> call fail.</summary>
        public bool FailSendTeamMessage { get; set; }

        public Task<RustPlusResult<TeamChatMessageSnapshot>> SendTeamMessageAsync(
            string message,
            CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
            {
                return Task.FromResult(RustPlusResult<TeamChatMessageSnapshot>.Failure("not_connected", "Disconnected."));
            }

            if (FailSendTeamMessage)
            {
                return Task.FromResult(RustPlusResult<TeamChatMessageSnapshot>.Failure("send_failed", "Test-configured failure."));
            }

            SentTeamMessages.Add(message);
            return Task.FromResult(RustPlusResult<TeamChatMessageSnapshot>.Success(
                new TeamChatMessageSnapshot(111, "Tester", message, "#FFFFFFFF", DateTimeOffset.UnixEpoch)));
        }

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
