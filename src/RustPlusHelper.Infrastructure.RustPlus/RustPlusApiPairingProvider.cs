using System.Security.Cryptography;
using System.Text;
using RustPlusApi.Fcm.Registration;
using RustPlusHelper.Application.Pairing;

namespace RustPlusHelper.Infrastructure.RustPlus;

public sealed class RustPlusApiPairingProvider : IRustPlusPairingProvider
{
    public async Task<byte[]> RegisterAsync(CancellationToken cancellationToken = default)
    {
        var registration = new FcmRegistration();
        var credentials = await registration.AcquireCredentialsAsync(cancellationToken);
        _ = await registration.RegisterWithRustPlusAsync(credentials, cancellationToken);
        return Encoding.UTF8.GetBytes(CredentialsStore.Serialize(credentials));
    }

    public async Task<CapturedRustPlusPairing> WaitForServerPairingAsync(
        ReadOnlyMemory<byte> credentials,
        CancellationToken cancellationToken = default)
    {
        var serialized = Encoding.UTF8.GetString(credentials.Span);
        var parsed = CredentialsStore.Deserialize(serialized);
        using var listener = new PairingListener(parsed);
        var pairing = await listener.WaitForServerPairingAsync(cancellationToken);
        return new(
            pairing.Ip,
            pairing.Port,
            pairing.PlayerId,
            pairing.PlayerToken,
            pairing.Name);
    }
}
