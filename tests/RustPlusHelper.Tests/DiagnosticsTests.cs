using RustPlusHelper.Application.Diagnostics;
using RustPlusHelper.Application.Security;

namespace RustPlusHelper.Tests;

public sealed class DiagnosticsTests
{
    [Fact]
    public void SecretProtectorHealthCheckIsHealthyWhenRoundTripSucceeds()
    {
        var check = new SecretProtectorHealthCheck(new RoundTrippingProtector());

        var result = check.Check();

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public void SecretProtectorHealthCheckIsUnhealthyWhenProtectorThrows()
    {
        var check = new SecretProtectorHealthCheck(new ThrowingProtector());

        var result = check.Check();

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("Protect/unprotect failed", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void SecretProtectorHealthCheckIsUnhealthyWhenRoundTrippedValueDiffers()
    {
        var check = new SecretProtectorHealthCheck(new MismatchingProtector());

        var result = check.Check();

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    private sealed class RoundTrippingProtector : ISecretProtector
    {
        public byte[] Protect(ReadOnlySpan<byte> cleartext, ReadOnlySpan<byte> context) => cleartext.ToArray();

        public byte[] Unprotect(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> context) => ciphertext.ToArray();
    }

    private sealed class ThrowingProtector : ISecretProtector
    {
        public byte[] Protect(ReadOnlySpan<byte> cleartext, ReadOnlySpan<byte> context) =>
            throw new InvalidOperationException("DPAPI unavailable.");

        public byte[] Unprotect(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> context) =>
            throw new InvalidOperationException("DPAPI unavailable.");
    }

    private sealed class MismatchingProtector : ISecretProtector
    {
        public byte[] Protect(ReadOnlySpan<byte> cleartext, ReadOnlySpan<byte> context) => cleartext.ToArray();

        public byte[] Unprotect(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> context) => [0];
    }
}
