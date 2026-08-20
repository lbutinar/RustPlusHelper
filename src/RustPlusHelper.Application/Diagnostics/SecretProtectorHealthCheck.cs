using System.Text;
using RustPlusHelper.Application.Security;

namespace RustPlusHelper.Application.Diagnostics;

/// <summary>Round-trips a throwaway value; never touches a real stored secret.</summary>
public sealed class SecretProtectorHealthCheck(ISecretProtector protector) : IHealthCheck
{
    private static readonly byte[] Probe = Encoding.UTF8.GetBytes("rustplushelper-diagnostics-probe");
    private static readonly byte[] Context = Encoding.UTF8.GetBytes("diagnostics-health-check");

    public string Name => "Secret protection (DPAPI)";

    public HealthCheckResult Check()
    {
        try
        {
            var protectedValue = protector.Protect(Probe, Context);
            var roundTripped = protector.Unprotect(protectedValue, Context);
            return roundTripped.AsSpan().SequenceEqual(Probe)
                ? new HealthCheckResult(Name, HealthStatus.Healthy, "Round-trip succeeded for the current Windows user.")
                : new HealthCheckResult(Name, HealthStatus.Unhealthy, "Round-tripped value did not match the original.");
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(Name, HealthStatus.Unhealthy, $"Protect/unprotect failed: {ex.Message}");
        }
    }
}
