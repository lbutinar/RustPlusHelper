using System.Text.RegularExpressions;

namespace RustPlusHelper.Application.Security;

public static partial class SecretRedactor
{
    private const string Redacted = "***";

    public static string Redact(string? value, params string?[] knownSecrets)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        var result = SensitiveKeyPattern().Replace(value, match => $"{match.Groups[1].Value}={Redacted}");

        foreach (var secret in knownSecrets)
        {
            if (!string.IsNullOrWhiteSpace(secret))
            {
                result = result.Replace(secret, Redacted, StringComparison.Ordinal);
            }
        }

        return result;
    }

    [GeneratedRegex(
        @"(?i)\b(player_?token|rustplus_player_token|auth_?token|fcm_?token|expo_?token|authorization)\b\s*[:=]\s*[\""']?([^\s,}\""']+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveKeyPattern();
}
