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

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
