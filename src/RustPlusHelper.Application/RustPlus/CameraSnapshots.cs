namespace RustPlusHelper.Application.RustPlus;

/// <summary>
/// Camera description returned on subscribe. The four booleans mirror RustPlusApi.Camera's own
/// <c>CameraController</c> capability checks (read directly off the live controller, not
/// re-derived), so the UI never has to guess which controls a camera supports.
/// </summary>
public sealed record CameraInfoSnapshot(
    int Width,
    int Height,
    float NearPlane,
    float FarPlane,
    bool IsStaticCamera,
    bool IsPtzCamera,
    bool IsAutoTurret,
    bool IsDrone);

/// <summary>
/// A rendered camera frame. <see cref="PngImage"/> is already a complete, ready-to-display image —
/// the ray decode/rasterize happens once, inside the adapter, using RustPlusApi.Camera's renderer.
/// </summary>
public sealed record CameraFrameSnapshot(byte[] PngImage, float VerticalFov, DateTimeOffset ReceivedAtUtc);

/// <summary>
/// Discrete nudge directions for <c>MoveCameraAsync</c>. Ascend/Descend map to a drone's vertical
/// controls (Sprint/Duck in the raw Rust+ protocol) rather than a literal jump/duck action.
/// </summary>
public enum CameraMoveDirection
{
    Forward,
    Backward,
    Left,
    Right,
    Ascend,
    Descend
}
