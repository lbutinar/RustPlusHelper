using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using RustPlusHelper.Application.Identity;
using RustPlusHelper.Application.Security;

namespace RustPlusHelper.Application.Servers;

public sealed class ServerManager(
    IServerRepository repository,
    TimeProvider timeProvider,
    ISecretStore? secretStore = null,
    PlayerIdentityManager? playerIdentity = null)
{
    private readonly object _stateLock = new();

    public event EventHandler? Changed;

    public IReadOnlyList<ServerProfile> Profiles { get; private set; } = [];

    public Guid? SelectedServerId => Profiles
        .Where(profile => profile.LastSelectedUtc is not null)
        .OrderByDescending(profile => profile.LastSelectedUtc)
        .Select(profile => (Guid?)profile.Id)
        .FirstOrDefault();

    public void Load()
    {
        lock (_stateLock)
        {
            Profiles = repository.GetAll();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public ServerProfile Save(ServerProfileDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var displayName = RequireText(draft.DisplayName, nameof(draft.DisplayName), 100);
        var host = RequireText(draft.Host, nameof(draft.Host), 255);
        if (draft.Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(draft.Port), "Port must be between 1 and 65535.");
        }

        if (draft.PlayerId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(draft.PlayerId), "Steam64 ID must be greater than zero.");
        }

        ServerProfile profile;
        lock (_stateLock)
        {
            var now = timeProvider.GetUtcNow();
            var id = draft.Id ?? Guid.NewGuid();
            var existing = Profiles.FirstOrDefault(candidate => candidate.Id == id) ?? repository.GetById(id);
            profile = new ServerProfile(
                id,
                displayName,
                host,
                draft.Port,
                draft.UseFacepunchProxy,
                draft.PlayerId,
                existing?.CreatedUtc ?? now,
                now,
                now,
                draft.RustPlusServerId ?? existing?.RustPlusServerId);

            repository.Upsert(profile);
            Profiles = repository.GetAll();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return profile;
    }

    public ServerProfile SaveWithPairing(ServerProfileDraft draft, ReadOnlySpan<char> playerToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var effectiveDraft = playerIdentity?.Current is { } identity
            ? draft with { PlayerId = identity.SteamId }
            : draft;
        var normalizedToken = playerToken.Trim();
        if (normalizedToken.IsEmpty)
        {
            EnsurePlayerChangeDoesNotInvalidatePairing(effectiveDraft);
            return Save(effectiveDraft);
        }

        if (effectiveDraft.PlayerId is null or 0)
        {
            throw new ArgumentException(
                "Steam64 ID is required. Save your player identity before adding a player token.",
                nameof(draft.PlayerId));
        }

        if (!int.TryParse(normalizedToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedToken))
        {
            throw new ArgumentException("Player token must be a signed 32-bit integer.", nameof(playerToken));
        }

        if (secretStore is null)
        {
            throw new InvalidOperationException("Protected pairing storage is not available.");
        }

        return SaveCapturedPairing(effectiveDraft, parsedToken);
    }

    public ServerProfile SaveCapturedPairing(ServerProfileDraft draft, int playerToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var effectiveDraft = playerIdentity?.Current is { } identity
            ? draft with { PlayerId = identity.SteamId }
            : draft;
        if (effectiveDraft.PlayerId is null or 0)
        {
            throw new ArgumentException("Steam64 ID is required for server pairing.", nameof(draft.PlayerId));
        }

        if (secretStore is null)
        {
            throw new InvalidOperationException("Protected pairing storage is not available.");
        }

        var profile = Save(effectiveDraft);
        Span<byte> tokenBytes = stackalloc byte[11];
        if (!Utf8Formatter.TryFormat(playerToken, tokenBytes, out var bytesWritten))
        {
            throw new InvalidOperationException("The player token could not be prepared for protected storage.");
        }

        try
        {
            secretStore.Store(profile.Id, SecretKind.RustPlusPlayerToken, tokenBytes[..bytesWritten]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tokenBytes);
        }

        return profile;
    }

    public bool HasPairing(Guid serverId) =>
        secretStore?.Contains(serverId, SecretKind.RustPlusPlayerToken) == true;

    public bool Select(Guid id)
    {
        lock (_stateLock)
        {
            var existing = Profiles.FirstOrDefault(profile => profile.Id == id) ?? repository.GetById(id);
            if (existing is null)
            {
                return false;
            }

            var now = timeProvider.GetUtcNow();
            repository.Upsert(existing with
            {
                UpdatedUtc = now,
                LastSelectedUtc = now
            });
            Profiles = repository.GetAll();
        }

        Changed?.Invoke(this, EventArgs.Empty);

        return true;
    }

    public bool Remove(Guid id)
    {
        bool removed;
        lock (_stateLock)
        {
            removed = repository.Remove(id);
            if (removed)
            {
                Profiles = repository.GetAll();
            }
        }

        if (removed)
        {
            secretStore?.Delete(id, SecretKind.RustPlusPlayerToken);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    private void EnsurePlayerChangeDoesNotInvalidatePairing(ServerProfileDraft draft)
    {
        if (draft.Id is not Guid id || !HasPairing(id))
        {
            return;
        }

        ServerProfile? existing;
        lock (_stateLock)
        {
            existing = Profiles.FirstOrDefault(profile => profile.Id == id) ?? repository.GetById(id);
        }

        if (existing is not null && existing.PlayerId != draft.PlayerId)
        {
            throw new ArgumentException(
                "Enter the player token again when changing the Steam64 ID.",
                nameof(draft.PlayerId));
        }
    }

    private static string RequireText(string value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maximumLength)
        {
            throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
        }

        return trimmed;
    }
}
