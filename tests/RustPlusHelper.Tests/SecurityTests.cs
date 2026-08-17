using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Application.Security;

namespace RustPlusHelper.Tests;

public sealed class SecurityTests
{
    [Fact]
    public void ConnectionOptionsNeverPrintPlayerToken()
    {
        const int token = 193746281;
        var options = new RustPlusConnectionOptions("127.0.0.1", 28082, 76561198000000000, token);

        var text = options.ToString();

        Assert.DoesNotContain(token.ToString(System.Globalization.CultureInfo.InvariantCulture), text, StringComparison.Ordinal);
        Assert.Contains("PlayerToken = ***", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("playerToken=193746281")]
    [InlineData("Player_Token: '193746281'")]
    [InlineData("RUSTPLUS_PLAYER_TOKEN=193746281")]
    [InlineData("Authorization: Bearer-secret")]
    [InlineData("fcmToken=FCM-secret")]
    [InlineData("expo_token: Expo-secret")]
    public void RedactorRemovesSensitiveKeyValues(string input)
    {
        var result = SecretRedactor.Redact(input);

        Assert.Contains("***", result, StringComparison.Ordinal);
        Assert.DoesNotContain("193746281", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer-secret", result, StringComparison.Ordinal);
        Assert.DoesNotContain("FCM-secret", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Expo-secret", result, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactorRemovesExplicitKnownSecret()
    {
        const string secret = "a-secret-without-a-key";

        var result = SecretRedactor.Redact($"Failure returned {secret} from upstream", secret);

        Assert.Equal("Failure returned *** from upstream", result);
    }
}
