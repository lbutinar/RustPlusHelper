namespace RustPlusHelper.Application.Security;

public enum ApplicationSecretKind
{
    RustPlusFcmCredentials = 1
}

public interface IApplicationSecretStore
{
    void Store(ApplicationSecretKind kind, ReadOnlySpan<byte> secret);

    bool Contains(ApplicationSecretKind kind);

    /// <summary>Returns a caller-owned cleartext buffer that should be zeroed after use.</summary>
    byte[]? Retrieve(ApplicationSecretKind kind);

    bool Delete(ApplicationSecretKind kind);
}
