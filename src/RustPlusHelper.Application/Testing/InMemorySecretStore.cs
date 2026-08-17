using System.Security.Cryptography;
using RustPlusHelper.Application.Security;

namespace RustPlusHelper.Application.Testing;

public sealed class InMemorySecretStore : ISecretStore, IDisposable
{
    private readonly Dictionary<(Guid ServerId, SecretKind Kind), byte[]> _secrets = [];

    public void Store(Guid serverId, SecretKind kind, ReadOnlySpan<byte> secret)
    {
        Delete(serverId, kind);
        _secrets[(serverId, kind)] = secret.ToArray();
    }

    public bool Contains(Guid serverId, SecretKind kind) => _secrets.ContainsKey((serverId, kind));

    public byte[]? Retrieve(Guid serverId, SecretKind kind) =>
        _secrets.TryGetValue((serverId, kind), out var secret) ? secret.ToArray() : null;

    public bool Delete(Guid serverId, SecretKind kind)
    {
        if (!_secrets.Remove((serverId, kind), out var secret))
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
