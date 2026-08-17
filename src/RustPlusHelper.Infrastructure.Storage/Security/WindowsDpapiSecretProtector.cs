using System.Security.Cryptography;
using RustPlusHelper.Application.Security;

namespace RustPlusHelper.Infrastructure.Storage.Security;

public sealed class WindowsDpapiSecretProtector : ISecretProtector
{
    public byte[] Protect(ReadOnlySpan<byte> cleartext, ReadOnlySpan<byte> context)
    {
        if (cleartext.IsEmpty)
        {
            throw new ArgumentException("Secret material cannot be empty.", nameof(cleartext));
        }

        var cleartextCopy = cleartext.ToArray();
        var contextCopy = context.ToArray();
        try
        {
            return ProtectedData.Protect(cleartextCopy, contextCopy, DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cleartextCopy);
            CryptographicOperations.ZeroMemory(contextCopy);
        }
    }

    public byte[] Unprotect(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> context)
    {
        if (ciphertext.IsEmpty)
        {
            throw new ArgumentException("Protected secret material cannot be empty.", nameof(ciphertext));
        }

        var ciphertextCopy = ciphertext.ToArray();
        var contextCopy = context.ToArray();
        try
        {
            return ProtectedData.Unprotect(ciphertextCopy, contextCopy, DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ciphertextCopy);
            CryptographicOperations.ZeroMemory(contextCopy);
        }
    }
}
