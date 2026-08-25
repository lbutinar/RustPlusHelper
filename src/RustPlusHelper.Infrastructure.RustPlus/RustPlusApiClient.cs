using RustPlusApi.Camera;
using RustPlusApi.Data;
using RustPlusApi.Data.Cameras;
using RustPlusApi.Data.Events;
using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Application.Security;
using ApiClient = RustPlusApi.RustPlus;
using ApiConnection = RustPlusApi.RustPlusConnection;
using ApiInterface = RustPlusApi.Interfaces.IRustPlus;

namespace RustPlusHelper.Infrastructure.RustPlus;

/// <summary>
/// Adapter around HandyS11/RustPlusApi. This is the only production class allowed to create the
/// third-party client; callers receive only application-owned snapshots.
/// </summary>
public sealed class RustPlusApiClient : IRustPlusClient
{
    private ApiInterface? _client;
    private string? _tokenText;
    private CameraController? _cameraController;
    private CameraRenderer? _cameraRenderer;

    // Serializes every operation that reads or mutates _client/_cameraController/_cameraRenderer
    // together — Connect/Disconnect and every camera subscribe/unsubscribe/command — so a reconnect's
    // teardown can never run concurrently with a camera command using the object it is about to
    // dispose. Deliberately does NOT cover the plain data-fetch methods (GetServerInfoAsync etc.):
    // those only read _client and are meant to run concurrently with each other (see
    // RustPlusConnectionManager.LoadDashboardAsync's Task.WhenAll), so they keep the lock-free
    // best-effort snapshot they already had.
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    public bool IsConnected => _client?.IsConnected == true;

    public event EventHandler<CameraFrameSnapshot>? CameraFrameReceived;

    public event EventHandler<EntityStateChangedSnapshot>? EntityStateChanged;

    public event EventHandler<RustPlusError>? CameraSubscriptionFailed;

    public async Task ConnectAsync(RustPlusConnectionOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client is not null)
            {
                throw new InvalidOperationException("This Rust+ client already has an active connection lifecycle.");
            }

            _tokenText = options.PlayerToken.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var client = new ApiClient(new ApiConnection(
                options.Server,
                options.Port,
                options.PlayerId,
                options.PlayerToken,
                options.UseFacepunchProxy));

            try
            {
                await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
                client.OnEntityChanged += HandleEntityChanged;
                _client = client;
            }
            catch (OperationCanceledException)
            {
                await client.DisposeAsync().ConfigureAwait(false);
                _tokenText = null;
                throw;
            }
            catch (Exception exception)
            {
                await client.DisposeAsync().ConfigureAwait(false);
                var safeMessage = SecretRedactor.Redact(exception.Message, _tokenText);
                _tokenText = null;
                throw new RustPlusConnectionException(
                    "websocket_connect_failed",
                    $"Rust+ connection failed: {safeMessage}");
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        // The gate wait itself must not be cancellable: cleanup must run even if the caller's token
        // is already cancelled (e.g. a shutdown path) — skipping it here would leak the camera
        // controller, event subscriptions, and the socket.
        await _lifecycleGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await TeardownCameraCoreAsync().ConfigureAwait(false);

            var client = _client;
            _client = null;
            _tokenText = null;

            if (client is null)
            {
                return;
            }

            client.OnEntityChanged -= HandleEntityChanged;
            try
            {
                if (client.IsConnected)
                {
                    await client.DisconnectAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public Task<RustPlusResult<ServerInfoSnapshot>> GetServerInfoAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            client => client.GetInfoAsync(cancellationToken),
            RustPlusApiMapper.Map,
            cancellationToken);

    public Task<RustPlusResult<ServerMapSnapshot>> GetMapAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            client => client.GetMapAsync(cancellationToken),
            RustPlusApiMapper.Map,
            cancellationToken);

    public Task<RustPlusResult<TeamSnapshot>> GetTeamAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            client => client.GetTeamInfoAsync(cancellationToken),
            RustPlusApiMapper.Map,
            cancellationToken);

    public Task<RustPlusResult<TeamChatSnapshot>> GetTeamChatAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            client => client.GetTeamChatAsync(cancellationToken),
            RustPlusApiMapper.Map,
            cancellationToken);

    public Task<RustPlusResult<TeamChatMessageSnapshot>> SendTeamMessageAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return ExecuteAsync(
            client => client.SendTeamMessageAsync(message, cancellationToken),
            RustPlusApiMapper.Map,
            cancellationToken);
    }

    public Task<RustPlusResult<MapMarkersSnapshot>> GetMapMarkersAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            client => client.GetMapMarkersAsync(cancellationToken),
            RustPlusApiMapper.Map,
            cancellationToken);

    public async Task<RustPlusResult<CameraInfoSnapshot>> SubscribeToCameraAsync(
        string cameraId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var client = _client;
            if (client?.IsConnected != true)
            {
                return RustPlusResult<CameraInfoSnapshot>.Failure("not_connected", "The Rust+ client is not connected.");
            }

            // The server tracks only one camera subscription per connection.
            await TeardownCameraCoreAsync().ConfigureAwait(false);

            try
            {
                var response = await CameraController.SubscribeAsync(client, cameraId, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (!response.IsSuccess || response.Data is null)
                {
                    var errorCode = response.Error?.Code ?? RustPlusErrorCode.Unknown;
                    var rawMessage = SecretRedactor.Redact(response.Error?.Message ?? "Rust+ returned no data.", _tokenText);
                    return RustPlusResult<CameraInfoSnapshot>.Failure(errorCode.ToString(), DescribeInitialSubscribeError(errorCode, rawMessage));
                }

                _cameraController = response.Data;
                _cameraRenderer = new CameraRenderer(_cameraController.Info.Width, _cameraController.Info.Height);
                _cameraController.OnFrameReceived += HandleCameraFrame;
                _cameraController.OnKeepAliveFailed += HandleCameraKeepAliveFailed;
                return RustPlusResult<CameraInfoSnapshot>.Success(RustPlusApiMapper.Map(_cameraController));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return RustPlusResult<CameraInfoSnapshot>.Failure(
                    "transport_exception",
                    SecretRedactor.Redact(exception.Message, _tokenText));
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public Task<RustPlusResult<bool>> ZoomCameraAsync(CancellationToken cancellationToken = default) =>
        ExecuteCameraCommandAsync(controller => controller.ZoomAsync(cancellationToken), cancellationToken);

    public Task<RustPlusResult<bool>> ShootCameraAsync(CancellationToken cancellationToken = default) =>
        ExecuteCameraCommandAsync(controller => controller.ShootAsync(cancellationToken), cancellationToken);

    public Task<RustPlusResult<bool>> ReloadCameraAsync(CancellationToken cancellationToken = default) =>
        ExecuteCameraCommandAsync(controller => controller.ReloadAsync(cancellationToken), cancellationToken);

    public Task<RustPlusResult<bool>> LookCameraAsync(
        float deltaX,
        float deltaY,
        CancellationToken cancellationToken = default) =>
        ExecuteCameraCommandAsync(controller => controller.LookAsync(deltaX, deltaY, cancellationToken), cancellationToken);

    public Task<RustPlusResult<bool>> MoveCameraAsync(
        CameraMoveDirection direction,
        CancellationToken cancellationToken = default) =>
        ExecuteCameraCommandAsync(
            controller => controller.MoveAsync(ToCameraButtons(direction), cancellationToken: cancellationToken),
            cancellationToken);

    public async Task UnsubscribeFromCameraAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await TeardownCameraCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public Task<RustPlusResult<SmartDeviceStateSnapshot>> GetSmartSwitchInfoAsync(
        ulong entityId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            client => client.GetSmartSwitchInfoAsync(entityId, cancellationToken),
            data => RustPlusApiMapper.Map(entityId, data),
            cancellationToken);

    public Task<RustPlusResult<SmartDeviceStateSnapshot>> GetAlarmInfoAsync(
        ulong entityId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            client => client.GetAlarmInfoAsync(entityId, cancellationToken),
            data => RustPlusApiMapper.Map(entityId, data),
            cancellationToken);

    public Task<RustPlusResult<StorageMonitorStateSnapshot>> GetStorageMonitorInfoAsync(
        ulong entityId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            client => client.GetStorageMonitorInfoAsync(entityId, cancellationToken),
            data => RustPlusApiMapper.Map(entityId, data),
            cancellationToken);

    public Task<RustPlusResult<SmartDeviceStateSnapshot>> SetSmartSwitchValueAsync(
        ulong entityId,
        bool value,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            client => client.SetSmartSwitchValueAsync(entityId, value, cancellationToken),
            data => RustPlusApiMapper.Map(entityId, data),
            cancellationToken);

    public Task<RustPlusResult<SmartDeviceStateSnapshot>> ToggleSmartSwitchAsync(
        ulong entityId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            client => client.ToggleSmartSwitchAsync(entityId, cancellationToken),
            data => RustPlusApiMapper.Map(entityId, data),
            cancellationToken);

    public Task<RustPlusResult<SmartDeviceStateSnapshot>> StrobeSmartSwitchAsync(
        ulong entityId,
        TimeSpan duration,
        bool value,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            client => client.StrobeSmartSwitchAsync(entityId, (int)duration.TotalMilliseconds, value, cancellationToken),
            data => RustPlusApiMapper.Map(entityId, data),
            cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _lifecycleGate.Dispose();
    }

    private Task<RustPlusResult<TSnapshot>> ExecuteAsync<TApi, TSnapshot>(
        Func<ApiInterface, Task<Response<TApi?>>> operation,
        Func<TApi, TSnapshot> mapper,
        CancellationToken cancellationToken)
        where TApi : class
    {
        cancellationToken.ThrowIfCancellationRequested();

        var client = _client;
        if (client?.IsConnected != true)
        {
            return Task.FromResult(
                RustPlusResult<TSnapshot>.Failure("not_connected", "The Rust+ client is not connected."));
        }

        return GuardTransportExceptionsAsync(async () =>
        {
            var response = await operation(client).ConfigureAwait(false);
            if (!response.IsSuccess || response.Data is null)
            {
                var code = response.Error?.Code.ToString() ?? "unknown_error";
                var message = SecretRedactor.Redact(response.Error?.Message ?? "Rust+ returned no data.", _tokenText);
                return RustPlusResult<TSnapshot>.Failure(code, message);
            }

            return RustPlusResult<TSnapshot>.Success(mapper(response.Data));
        });
    }

    private async Task<RustPlusResult<bool>> ExecuteCameraCommandAsync(
        Func<CameraController, Task<Response>> operation,
        CancellationToken cancellationToken)
    {
        // Held for the whole command, not just the field read: this is what stops a concurrent
        // Disconnect/teardown from disposing the controller out from under an in-flight command
        // (rather than the controller being captured just before its null-check, then torn down a
        // moment later).
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var controller = _cameraController;
            if (controller is null)
            {
                return RustPlusResult<bool>.Failure("no_active_camera", "No camera subscription is active.");
            }

            return await GuardTransportExceptionsAsync(async () =>
            {
                var response = await operation(controller).ConfigureAwait(false);
                if (!response.IsSuccess)
                {
                    var code = response.Error?.Code.ToString() ?? "unknown_error";
                    var message = SecretRedactor.Redact(response.Error?.Message ?? "Rust+ returned no data.", _tokenText);
                    return RustPlusResult<bool>.Failure(code, message);
                }

                return RustPlusResult<bool>.Success(true);
            }).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Shared exception-to-result boundary for every Rust+ call: lets <see cref="OperationCanceledException"/>
    /// propagate, and maps anything else to a redacted "transport_exception" failure instead of throwing.
    /// </summary>
    private async Task<RustPlusResult<T>> GuardTransportExceptionsAsync<T>(Func<Task<RustPlusResult<T>>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return RustPlusResult<T>.Failure(
                "transport_exception",
                SecretRedactor.Redact(exception.Message, _tokenText));
        }
    }

    private static CameraButtons ToCameraButtons(CameraMoveDirection direction) => direction switch
    {
        CameraMoveDirection.Forward => CameraButtons.Forward,
        CameraMoveDirection.Backward => CameraButtons.Backward,
        CameraMoveDirection.Left => CameraButtons.Left,
        CameraMoveDirection.Right => CameraButtons.Right,
        CameraMoveDirection.Ascend => CameraButtons.Sprint,
        CameraMoveDirection.Descend => CameraButtons.Duck,
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
    };

    private void HandleCameraFrame(object? sender, CameraRaysEventArg frame)
    {
        var renderer = _cameraRenderer;
        if (renderer is null)
        {
            return;
        }

        renderer.AddRays(frame);
        var png = renderer.Render();
        CameraFrameReceived?.Invoke(this, new CameraFrameSnapshot(png, frame.VerticalFov, DateTimeOffset.UtcNow));
    }

    private void HandleCameraKeepAliveFailed(object? sender, ErrorMessage error)
    {
        // The server has already dropped this camera subscription — tear down our side so a
        // subsequent camera command fails fast with "no_active_camera" instead of issuing a doomed
        // round-trip against a session the server abandoned. Fire-and-forget: this is a sync event
        // handler, and the gated teardown will simply wait its turn behind any in-flight
        // command/subscribe/disconnect rather than racing it.
        _ = TeardownCameraGatedAsync();

        CameraSubscriptionFailed?.Invoke(
            this,
            new RustPlusError(
                error.Code.ToString(),
                DescribeCameraError(
                    error.Code,
                    SecretRedactor.Redact(error.Message ?? "Camera subscription renewal failed.", _tokenText))));
    }

    /// <summary>Overrides a handful of camera keep-alive-renewal error codes with an accurate
    /// explanation — most notably <see cref="RustPlusErrorCode.NoPlayer"/>, whose own doc comment in
    /// the pinned package flags it as confusingly named: it's observed specifically when a
    /// previously-subscribed camera entity was destroyed in-game between renewals, not anything about
    /// the paired player. See docs/protocol-evidence.md. Only valid for the renewal path — a camera
    /// that was successfully streaming before this failure really did exist a moment ago.</summary>
    private static string DescribeCameraError(RustPlusErrorCode code, string rawMessage) => code switch
    {
        RustPlusErrorCode.NoPlayer => "This camera no longer exists in Rust — it may have been destroyed.",
        _ => rawMessage
    };

    /// <summary>Describes a failure from the very first subscribe attempt to a camera code. Unlike
    /// <see cref="DescribeCameraError"/>, there's no prior successful subscribe to compare against, so
    /// <see cref="RustPlusErrorCode.NoPlayer"/> here can't be narrated as "it was destroyed" — that
    /// explanation is only documented for the renewal path. The same raw error also covers a
    /// never-existed or mistyped code, a monument the current map doesn't have, or (plausibly,
    /// unconfirmed) not currently being connected in-game on this server.</summary>
    private static string DescribeInitialSubscribeError(RustPlusErrorCode code, string rawMessage) => code switch
    {
        RustPlusErrorCode.NoPlayer =>
            "No camera found with that code. Double-check the spelling/case, confirm this map actually " +
            "has that camera, and make sure your character is currently online in this Rust server.",
        _ => rawMessage
    };

    private void HandleEntityChanged(object? sender, EntityChangedEventArg args) =>
        EntityStateChanged?.Invoke(this, RustPlusApiMapper.Map(args));

    private async Task TeardownCameraGatedAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await TeardownCameraCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Unlocked core of camera teardown — callers must already hold <see cref="_lifecycleGate"/>.
    /// </summary>
    private async Task TeardownCameraCoreAsync()
    {
        var controller = _cameraController;
        _cameraController = null;
        _cameraRenderer = null;
        if (controller is null)
        {
            return;
        }

        controller.OnFrameReceived -= HandleCameraFrame;
        controller.OnKeepAliveFailed -= HandleCameraKeepAliveFailed;
        await controller.DisposeAsync().ConfigureAwait(false);
    }
}
