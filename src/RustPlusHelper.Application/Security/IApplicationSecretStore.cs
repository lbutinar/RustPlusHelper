namespace RustPlusHelper.Application.Security;

public enum ApplicationSecretKind
{
    RustPlusFcmCredentials = 1,

    /// <summary>Already-seen FCM persistent message IDs for the alarm-triggered listener's
    /// de-duplication (see <c>RustPlusApi.Fcm.RustPlusFcm</c>'s own <c>persistentIds</c> parameter) —
    /// not a secret, but reuses this store's existing single-row key/value shape rather than a new
    /// migration for one small blob.</summary>
    AlarmFcmPersistentIds = 2,

    /// <summary>Per-category desktop notification enable/disable toggles. Not a secret either, same
    /// pragmatic reuse as <see cref="AlarmFcmPersistentIds"/>.</summary>
    NotificationPreferences = 3
}

public interface IApplicationSecretStore
{
    void Store(ApplicationSecretKind kind, ReadOnlySpan<byte> secret);

    bool Contains(ApplicationSecretKind kind);

    /// <summary>Returns a caller-owned cleartext buffer that should be zeroed after use.</summary>
    byte[]? Retrieve(ApplicationSecretKind kind);

    bool Delete(ApplicationSecretKind kind);
}
