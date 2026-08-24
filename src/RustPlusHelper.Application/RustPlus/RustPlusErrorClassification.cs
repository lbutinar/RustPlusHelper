namespace RustPlusHelper.Application.RustPlus;

/// <summary>
/// Shared classification of Rust+ protocol error codes, used identically by
/// <see cref="RustPlusConnectionManager"/> and <see cref="RustPlusLiveSessionManager"/> so a future
/// correction or alias addition only has to be made in one place.
/// </summary>
internal static class RustPlusErrorClassification
{
    public static bool IsAccessDenied(string? code) =>
        code is not null
        && (code.Equals("AccessDenied", StringComparison.OrdinalIgnoreCase)
            || code.Equals("access_denied", StringComparison.OrdinalIgnoreCase));
}
