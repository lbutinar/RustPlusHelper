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
        var credentials = await registration.AcquireCredentialsAsync(cancellationToken).ConfigureAwait(false);
        _ = await registration.RegisterWithRustPlusAsync(credentials, cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetBytes(CredentialsStore.Serialize(credentials));
    }

    /// <summary>
    /// Bypasses <see cref="PairingListener.WaitForServerPairingAsync"/>: that convenience wrapper's
    /// <c>ServerPairing</c> result only carries <c>Ip/Port/PlayerId/PlayerToken/Name</c>, dropping the
    /// Rust+ server's own GUID (<see cref="ServerEvent.Id"/>) that alarm-triggered pushes key off of
    /// (see docs/protocol-evidence.md). Talks to <see cref="RustPlusFcm.OnServerPairing"/> directly
    /// instead, mirroring <see cref="WaitForEntityPairingAsync"/>'s already-established pattern below.
    /// </summary>
    public Task<CapturedRustPlusPairing> WaitForServerPairingAsync(
        ReadOnlyMemory<byte> credentials,
        CancellationToken cancellationToken = default) =>
        FcmSessionRunner.RunAsync<CapturedRustPlusPairing>(
            credentials,
            seedPersistentIds: null,
            (fcm, completion) =>
            {
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

                fcm.OnServerPairing += OnServerPairing;
                fcm.ErrorOccurred += OnError;
                return () =>
                {
                    fcm.OnServerPairing -= OnServerPairing;
                    fcm.ErrorOccurred -= OnError;
                };
            },
            cancellationToken);

    /// <summary>
    /// Mirrors <see cref="PairingListener.WaitForServerPairingAsync"/>'s own implementation exactly
    /// (fetched from source at the pinned tag to confirm the pattern) since there is no
    /// entity-pairing equivalent of that convenience wrapper: re-check-in the device before every
    /// MCS connect so the push routes correctly, connect, and wait for the first entity-pairing
    /// notification via the lower-level <see cref="RustPlusFcm.OnEntityPairing"/> event.
    /// </summary>
    public Task<CapturedEntityPairing> WaitForEntityPairingAsync(
        ReadOnlyMemory<byte> credentials,
        CancellationToken cancellationToken = default) =>
        FcmSessionRunner.RunAsync<CapturedEntityPairing>(
            credentials,
            seedPersistentIds: null,
            (fcm, completion) =>
            {
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

                fcm.OnEntityPairing += OnEntityPairing;
                fcm.ErrorOccurred += OnError;
                return () =>
                {
                    fcm.OnEntityPairing -= OnEntityPairing;
                    fcm.ErrorOccurred -= OnError;
                };
            },
            cancellationToken);
}
