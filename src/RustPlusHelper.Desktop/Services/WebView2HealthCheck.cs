using RustPlusHelper.Application.Diagnostics;

namespace RustPlusHelper.Desktop.Services;

public sealed class WebView2HealthCheck : IHealthCheck
{
    public string Name => "WebView2 runtime";

    public HealthCheckResult Check()
    {
        try
        {
            var version = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
            return string.IsNullOrWhiteSpace(version)
                ? new HealthCheckResult(Name, HealthStatus.Unhealthy, "No WebView2 runtime was detected.")
                : new HealthCheckResult(Name, HealthStatus.Healthy, $"Detected version {version}.");
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(Name, HealthStatus.Unhealthy, $"WebView2 runtime is not available: {ex.Message}");
        }
    }
}
