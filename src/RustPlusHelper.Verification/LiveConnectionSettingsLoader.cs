using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using RustPlusHelper.Application.RustPlus;

namespace RustPlusHelper.Verification;

internal static class LiveConnectionSettingsLoader
{
    internal static RustPlusConnectionOptions Load(bool allowInsecureDirect)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
            .AddEnvironmentVariables()
            .Build();

        var server = Read(configuration, "Server");
        var portText = Read(configuration, "Port");
        var playerIdText = Read(configuration, "PlayerId");
        var playerTokenText = Read(configuration, "PlayerToken");
        var useProxyText = Read(configuration, "UseFacepunchProxy", required: false);

        if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port))
        {
            throw new InvalidOperationException("RustPlus:Port must be a valid integer.");
        }

        if (!ulong.TryParse(playerIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var playerId)
            || playerId == 0)
        {
            throw new InvalidOperationException("RustPlus:PlayerId must be a non-zero unsigned integer.");
        }

        if (!int.TryParse(playerTokenText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var playerToken))
        {
            throw new InvalidOperationException("RustPlus:PlayerToken must be a signed 32-bit integer.");
        }

        var useProxy = true;
        if (!string.IsNullOrWhiteSpace(useProxyText)
            && !bool.TryParse(useProxyText, out useProxy))
        {
            throw new InvalidOperationException("RustPlus:UseFacepunchProxy must be true or false.");
        }

        if (!useProxy && !allowInsecureDirect)
        {
            throw new InvalidOperationException(
                "Direct Rust+ transport uses ws://. Re-run with --allow-insecure-direct only if that exposure is intentional.");
        }

        return new RustPlusConnectionOptions(server, port, playerId, playerToken, useProxy);
    }

    private static string Read(IConfiguration configuration, string key, bool required = true)
    {
        var environmentName = $"RUSTPLUS_{ToEnvironmentKey(key)}";
        var value = Environment.GetEnvironmentVariable(environmentName)
            ?? configuration[$"RustPlus:{key}"];

        if (required && string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Missing RustPlus:{key}. Configure it with .NET user-secrets or {environmentName}.");
        }

        return value ?? string.Empty;
    }

    private static string ToEnvironmentKey(string key)
    {
        var builder = new System.Text.StringBuilder();
        for (var index = 0; index < key.Length; index++)
        {
            if (index > 0 && char.IsUpper(key[index]))
            {
                builder.Append('_');
            }

            builder.Append(char.ToUpperInvariant(key[index]));
        }

        return builder.ToString();
    }
}
