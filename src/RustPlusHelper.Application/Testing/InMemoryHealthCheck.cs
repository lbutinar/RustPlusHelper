using RustPlusHelper.Application.Diagnostics;

namespace RustPlusHelper.Application.Testing;

public sealed class InMemoryHealthCheck(string name, HealthStatus status, string detail) : IHealthCheck
{
    public string Name { get; } = name;

    public HealthCheckResult Check() => new(Name, status, detail);
}
