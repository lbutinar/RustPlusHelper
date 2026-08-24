using System.Security.Cryptography;
using RustPlusHelper.Application.Identity;
using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Application.Security;

namespace RustPlusHelper.Application.Pairing;

public enum EntityPairingStatus
{
    NotConfigured,
    Listening,
    Paired,
    Failed
}

public sealed record EntityPairingState(
    EntityPairingStatus Status,
    string Label,
    string? Detail = null,
    Guid? PairedEntityId = null)
{
    public static EntityPairingState NotConfigured { get; } = new(
        EntityPairingStatus.NotConfigured,
        "Automatic pairing is not set up.",
        "Register this PC on the Servers page first, then listen while you pair a device from Rust.");

    public static EntityPairingState Ready { get; } = new(
        EntityPairingStatus.NotConfigured,
        "Ready to listen for a device pairing.",
        "Pair a Smart Switch, Smart Alarm, or Storage Monitor from Rust.");
}

/// <summary>
/// Listens for the Smart Switch/Alarm/Storage Monitor pairing notification and persists it against
/// whichever saved server the caller is currently viewing. This is deliberately separate from
/// <see cref="RustPlusPairingManager"/> (server pairing): the entity-pairing notification carries no
/// host/port, only a player ID — the caller already knows which server this pairing belongs to. Both
/// managers share the one registered <see cref="ApplicationSecretKind.RustPlusFcmCredentials"/>; this
/// class never registers or resets it.
/// </summary>
public sealed class RustPlusEntityPairingManager(
    IRustPlusPairingProvider provider,
    IApplicationSecretStore credentialStore,
    PlayerIdentityManager identity,
    IPairedEntityRepository entities,
    TimeProvider timeProvider) : IDisposable
{
    private readonly CancellableOperationGate _operationGate = new();

    public event EventHandler? StateChanged;

    public EntityPairingState State { get; private set; } = EntityPairingState.NotConfigured;

    public bool IsConfigured => credentialStore.Contains(ApplicationSecretKind.RustPlusFcmCredentials);

    public void Load() => SetState(IsConfigured ? EntityPairingState.Ready : EntityPairingState.NotConfigured);

    public async Task ListenAsync(Guid serverId)
    {
        var credentials = credentialStore.Retrieve(ApplicationSecretKind.RustPlusFcmCredentials);
        if (credentials is null)
        {
            SetState(EntityPairingState.NotConfigured);
            return;
        }

        using var operation = BeginOperation(
            "Listening for a device pairing…",
            "Pair a Smart Switch, Smart Alarm, or Storage Monitor from Rust.");

        try
        {
            var capture = await provider.WaitForEntityPairingAsync(credentials, operation.Token)
                .ConfigureAwait(false);
            var saved = SaveCapture(serverId, capture);
            SetState(new(
                EntityPairingStatus.Paired,
                $"Saved {saved.Nickname}.",
                "The device is ready to monitor and control.",
                saved.Id));
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            SetState(new(EntityPairingStatus.NotConfigured, "Pairing listener cancelled.", EntityPairingState.Ready.Detail));
        }
        catch (EntityPairingValidationException exception)
        {
            SetState(new(EntityPairingStatus.Failed, "Pairing was not saved.", exception.Message));
        }
        catch
        {
            SetState(new(
                EntityPairingStatus.Failed,
                "The pairing notification could not be received.",
                "Try listening again, then pair from Rust. No pairing values were shown or logged."));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(credentials);
            EndOperation(operation);
        }
    }

    public void Cancel() => _operationGate.Cancel();

    public void Dispose() => _operationGate.Dispose();

    private PairedEntity SaveCapture(Guid serverId, CapturedEntityPairing capture)
    {
        if (capture.EntityId == 0)
        {
            throw new EntityPairingValidationException(
                "The pairing notification did not contain a valid entity ID.");
        }

        if (identity.Current is { } current && current.SteamId != capture.PlayerId)
        {
            throw new EntityPairingValidationException(
                "This pairing belongs to a different Steam account. Remove existing pairings before changing identity.");
        }

        var nickname = string.IsNullOrWhiteSpace(capture.EntityName)
            ? DefaultNickname(capture.Kind)
            : capture.EntityName.Trim();
        var entity = new PairedEntity(
            Guid.NewGuid(),
            serverId,
            capture.EntityId,
            capture.Kind,
            nickname,
            timeProvider.GetUtcNow());
        entities.Add(entity);
        return entity;
    }

    private static string DefaultNickname(PairedEntityKind kind) => kind switch
    {
        PairedEntityKind.Switch => "Smart Switch",
        PairedEntityKind.Alarm => "Smart Alarm",
        PairedEntityKind.StorageMonitor => "Storage Monitor",
        _ => "Paired device"
    };

    private CancellationTokenSource BeginOperation(string label, string detail) =>
        _operationGate.Begin(
            "A device pairing operation is already in progress.",
            () => SetState(new(EntityPairingStatus.Listening, label, detail)));

    private void EndOperation(CancellationTokenSource operation) => _operationGate.End(operation);

    private void SetState(EntityPairingState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class EntityPairingValidationException(string message) : Exception(message);
}
