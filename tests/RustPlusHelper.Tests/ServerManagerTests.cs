using System.Security.Cryptography;
using System.Text;
using RustPlusHelper.Application.Identity;
using RustPlusHelper.Application.Security;
using RustPlusHelper.Application.Servers;
using RustPlusHelper.Application.Testing;

namespace RustPlusHelper.Tests;

public sealed class ServerManagerTests
{
    private static readonly DateTimeOffset FixedUtc = new(2026, 8, 17, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SavesSelectsReloadsAndRemovesProfilesThroughRepositoryBoundary()
    {
        var repository = new InMemoryServerRepository();
        var manager = new ServerManager(repository, new FixedTimeProvider(FixedUtc));

        var saved = manager.Save(new ServerProfileDraft(
            null,
            " EU Main ",
            " companion.example.invalid ",
            28082));

        Assert.Equal("EU Main", saved.DisplayName);
        Assert.Equal("companion.example.invalid", saved.Host);
        Assert.True(saved.UseFacepunchProxy);
        Assert.Equal(saved.Id, manager.SelectedServerId);

        var reloaded = new ServerManager(repository, new FixedTimeProvider(FixedUtc.AddMinutes(1)));
        reloaded.Load();
        Assert.Equal(saved, Assert.Single(reloaded.Profiles));
        Assert.True(reloaded.Remove(saved.Id));
        Assert.Empty(reloaded.Profiles);
    }

    [Theory]
    [InlineData("", "host", 28082)]
    [InlineData("name", "", 28082)]
    [InlineData("name", "host", 0)]
    [InlineData("name", "host", 65536)]
    public void RejectsInvalidProfiles(string name, string host, int port)
    {
        var manager = new ServerManager(new InMemoryServerRepository(), new FixedTimeProvider(FixedUtc));

        Assert.ThrowsAny<ArgumentException>(() => manager.Save(new ServerProfileDraft(
            null,
            name,
            host,
            port)));
    }

    [Fact]
    public void SelectReturnsFalseForUnknownProfile()
    {
        var manager = new ServerManager(new InMemoryServerRepository(), new FixedTimeProvider(FixedUtc));

        Assert.False(manager.Select(Guid.NewGuid()));
    }

    [Fact]
    public void SavesPlayerIdAndCanonicalTokenThroughProtectedStoreBoundary()
    {
        var repository = new InMemoryServerRepository();
        using var secrets = new InMemorySecretStore();
        var manager = new ServerManager(repository, new FixedTimeProvider(FixedUtc), secrets);

        var saved = manager.SaveWithPairing(new ServerProfileDraft(
            null,
            "Dev server",
            "companion.example.invalid",
            28082,
            true,
            ulong.MaxValue), " -2147483648 ".AsSpan());

        Assert.Equal(ulong.MaxValue, saved.PlayerId);
        Assert.True(manager.HasPairing(saved.Id));
        var restored = secrets.Retrieve(saved.Id, SecretKind.RustPlusPlayerToken);
        try
        {
            Assert.NotNull(restored);
            Assert.Equal("-2147483648", Encoding.UTF8.GetString(restored));
        }
        finally
        {
            if (restored is not null)
            {
                CryptographicOperations.ZeroMemory(restored);
            }
        }
    }

    [Theory]
    [InlineData(null, "123", "Steam64 ID is required")]
    [InlineData(76561198000000000L, "not-a-token", "signed 32-bit integer")]
    [InlineData(76561198000000000L, "2147483648", "signed 32-bit integer")]
    public void RejectsIncompleteOrInvalidPairing(long? playerId, string token, string expectedMessage)
    {
        using var secrets = new InMemorySecretStore();
        var manager = new ServerManager(
            new InMemoryServerRepository(),
            new FixedTimeProvider(FixedUtc),
            secrets);

        var exception = Assert.Throws<ArgumentException>(() => manager.SaveWithPairing(
            new ServerProfileDraft(null, "Dev", "host", 28082, true, playerId is null ? null : (ulong)playerId.Value),
            token.AsSpan()));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
        Assert.Empty(manager.Profiles);
    }

    [Fact]
    public void BlankTokenPreservesPairingButRequiresTokenWhenPlayerIdChanges()
    {
        var repository = new InMemoryServerRepository();
        using var secrets = new InMemorySecretStore();
        var manager = new ServerManager(repository, new FixedTimeProvider(FixedUtc), secrets);
        var original = manager.SaveWithPairing(
            new ServerProfileDraft(null, "Dev", "host", 28082, true, 76561198000000000UL),
            "-42".AsSpan());

        var edited = manager.SaveWithPairing(
            new ServerProfileDraft(original.Id, "Dev renamed", "host", 28082, true, original.PlayerId),
            ReadOnlySpan<char>.Empty);

        Assert.Equal("Dev renamed", edited.DisplayName);
        Assert.True(manager.HasPairing(original.Id));
        var exception = Assert.Throws<ArgumentException>(() => manager.SaveWithPairing(
            new ServerProfileDraft(original.Id, "Dev renamed", "host", 28082, true, 76561198000000001UL),
            ReadOnlySpan<char>.Empty));
        Assert.Contains("Enter the player token again", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalPlayerIdentityIsReusedWhileTokensRemainPerServer()
    {
        var identityRepository = new InMemoryPlayerIdentityRepository();
        var identity = new PlayerIdentityManager(identityRepository, new FixedTimeProvider(FixedUtc));
        identity.Save(76561198000000000UL);
        using var secrets = new InMemorySecretStore();
        var manager = new ServerManager(
            new InMemoryServerRepository(),
            new FixedTimeProvider(FixedUtc),
            secrets,
            identity);

        var first = manager.SaveWithPairing(
            new ServerProfileDraft(null, "One", "one.invalid", 28082),
            "101".AsSpan());
        var second = manager.SaveWithPairing(
            new ServerProfileDraft(null, "Two", "two.invalid", 28083),
            "-202".AsSpan());

        Assert.Equal(identity.Current?.SteamId, first.PlayerId);
        Assert.Equal(identity.Current?.SteamId, second.PlayerId);
        AssertToken(secrets, first.Id, "101");
        AssertToken(secrets, second.Id, "-202");
        var reloadedIdentity = new PlayerIdentityManager(identityRepository, new FixedTimeProvider(FixedUtc));
        reloadedIdentity.Load();
        Assert.Equal(identity.Current, reloadedIdentity.Current);
    }

    [Fact]
    public void PlayerIdentityCannotChangeWhileServerPairingsExist()
    {
        var servers = new InMemoryServerRepository();
        using var secrets = new InMemorySecretStore();
        var identity = new PlayerIdentityManager(
            new InMemoryPlayerIdentityRepository(),
            new FixedTimeProvider(FixedUtc),
            servers,
            secrets);
        identity.Save(76561198000000000UL);
        var manager = new ServerManager(servers, new FixedTimeProvider(FixedUtc), secrets, identity);
        manager.SaveWithPairing(
            new ServerProfileDraft(null, "Dev", "dev.invalid", 28082),
            "42".AsSpan());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            identity.Save(76561198000000001UL));

        Assert.Contains("Remove or re-pair", exception.Message, StringComparison.Ordinal);
        Assert.Equal(76561198000000000UL, identity.Current?.SteamId);
    }

    private static void AssertToken(InMemorySecretStore secrets, Guid serverId, string expected)
    {
        var restored = secrets.Retrieve(serverId, SecretKind.RustPlusPlayerToken);
        try
        {
            Assert.NotNull(restored);
            Assert.Equal(expected, Encoding.UTF8.GetString(restored));
        }
        finally
        {
            if (restored is not null)
            {
                CryptographicOperations.ZeroMemory(restored);
            }
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
