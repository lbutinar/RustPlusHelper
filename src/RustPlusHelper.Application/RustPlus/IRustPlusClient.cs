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
}
