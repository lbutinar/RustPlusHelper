using System.Security.Cryptography;
using RustPlusHelper.Application.Security;

namespace RustPlusHelper.Application.Testing;

public sealed class InMemoryApplicationSecretStore : IApplicationSecretStore, IDisposable
{
    private readonly Dictionary<ApplicationSecretKind, byte[]> _secrets = [];

    public void Store(ApplicationSecretKind kind, ReadOnlySpan<byte> secret)
    {
        Delete(kind);
        _secrets[kind] = secret.ToArray();
    }

    public bool Contains(ApplicationSecretKind kind) => _secrets.ContainsKey(kind);

    public byte[]? Retrieve(ApplicationSecretKind kind) =>
        _secrets.TryGetValue(kind, out var secret) ? secret.ToArray() : null;

    public bool Delete(ApplicationSecretKind kind)
    {
        if (!_secrets.Remove(kind, out var secret))
        {
            return false;
        }

        CryptographicOperations.ZeroMemory(secret);
        return true;
    }

    public void Dispose()
    {
        foreach (var secret in _secrets.Values)
        {
            CryptographicOperations.ZeroMemory(secret);
        }

        _secrets.Clear();
    }
}
