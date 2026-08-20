namespace RustPlusHelper.Application.Diagnostics;

public enum HealthStatus
{
    Healthy,
    Unhealthy
}

public sealed record HealthCheckResult(string Name, HealthStatus Status, string Detail);

public interface IHealthCheck
{
    string Name { get; }

    HealthCheckResult Check();
}
