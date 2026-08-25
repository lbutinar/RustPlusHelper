namespace RustPlusHelper.Application.RustPlus;

/// <summary>
/// Application-owned Rust+ boundary. No third-party protocol type may appear in this contract.
/// </summary>
public interface IRustPlusClient : IAsyncDisposable
{
    bool IsConnected { get; }

    /// <summary>Raised for every rendered camera frame while a camera subscription is active.</summary>
    event EventHandler<CameraFrameSnapshot>? CameraFrameReceived;

    /// <summary>Raised when the camera subscription's keep-alive renewal fails (e.g. the camera
    /// entity was destroyed, or the connection dropped). The subscription is effectively dead
    /// after this fires; frames stop arriving until a new <see cref="SubscribeToCameraAsync"/>.</summary>
    event EventHandler<RustPlusError>? CameraSubscriptionFailed;

    Task ConnectAsync(RustPlusConnectionOptions options, CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task<RustPlusResult<ServerInfoSnapshot>> GetServerInfoAsync(CancellationToken cancellationToken = default);

    Task<RustPlusResult<ServerMapSnapshot>> GetMapAsync(CancellationToken cancellationToken = default);

    Task<RustPlusResult<TeamSnapshot>> GetTeamAsync(CancellationToken cancellationToken = default);

    Task<RustPlusResult<TeamChatSnapshot>> GetTeamChatAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends a message to team chat. Returns the message Rust+ echoes back (including its
    /// assigned timestamp), so callers can show it immediately without waiting for the next poll.</summary>
    Task<RustPlusResult<TeamChatMessageSnapshot>> SendTeamMessageAsync(string message, CancellationToken cancellationToken = default);

    Task<RustPlusResult<MapMarkersSnapshot>> GetMapMarkersAsync(CancellationToken cancellationToken = default);

    /// <summary>Subscribes to a camera's ray stream, replacing any previous subscription (the
    /// server tracks only one camera subscription per connection).</summary>
    Task<RustPlusResult<CameraInfoSnapshot>> SubscribeToCameraAsync(string cameraId, CancellationToken cancellationToken = default);

    /// <summary>Advances a PTZ camera's zoom by one step. Refused client-side (nothing sent) when
    /// the subscribed camera is not a PTZ camera.</summary>
    Task<RustPlusResult<bool>> ZoomCameraAsync(CancellationToken cancellationToken = default);

    /// <summary>Fires an auto-turret once. Refused client-side when the subscribed camera is not
    /// an auto-turret.</summary>
    Task<RustPlusResult<bool>> ShootCameraAsync(CancellationToken cancellationToken = default);

    /// <summary>Reloads an auto-turret. Refused client-side when the subscribed camera is not an
    /// auto-turret.</summary>
    Task<RustPlusResult<bool>> ReloadCameraAsync(CancellationToken cancellationToken = default);

    /// <summary>Turns the camera by a mouse-look delta. Refused client-side when the subscribed
    /// camera does not support mouse look.</summary>
    Task<RustPlusResult<bool>> LookCameraAsync(float deltaX, float deltaY, CancellationToken cancellationToken = default);

    /// <summary>Nudges a drone one step in <paramref name="direction"/>. Refused client-side when
    /// the subscribed camera does not support the required movement flag.</summary>
    Task<RustPlusResult<bool>> MoveCameraAsync(CameraMoveDirection direction, CancellationToken cancellationToken = default);

    /// <summary>Ends the current camera subscription, if any.</summary>
    Task UnsubscribeFromCameraAsync(CancellationToken cancellationToken = default);

    /// <summary>Raised whenever a paired entity's state changes. Carries no entity type of its
    /// own — the caller must already know the entity's paired kind to interpret the payload.</summary>
    event EventHandler<EntityStateChangedSnapshot>? EntityStateChanged;

    /// <summary>Reads a Smart Switch's current value. Also arms this entity's
    /// <see cref="EntityStateChanged"/> broadcasts going forward (a verified Rust+ behavior — see
    /// docs/protocol-evidence.md).</summary>
    Task<RustPlusResult<SmartDeviceStateSnapshot>> GetSmartSwitchInfoAsync(ulong entityId, CancellationToken cancellationToken = default);

    /// <summary>Reads a Smart Alarm's current value (arms broadcasts, same as
    /// <see cref="GetSmartSwitchInfoAsync"/>). This is the live signal state only — the alarm's
    /// triggered message/title arrives over a separate FCM channel, not this call.</summary>
    Task<RustPlusResult<SmartDeviceStateSnapshot>> GetAlarmInfoAsync(ulong entityId, CancellationToken cancellationToken = default);

    /// <summary>Reads a Storage Monitor's capacity/protection/contents (arms broadcasts, same as
    /// <see cref="GetSmartSwitchInfoAsync"/>).</summary>
    Task<RustPlusResult<StorageMonitorStateSnapshot>> GetStorageMonitorInfoAsync(ulong entityId, CancellationToken cancellationToken = default);

    /// <summary>Sets a Smart Switch's value. Returns the resulting state directly (the verified
    /// Rust+ response carries it back), so callers never need a separate re-read.</summary>
    Task<RustPlusResult<SmartDeviceStateSnapshot>> SetSmartSwitchValueAsync(ulong entityId, bool value, CancellationToken cancellationToken = default);

    Task<RustPlusResult<SmartDeviceStateSnapshot>> ToggleSmartSwitchAsync(ulong entityId, CancellationToken cancellationToken = default);

    /// <summary>Rapidly toggles a Smart Switch for <paramref name="duration"/>.</summary>
    Task<RustPlusResult<SmartDeviceStateSnapshot>> StrobeSmartSwitchAsync(ulong entityId, TimeSpan duration, bool value, CancellationToken cancellationToken = default);
}
