using System.Security.Cryptography;
using RustPlusHelper.Application.Identity;
using RustPlusHelper.Application.Security;
using RustPlusHelper.Application.Servers;

namespace RustPlusHelper.Application.Pairing;

public enum RustPlusPairingStatus
{
    NotConfigured,
    Ready,
    Registering,
    Listening,
    Paired,
    Failed
}

public sealed record RustPlusPairingState(
    RustPlusPairingStatus Status,
    string Label,
    string? Detail = null,
    Guid? SavedServerId = null)
{
    public static RustPlusPairingState NotConfigured { get; } = new(
        RustPlusPairingStatus.NotConfigured,
        "Automatic pairing is not set up.",
        "Register this PC once, then listen while you pair a server from Rust.");
}

public sealed class RustPlusPairingManager(
    IRustPlusPairingProvider provider,
    IApplicationSecretStore credentialStore,
    PlayerIdentityManager identity,
    ServerManager servers) : IDisposable
{
    private readonly object _stateLock = new();
    private CancellationTokenSource? _operationCancellation;
    private bool _isConfigured;

    public event EventHandler? StateChanged;

    public RustPlusPairingState State { get; private set; } = RustPlusPairingState.NotConfigured;

    public bool IsConfigured => _isConfigured;

    public void Load()
    {
        _isConfigured = credentialStore.Contains(ApplicationSecretKind.RustPlusFcmCredentials);
        SetState(IsConfigured ? ReadyState() : RustPlusPairingState.NotConfigured);
    }

    public async Task RegisterAsync()
    {
        using var operation = BeginOperation(
            RustPlusPairingStatus.Registering,
            "Waiting for Steam sign-in…",
            "Complete the secure Rust+ registration in the browser window.");

        byte[]? credentials = null;
        try
        {
            credentials = await provider.RegisterAsync(operation.Token);
            if (credentials.Length == 0)
            {
                throw new InvalidOperationException("Registration returned no credentials.");
            }

            credentialStore.Store(ApplicationSecretKind.RustPlusFcmCredentials, credentials);
            _isConfigured = true;
            SetState(ReadyState("This PC is registered. You can now listen for a server pairing."));
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            SetState(IsConfigured
                ? ReadyState("Registration cancelled.")
                : RustPlusPairingState.NotConfigured);
        }
        catch
        {
            SetState(new(
                RustPlusPairingStatus.Failed,
                "Automatic pairing setup failed.",
                "Try again. No registration secrets were shown or logged."));
        }
        finally
        {
            if (credentials is not null)
            {
                CryptographicOperations.ZeroMemory(credentials);
            }

            EndOperation(operation);
        }
    }

    public async Task ListenAsync()
    {
        var credentials = credentialStore.Retrieve(ApplicationSecretKind.RustPlusFcmCredentials);
        if (credentials is null)
        {
            _isConfigured = false;
            SetState(RustPlusPairingState.NotConfigured);
            return;
        }

        using var operation = BeginOperation(
            RustPlusPairingStatus.Listening,
            "Listening for Rust+ pairing…",
            "Join the server in Rust, open Rust+, and choose Pair with Server.");

        try
        {
            var capture = await provider.WaitForServerPairingAsync(credentials, operation.Token);
            var profile = SaveCapture(capture);
            SetState(new(
                RustPlusPairingStatus.Paired,
                $"Saved {profile.DisplayName}.",
                "The server address and protected player token are ready to use.",
                profile.Id));
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            SetState(ReadyState("Pairing listener cancelled."));
        }
        catch (PairingValidationException exception)
        {
            SetState(new(RustPlusPairingStatus.Failed, "Pairing was not saved.", exception.Message));
        }
        catch
        {
            SetState(new(
                RustPlusPairingStatus.Failed,
                "The pairing notification could not be received.",
                "Try listening again, then pair from Rust. No pairing values were shown or logged."));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(credentials);
            EndOperation(operation);
        }
    }

    public void Cancel()
    {
        lock (_stateLock)
        {
            _operationCancellation?.Cancel();
        }
    }

    public void ResetCredentials()
    {
        Cancel();
        credentialStore.Delete(ApplicationSecretKind.RustPlusFcmCredentials);
        _isConfigured = false;
        SetState(RustPlusPairingState.NotConfigured);
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            _operationCancellation?.Cancel();
            _operationCancellation?.Dispose();
            _operationCancellation = null;
        }
    }

    private ServerProfile SaveCapture(CapturedRustPlusPairing capture)
    {
        if (string.IsNullOrWhiteSpace(capture.Host)
            || capture.Port is < 1 or > 65535
            || capture.PlayerId == 0)
        {
            throw new PairingValidationException("The pairing notification did not contain a valid server address and player identity.");
        }

        if (identity.Current is { } current && current.SteamId != capture.PlayerId)
        {
            throw new PairingValidationException(
                "This pairing belongs to a different Steam account. Remove existing pairings before changing identity.");
        }

        if (identity.Current is null)
        {
            identity.Save(capture.PlayerId);
        }

        var existing = servers.Profiles.FirstOrDefault(profile =>
            profile.Port == capture.Port
            && string.Equals(profile.Host, capture.Host, StringComparison.OrdinalIgnoreCase));
        var displayName = string.IsNullOrWhiteSpace(capture.ServerName)
            ? existing?.DisplayName ?? "Rust+ server"
            : capture.ServerName.Trim();
        return servers.SaveCapturedPairing(new(
            existing?.Id,
            displayName,
            capture.Host,
            capture.Port,
            true,
            capture.PlayerId,
            capture.RustPlusServerId), capture.PlayerToken);
    }

    private CancellationTokenSource BeginOperation(
        RustPlusPairingStatus status,
        string label,
        string detail)
    {
        lock (_stateLock)
        {
            if (_operationCancellation is not null)
            {
                throw new InvalidOperationException("A Rust+ pairing operation is already in progress.");
            }

            _operationCancellation = new CancellationTokenSource();
            SetState(new(status, label, detail));
            return _operationCancellation;
        }
    }

    private void EndOperation(CancellationTokenSource operation)
    {
        lock (_stateLock)
        {
            if (ReferenceEquals(_operationCancellation, operation))
            {
                _operationCancellation = null;
            }
        }
    }

    private void SetState(RustPlusPairingState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static RustPlusPairingState ReadyState(string? detail = null) => new(
        RustPlusPairingStatus.Ready,
        "Automatic pairing is ready.",
        detail ?? "Listen for a pairing, then choose Pair with Server from Rust.");

    private sealed class PairingValidationException(string message) : Exception(message);
}
