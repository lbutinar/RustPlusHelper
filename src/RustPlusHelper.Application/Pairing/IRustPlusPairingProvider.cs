namespace RustPlusHelper.Application.Pairing;

public sealed record CapturedRustPlusPairing(
    string Host,
    int Port,
    ulong PlayerId,
    int PlayerToken,
    string? ServerName);

/// <summary>Application-owned boundary for Rust+ device registration and server pairing.</summary>
public interface IRustPlusPairingProvider
{
    /// <summary>Returns caller-owned serialized credentials that must be protected and zeroed.</summary>
    Task<byte[]> RegisterAsync(CancellationToken cancellationToken = default);

    Task<CapturedRustPlusPairing> WaitForServerPairingAsync(
        ReadOnlyMemory<byte> credentials,
        CancellationToken cancellationToken = default);
}
