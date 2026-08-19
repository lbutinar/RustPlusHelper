using System.Text.Json;
using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Application.Testing;
using RustPlusHelper.Verification;

namespace RustPlusHelper.Tests;

public sealed class VerificationTests
{
    [Fact]
    public async Task AggregateReportExcludesNamesMessagesSteamIdsAndCoordinates()
    {
        await using var client = new FakeRustPlusClient();
        var connection = new RustPlusConnectionOptions("fake.invalid", 28082, ulong.MaxValue - 42, 193746281);
        var result = await new VerificationRunner(client).RunAsync(connection);

        var json = JsonSerializer.Serialize(result.Report);

        Assert.True(result.Report.Success);
        Assert.DoesNotContain("Kakec", json, StringComparison.Ordinal);
        Assert.DoesNotContain("heading to launch site", json, StringComparison.Ordinal);
        Assert.DoesNotContain((ulong.MaxValue - 42).ToString(System.Globalization.CultureInfo.InvariantCulture), json, StringComparison.Ordinal);
        Assert.DoesNotContain("193746281", json, StringComparison.Ordinal);
        Assert.Contains("Unknown", json, StringComparison.Ordinal);
        Assert.Contains("777", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CameraCodeAddsARequestAndSummaryWhenRequested()
    {
        await using var client = new FakeRustPlusClient();
        var connection = new RustPlusConnectionOptions("fake.invalid", 28082, ulong.MaxValue - 42, 193746281);
        var result = await new VerificationRunner(client).RunAsync(connection, cameraCode: "DOME1");

        Assert.True(result.Report.Success);
        Assert.True(result.Report.Requests["camera"].Success);
        Assert.NotNull(result.Report.Camera);
        Assert.Equal("DOME1", result.Report.Camera!.Code);
    }

    [Fact]
    public void CameraOptionIsParsedFromTheCommandLine()
    {
        var options = VerificationCommandOptions.Parse(["--live", "--camera", "DOME1"]);

        Assert.Equal("DOME1", options.CameraCode);
    }

    [Fact]
    public void ArtifactGuardRejectsCredentialValue()
    {
        Assert.Throws<InvalidOperationException>(() =>
            VerificationArtifactWriter.AssertDoesNotContainSecrets(
                "{\"playerToken\":193746281}",
                ["193746281"]));
    }

    [Fact]
    public void CommandLineDoesNotAcceptCredentialArguments()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            VerificationCommandOptions.Parse(["--live", "--player-token", "193746281"]));

        Assert.Contains("Unknown argument", exception.Message, StringComparison.Ordinal);
    }
}
