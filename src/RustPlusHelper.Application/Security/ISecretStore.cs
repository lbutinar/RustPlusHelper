namespace RustPlusHelper.Application.Security;

public enum SecretKind
{
    RustPlusPlayerToken = 1
}

public interface ISecretStore
{
    void Store(Guid serverId, SecretKind kind, ReadOnlySpan<byte> secret);

    bool Contains(Guid serverId, SecretKind kind);

    /// <summary>Returns a caller-owned cleartext buffer that should be zeroed after use.</summary>
    byte[]? Retrieve(Guid serverId, SecretKind kind);

    bool Delete(Guid serverId, SecretKind kind);
}

public interface ISecretProtector
{
    byte[] Protect(ReadOnlySpan<byte> cleartext, ReadOnlySpan<byte> context);

    byte[] Unprotect(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> context);
}
