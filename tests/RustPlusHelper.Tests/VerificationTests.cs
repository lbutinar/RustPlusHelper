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
    public async Task ReportIsUnsuccessfulWhenAReadOnlyRequestFails()
    {
        await using var inner = new FakeRustPlusClient();
        await using var client = new TeamRequestFailingClient(inner);
        var connection = new RustPlusConnectionOptions("fake.invalid", 28082, ulong.MaxValue - 42, 193746281);
        var result = await new VerificationRunner(client).RunAsync(connection);

        Assert.False(result.Report.Success);
        Assert.False(result.Report.Requests["teamInfo"].Success);
        Assert.Equal("simulated_failure", result.Report.Requests["teamInfo"].ErrorCode);
        Assert.True(result.Report.Requests["serverInfo"].Success);
        Assert.True(result.Report.Requests["map"].Success);
        Assert.True(result.Report.Requests["teamChat"].Success);
        Assert.True(result.Report.Requests["mapMarkers"].Success);
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

    /// <summary>
    /// Wraps a real <see cref="IRustPlusClient"/> and forces <see cref="GetTeamAsync"/> to fail, so
    /// tests can exercise VerificationRunner's failure/partial-failure aggregation path — the
    /// deterministic <see cref="FakeRustPlusClient"/> alone always succeeds and can't reach it.
    /// </summary>
    private sealed class TeamRequestFailingClient(IRustPlusClient inner) : IRustPlusClient
    {
        public bool IsConnected => inner.IsConnected;

        public event EventHandler<CameraFrameSnapshot>? CameraFrameReceived
        {
            add => inner.CameraFrameReceived += value;
            remove => inner.CameraFrameReceived -= value;
        }

        public event EventHandler<RustPlusError>? CameraSubscriptionFailed
        {
            add => inner.CameraSubscriptionFailed += value;
            remove => inner.CameraSubscriptionFailed -= value;
        }

        public event EventHandler<EntityStateChangedSnapshot>? EntityStateChanged
        {
            add => inner.EntityStateChanged += value;
            remove => inner.EntityStateChanged -= value;
        }

        public Task ConnectAsync(RustPlusConnectionOptions options, CancellationToken cancellationToken = default) =>
            inner.ConnectAsync(options, cancellationToken);

        public Task DisconnectAsync(CancellationToken cancellationToken = default) =>
            inner.DisconnectAsync(cancellationToken);

        public Task<RustPlusResult<ServerInfoSnapshot>> GetServerInfoAsync(CancellationToken cancellationToken = default) =>
            inner.GetServerInfoAsync(cancellationToken);

        public Task<RustPlusResult<ServerMapSnapshot>> GetMapAsync(CancellationToken cancellationToken = default) =>
            inner.GetMapAsync(cancellationToken);

        public Task<RustPlusResult<TeamSnapshot>> GetTeamAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(RustPlusResult<TeamSnapshot>.Failure("simulated_failure", "Injected failure for testing."));

        public Task<RustPlusResult<TeamChatSnapshot>> GetTeamChatAsync(CancellationToken cancellationToken = default) =>
            inner.GetTeamChatAsync(cancellationToken);

        public Task<RustPlusResult<MapMarkersSnapshot>> GetMapMarkersAsync(CancellationToken cancellationToken = default) =>
            inner.GetMapMarkersAsync(cancellationToken);

        public Task<RustPlusResult<CameraInfoSnapshot>> SubscribeToCameraAsync(
            string cameraId, CancellationToken cancellationToken = default) =>
            inner.SubscribeToCameraAsync(cameraId, cancellationToken);

        public Task<RustPlusResult<bool>> ZoomCameraAsync(CancellationToken cancellationToken = default) =>
            inner.ZoomCameraAsync(cancellationToken);

        public Task<RustPlusResult<bool>> ShootCameraAsync(CancellationToken cancellationToken = default) =>
            inner.ShootCameraAsync(cancellationToken);

        public Task<RustPlusResult<bool>> ReloadCameraAsync(CancellationToken cancellationToken = default) =>
            inner.ReloadCameraAsync(cancellationToken);

        public Task<RustPlusResult<bool>> LookCameraAsync(
            float deltaX, float deltaY, CancellationToken cancellationToken = default) =>
            inner.LookCameraAsync(deltaX, deltaY, cancellationToken);

        public Task<RustPlusResult<bool>> MoveCameraAsync(
            CameraMoveDirection direction, CancellationToken cancellationToken = default) =>
            inner.MoveCameraAsync(direction, cancellationToken);

        public Task UnsubscribeFromCameraAsync(CancellationToken cancellationToken = default) =>
            inner.UnsubscribeFromCameraAsync(cancellationToken);

        public Task<RustPlusResult<SmartDeviceStateSnapshot>> GetSmartSwitchInfoAsync(
            ulong entityId, CancellationToken cancellationToken = default) =>
            inner.GetSmartSwitchInfoAsync(entityId, cancellationToken);

        public Task<RustPlusResult<SmartDeviceStateSnapshot>> GetAlarmInfoAsync(
            ulong entityId, CancellationToken cancellationToken = default) =>
            inner.GetAlarmInfoAsync(entityId, cancellationToken);

        public Task<RustPlusResult<StorageMonitorStateSnapshot>> GetStorageMonitorInfoAsync(
            ulong entityId, CancellationToken cancellationToken = default) =>
            inner.GetStorageMonitorInfoAsync(entityId, cancellationToken);

        public Task<RustPlusResult<SmartDeviceStateSnapshot>> SetSmartSwitchValueAsync(
            ulong entityId, bool value, CancellationToken cancellationToken = default) =>
            inner.SetSmartSwitchValueAsync(entityId, value, cancellationToken);

        public Task<RustPlusResult<SmartDeviceStateSnapshot>> ToggleSmartSwitchAsync(
            ulong entityId, CancellationToken cancellationToken = default) =>
            inner.ToggleSmartSwitchAsync(entityId, cancellationToken);

        public Task<RustPlusResult<SmartDeviceStateSnapshot>> StrobeSmartSwitchAsync(
            ulong entityId, TimeSpan duration, bool value, CancellationToken cancellationToken = default) =>
            inner.StrobeSmartSwitchAsync(entityId, duration, value, cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
