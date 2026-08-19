using System.Security.Cryptography;
using System.Text;
using RustPlusApi.Fcm;
using RustPlusApi.Fcm.Data.Events;
using RustPlusApi.Fcm.Registration;
using RustPlusApi.Fcm.Registration.Steps;
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

    /// <summary>
    /// Mirrors <see cref="PairingListener.WaitForServerPairingAsync"/>'s own implementation exactly
    /// (fetched from source at the pinned tag to confirm the pattern) since there is no
    /// entity-pairing equivalent of that convenience wrapper: re-check-in the device before every
    /// MCS connect so the push routes correctly, connect, and wait for the first entity-pairing
    /// notification via the lower-level <see cref="RustPlusFcm.OnEntityPairing"/> event.
    /// </summary>
    public async Task<CapturedEntityPairing> WaitForEntityPairingAsync(
        ReadOnlyMemory<byte> credentials,
        CancellationToken cancellationToken = default)
    {
        var serialized = Encoding.UTF8.GetString(credentials.Span);
        var parsed = CredentialsStore.Deserialize(serialized);
        using var fcm = new RustPlusFcm(parsed);
        var androidFcmRegister = new AndroidFcmRegister(null);
        var completion = new TaskCompletionSource<CapturedEntityPairing>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void OnEntityPairing(object? sender, RustPlusApi.Fcm.Data.Notification<EntityEvent?> notification)
        {
            if (notification.Data is not { } entity
                || entity.EntityType is not { } entityType
                || entity.EntityId is not { } entityId)
            {
                return;
            }

            completion.TrySetResult(new CapturedEntityPairing(
                notification.PlayerId,
                notification.PlayerToken,
                entityId,
                (PairedEntityKind)(int)entityType,
                entity.EntityName));
        }

        void OnError(object? sender, Exception exception) => completion.TrySetException(exception);

        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));

        fcm.OnEntityPairing += OnEntityPairing;
        fcm.ErrorOccurred += OnError;
        try
        {
            await androidFcmRegister.CheckInAsync(parsed.Gcm, cancellationToken).ConfigureAwait(false);
            await fcm.ConnectAsync(cancellationToken).ConfigureAwait(false);
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            fcm.OnEntityPairing -= OnEntityPairing;
            fcm.ErrorOccurred -= OnError;
            fcm.Disconnect();
        }
    }
}
