namespace RustPlusHelper.Application.Servers;

public sealed class ServerManager(IServerRepository repository, TimeProvider timeProvider)
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
                now);

            repository.Upsert(profile);
            Profiles = repository.GetAll();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return profile;
    }

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
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return removed;
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
