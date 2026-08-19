using System.Security.Cryptography;
using System.Text;
using RustPlusHelper.Application.Identity;
using RustPlusHelper.Application.Pairing;
using RustPlusHelper.Application.Security;
using RustPlusHelper.Application.Servers;
using RustPlusHelper.Application.Testing;

namespace RustPlusHelper.Tests;

public sealed class RustPlusPairingManagerTests
{
    private const ulong PlayerId = 76561198000000000UL;
    private static readonly Guid RustPlusServerId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task RegistrationProtectsCredentialsAndZerosProviderBuffer()
    {
        using var applicationSecrets = new InMemoryApplicationSecretStore();
        using var serverSecrets = new InMemorySecretStore();
        var provider = new FakePairingProvider();
        var manager = CreateManager(provider, applicationSecrets, serverSecrets);

        await manager.RegisterAsync();

        Assert.Equal(RustPlusPairingStatus.Ready, manager.State.Status);
        Assert.True(applicationSecrets.Contains(ApplicationSecretKind.RustPlusFcmCredentials));
        Assert.NotNull(provider.ReturnedCredentials);
        Assert.All(provider.ReturnedCredentials!, value => Assert.Equal(0, value));
        var restored = applicationSecrets.Retrieve(ApplicationSecretKind.RustPlusFcmCredentials);
        try
        {
            Assert.Equal("sanitized-registration", Encoding.UTF8.GetString(restored!));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(restored!);
        }
    }

    [Fact]
    public async Task PairingCaptureSavesIdentityServerAndProtectedToken()
    {
        using var applicationSecrets = RegisteredCredentials();
        using var serverSecrets = new InMemorySecretStore();
        var provider = new FakePairingProvider
        {
            Pairing = new("203.0.113.20", 28082, PlayerId, -4242, "Sanitized server", RustPlusServerId)
        };
        var serverRepository = new InMemoryServerRepository();
        var identity = new PlayerIdentityManager(
            new InMemoryPlayerIdentityRepository(),
            TimeProvider.System,
            serverRepository,
            serverSecrets);
        var servers = new ServerManager(serverRepository, TimeProvider.System, serverSecrets, identity);
        var manager = new RustPlusPairingManager(provider, applicationSecrets, identity, servers);
        manager.Load();

        await manager.ListenAsync();

        Assert.Equal(RustPlusPairingStatus.Paired, manager.State.Status);
        Assert.Equal(PlayerId, identity.Current?.SteamId);
        var profile = Assert.Single(servers.Profiles);
        Assert.Equal("203.0.113.20", profile.Host);
        Assert.Equal(28082, profile.Port);
        Assert.True(profile.UseFacepunchProxy);
        Assert.Equal(RustPlusServerId, profile.RustPlusServerId);
        AssertToken(serverSecrets, profile.Id, "-4242");
    }

    [Fact]
    public async Task RePairingSameAddressUpdatesExistingProfileAndToken()
    {
        using var applicationSecrets = RegisteredCredentials();
        using var serverSecrets = new InMemorySecretStore();
        var provider = new FakePairingProvider
        {
            Pairing = new("pair.invalid", 28082, PlayerId, 1, "First name", RustPlusServerId)
        };
        var serverRepository = new InMemoryServerRepository();
        var identity = new PlayerIdentityManager(
            new InMemoryPlayerIdentityRepository(), TimeProvider.System, serverRepository, serverSecrets);
        var servers = new ServerManager(serverRepository, TimeProvider.System, serverSecrets, identity);
        var manager = new RustPlusPairingManager(provider, applicationSecrets, identity, servers);

        await manager.ListenAsync();
        var originalId = Assert.Single(servers.Profiles).Id;
        var updatedRustPlusServerId = Guid.NewGuid();
        provider.Pairing = new("PAIR.INVALID", 28082, PlayerId, 2, "Updated name", updatedRustPlusServerId);
        await manager.ListenAsync();

        var updated = Assert.Single(servers.Profiles);
        Assert.Equal(originalId, updated.Id);
        Assert.Equal("Updated name", updated.DisplayName);
        Assert.Equal(updatedRustPlusServerId, updated.RustPlusServerId);
        AssertToken(serverSecrets, updated.Id, "2");
    }

    [Fact]
    public async Task PairingForDifferentIdentityIsRejectedWithoutSavingToken()
    {
        using var applicationSecrets = RegisteredCredentials();
        using var serverSecrets = new InMemorySecretStore();
        var provider = new FakePairingProvider
        {
            Pairing = new("pair.invalid", 28082, PlayerId + 1, 99, "Other account", RustPlusServerId)
        };
        var serverRepository = new InMemoryServerRepository();
        var identity = new PlayerIdentityManager(
            new InMemoryPlayerIdentityRepository(), TimeProvider.System, serverRepository, serverSecrets);
        identity.Save(PlayerId);
        var servers = new ServerManager(serverRepository, TimeProvider.System, serverSecrets, identity);
        var manager = new RustPlusPairingManager(provider, applicationSecrets, identity, servers);

        await manager.ListenAsync();

        Assert.Equal(RustPlusPairingStatus.Failed, manager.State.Status);
        Assert.Contains("different Steam account", manager.State.Detail, StringComparison.Ordinal);
        Assert.Empty(servers.Profiles);
    }

    private static RustPlusPairingManager CreateManager(
        IRustPlusPairingProvider provider,
        IApplicationSecretStore applicationSecrets,
        ISecretStore serverSecrets)
    {
        var repositories = new InMemoryServerRepository();
        var identity = new PlayerIdentityManager(
            new InMemoryPlayerIdentityRepository(), TimeProvider.System, repositories, serverSecrets);
        return new(provider, applicationSecrets, identity,
            new ServerManager(repositories, TimeProvider.System, serverSecrets, identity));
    }

    private static InMemoryApplicationSecretStore RegisteredCredentials()
    {
        var store = new InMemoryApplicationSecretStore();
        store.Store(ApplicationSecretKind.RustPlusFcmCredentials, "sanitized-registration"u8);
        return store;
    }

    private static void AssertToken(InMemorySecretStore secrets, Guid serverId, string expected)
    {
        var restored = secrets.Retrieve(serverId, SecretKind.RustPlusPlayerToken);
        try
        {
            Assert.Equal(expected, Encoding.UTF8.GetString(restored!));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(restored!);
        }
    }

    private sealed class FakePairingProvider : IRustPlusPairingProvider
    {
        public byte[]? ReturnedCredentials { get; private set; }

        public CapturedRustPlusPairing Pairing { get; set; } =
            new("pair.invalid", 28082, PlayerId, 42, "Sanitized server", RustPlusServerId);

        public CapturedEntityPairing EntityPairing { get; set; } =
            new(PlayerId, 42, 12345, PairedEntityKind.Switch, "Sanitized switch");

        public Task<byte[]> RegisterAsync(CancellationToken cancellationToken = default)
        {
            ReturnedCredentials = Encoding.UTF8.GetBytes("sanitized-registration");
            return Task.FromResult(ReturnedCredentials);
        }

        public Task<CapturedRustPlusPairing> WaitForServerPairingAsync(
            ReadOnlyMemory<byte> credentials,
            CancellationToken cancellationToken = default) => Task.FromResult(Pairing);

        public Task<CapturedEntityPairing> WaitForEntityPairingAsync(
            ReadOnlyMemory<byte> credentials,
            CancellationToken cancellationToken = default) => Task.FromResult(EntityPairing);
    }
}
