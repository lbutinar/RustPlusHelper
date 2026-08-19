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

    /// <summary>
    /// Bypasses <see cref="PairingListener.WaitForServerPairingAsync"/>: that convenience wrapper's
    /// <c>ServerPairing</c> result only carries <c>Ip/Port/PlayerId/PlayerToken/Name</c>, dropping the
    /// Rust+ server's own GUID (<see cref="ServerEvent.Id"/>) that alarm-triggered pushes key off of
    /// (see docs/protocol-evidence.md). Talks to <see cref="RustPlusFcm.OnServerPairing"/> directly
    /// instead, mirroring <see cref="WaitForEntityPairingAsync"/>'s already-established pattern below.
    /// </summary>
    public async Task<CapturedRustPlusPairing> WaitForServerPairingAsync(
        ReadOnlyMemory<byte> credentials,
        CancellationToken cancellationToken = default)
    {
        var serialized = Encoding.UTF8.GetString(credentials.Span);
        var parsed = CredentialsStore.Deserialize(serialized);
        using var fcm = new RustPlusFcm(parsed);
        var androidFcmRegister = new AndroidFcmRegister(null);
        var completion = new TaskCompletionSource<CapturedRustPlusPairing>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void OnServerPairing(object? sender, RustPlusApi.Fcm.Data.Notification<ServerEvent?> notification)
        {
            if (notification.Data is not { } server)
            {
                return;
            }

            completion.TrySetResult(new CapturedRustPlusPairing(
                server.Ip,
                server.Port,
                notification.PlayerId,
                notification.PlayerToken,
                server.Name,
                server.Id));
        }

        void OnError(object? sender, Exception exception) => completion.TrySetException(exception);

        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));

        fcm.OnServerPairing += OnServerPairing;
        fcm.ErrorOccurred += OnError;
        try
        {
            await androidFcmRegister.CheckInAsync(parsed.Gcm, cancellationToken).ConfigureAwait(false);
            await fcm.ConnectAsync(cancellationToken).ConfigureAwait(false);
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            fcm.OnServerPairing -= OnServerPairing;
            fcm.ErrorOccurred -= OnError;
            fcm.Disconnect();
        }
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
