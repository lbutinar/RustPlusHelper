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
        bool disconnectAfterFirstTeam = false) : IRustPlusClient
    {
        private int _teamIndex;
        private int _markerIndex;

        public bool IsConnected { get; private set; }

        public int MapCallCount { get; private set; }

        public int TeamCallCount => Volatile.Read(ref _teamIndex);

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

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            return ValueTask.CompletedTask;
        }
    }
}
