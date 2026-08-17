using System.Security.Cryptography;
using System.Text;
using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Application.Security;
using RustPlusHelper.Application.Servers;
using RustPlusHelper.Application.Testing;

namespace RustPlusHelper.Tests;

public sealed class RustPlusConnectionManagerTests
{
    private static readonly DateTimeOffset FixedUtc = new(2026, 8, 17, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AuthenticatesWithServerInfoThenClosesTestSocketAndClearsTokenBuffer()
    {
        var servers = CreateServers(out var profile);
        using var secrets = new TrackingSecretStore("193746281");
        var client = new StubRustPlusClient(RustPlusResult<ServerInfoSnapshot>.Success(ServerInfo("Live server")));
        using var manager = new RustPlusConnectionManager(
            servers,
            secrets,
            new SingleClientFactory(client),
            new FixedTimeProvider(FixedUtc));

        var state = await manager.TestConnectionAsync(profile.Id);

        Assert.Equal(RustPlusConnectionStatus.Succeeded, state.Status);
        Assert.Equal("Live server", state.ServerInfo?.Name);
        Assert.Equal(FixedUtc, state.CheckedAtUtc);
        Assert.NotNull(client.ConnectionOptions);
        Assert.True(client.ConnectionOptions.UseFacepunchProxy);
        Assert.Equal(193746281, client.ConnectionOptions.PlayerToken);
        Assert.False(client.IsConnected);
        Assert.NotNull(secrets.LastRetrievedBuffer);
        Assert.All(secrets.LastRetrievedBuffer, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task ClassifiesAccessDeniedAsRejectedPairing()
    {
        var servers = CreateServers(out var profile);
        using var secrets = new TrackingSecretStore("123");
        var client = new StubRustPlusClient(
            RustPlusResult<ServerInfoSnapshot>.Failure("AccessDenied", "access_denied"));
        using var manager = new RustPlusConnectionManager(
            servers,
            secrets,
            new SingleClientFactory(client),
            new FixedTimeProvider(FixedUtc));

        var state = await manager.TestConnectionAsync(profile.Id);

        Assert.Equal(RustPlusConnectionStatus.AuthenticationRejected, state.Status);
        Assert.Contains("Re-pair", state.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HonorsPersistedDirectTransportOptIn()
    {
        var servers = CreateServers(out var profile, useFacepunchProxy: false);
        using var secrets = new TrackingSecretStore("123");
        var factory = new SingleClientFactory(new StubRustPlusClient(
            RustPlusResult<ServerInfoSnapshot>.Success(ServerInfo("unused"))));
        using var manager = new RustPlusConnectionManager(
            servers,
            secrets,
            factory,
            new FixedTimeProvider(FixedUtc));

        var state = await manager.TestConnectionAsync(profile.Id);

        Assert.Equal(RustPlusConnectionStatus.Succeeded, state.Status);
        Assert.Equal(1, factory.CreateCount);
        Assert.False(factory.Client.ConnectionOptions?.UseFacepunchProxy);
    }

    [Fact]
    public async Task DoesNotExposeTokenWhenTransportThrowsIt()
    {
        const string token = "-2147483648";
        var servers = CreateServers(out var profile);
        using var secrets = new TrackingSecretStore(token);
        var client = new StubRustPlusClient(
            RustPlusResult<ServerInfoSnapshot>.Success(ServerInfo("unused")),
            throwTokenFromConnect: true);
        var factory = new SingleClientFactory(client);
        using var manager = new RustPlusConnectionManager(
            servers,
            secrets,
            factory,
            new FixedTimeProvider(FixedUtc));

        var state = await manager.TestConnectionAsync(profile.Id);

        Assert.Equal(RustPlusConnectionStatus.Failed, state.Status);
        Assert.Equal(1, factory.CreateCount);
        Assert.True(client.ConnectionOptions?.UseFacepunchProxy);
        Assert.DoesNotContain(token, state.Label, StringComparison.Ordinal);
        Assert.DoesNotContain(token, state.Detail ?? string.Empty, StringComparison.Ordinal);
    }

    private static ServerManager CreateServers(out ServerProfile profile, bool useFacepunchProxy = true)
    {
        var servers = new ServerManager(
            new InMemoryServerRepository(),
            new FixedTimeProvider(FixedUtc));
        profile = servers.Save(new ServerProfileDraft(
            null,
            "Test server",
            "companion.example.invalid",
            28082,
            useFacepunchProxy,
            76561198000000000));
        return servers;
    }

    private static ServerInfoSnapshot ServerInfo(string name) => new(
        name,
        null,
        null,
        "Procedural Map",
        4500,
        FixedUtc.AddDays(-2),
        10,
        200,
        0,
        1,
        2,
        null,
        null,
        null,
        null);

    private sealed class SingleClientFactory(StubRustPlusClient client) : IRustPlusClientFactory
    {
        public int CreateCount { get; private set; }

        public StubRustPlusClient Client { get; } = client;

        public IRustPlusClient Create()
        {
            CreateCount++;
            return Client;
        }
    }

    private sealed class StubRustPlusClient(
        RustPlusResult<ServerInfoSnapshot> serverInfo,
        bool throwTokenFromConnect = false) : IRustPlusClient
    {
        public bool IsConnected { get; private set; }

        public RustPlusConnectionOptions? ConnectionOptions { get; private set; }

        public Task ConnectAsync(RustPlusConnectionOptions options, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConnectionOptions = options;
            if (throwTokenFromConnect)
            {
                throw new InvalidOperationException($"transport leaked {options.PlayerToken}");
            }

            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsConnected = false;
            return Task.CompletedTask;
        }

        public Task<RustPlusResult<ServerInfoSnapshot>> GetServerInfoAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(serverInfo);
        }

        public Task<RustPlusResult<ServerMapSnapshot>> GetMapAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RustPlusResult<TeamSnapshot>> GetTeamAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RustPlusResult<TeamChatSnapshot>> GetTeamChatAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RustPlusResult<MapMarkersSnapshot>> GetMapMarkersAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async ValueTask DisposeAsync()
        {
            await DisconnectAsync().ConfigureAwait(false);
        }
    }

    private sealed class TrackingSecretStore(string token) : ISecretStore, IDisposable
    {
        private readonly byte[] _token = Encoding.UTF8.GetBytes(token);

        public byte[]? LastRetrievedBuffer { get; private set; }

        public void Store(Guid serverId, SecretKind kind, ReadOnlySpan<byte> secret) =>
            throw new NotSupportedException();

        public bool Contains(Guid serverId, SecretKind kind) => true;

        public byte[] Retrieve(Guid serverId, SecretKind kind)
        {
            LastRetrievedBuffer = _token.ToArray();
            return LastRetrievedBuffer;
        }

        public bool Delete(Guid serverId, SecretKind kind) => false;

        public void Dispose()
        {
            CryptographicOperations.ZeroMemory(_token);
            if (LastRetrievedBuffer is not null)
            {
                CryptographicOperations.ZeroMemory(LastRetrievedBuffer);
            }
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
