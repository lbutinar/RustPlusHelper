using System.Security.Cryptography;
using System.Text;
using RustPlusHelper.Application.Security;
using RustPlusHelper.Application.Testing;

namespace RustPlusHelper.Tests;

/// <summary>
/// Shared assertion helpers for verifying secrets stored via
/// <see cref="InMemorySecretStore"/>, zeroing the retrieved buffer afterwards.
/// </summary>
internal static class SecretAssertions
{
    /// <summary>
    /// Asserts that the <see cref="SecretKind.RustPlusPlayerToken"/> secret stored for
    /// <paramref name="serverId"/> matches <paramref name="expected"/>, then zeroes and
    /// discards the retrieved buffer regardless of the outcome.
    /// </summary>
    public static void AssertToken(InMemorySecretStore secrets, Guid serverId, string expected)
    {
        var restored = secrets.Retrieve(serverId, SecretKind.RustPlusPlayerToken);
        try
        {
            Assert.NotNull(restored);
            Assert.Equal(expected, Encoding.UTF8.GetString(restored));
        }
        finally
        {
            if (restored is not null)
            {
                CryptographicOperations.ZeroMemory(restored);
            }
        }
    }
}
