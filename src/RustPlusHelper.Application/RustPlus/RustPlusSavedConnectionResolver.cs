using System.Buffers.Text;
using System.Security.Cryptography;
using RustPlusHelper.Application.Security;
using RustPlusHelper.Application.Servers;

namespace RustPlusHelper.Application.RustPlus;

public sealed record ResolvedRustPlusConnection(
    ServerProfile Profile,
    RustPlusConnectionOptions Options);

public sealed record RustPlusConnectionResolution(
    ResolvedRustPlusConnection? Connection,
    RustPlusConnectionStatus FailureStatus = RustPlusConnectionStatus.Failed,
    string? FailureLabel = null,
    string? FailureDetail = null)
{
    public bool IsSuccess => Connection is not null;
}

/// <summary>Resolves a saved profile and DPAPI-backed token without retaining cleartext buffers.</summary>
public sealed class RustPlusSavedConnectionResolver(
    ServerManager servers,
    ISecretStore secretStore)
{
    public RustPlusConnectionResolution Resolve(Guid serverId)
    {
        var profile = servers.Profiles.FirstOrDefault(candidate => candidate.Id == serverId);
        if (profile is null)
        {
            return Failure(
                RustPlusConnectionStatus.Failed,
                "Server not found",
                "The saved server profile no longer exists.");
        }

        if (profile.PlayerId is null or 0)
        {
            return Failure(
                RustPlusConnectionStatus.PairingRequired,
                "Pairing required",
                "Save your Steam64 identity before connecting to this server.");
        }

        var tokenBytes = secretStore.Retrieve(serverId, SecretKind.RustPlusPlayerToken);
        if (tokenBytes is null)
        {
            return Failure(
                RustPlusConnectionStatus.PairingRequired,
                "Pairing required",
                "Enter the player token supplied by this server pairing.");
        }

        try
        {
            if (!Utf8Parser.TryParse(tokenBytes, out int playerToken, out var consumed)
                || consumed != tokenBytes.Length)
            {
                return Failure(
                    RustPlusConnectionStatus.PairingRequired,
                    "Pairing required",
                    "The protected pairing token is invalid. Re-pair this server.");
            }

            return new RustPlusConnectionResolution(new ResolvedRustPlusConnection(
                profile,
                new RustPlusConnectionOptions(
                    profile.Host,
                    profile.Port,
                    profile.PlayerId.Value,
                    playerToken,
                    profile.UseFacepunchProxy)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tokenBytes);
        }
    }

    private static RustPlusConnectionResolution Failure(
        RustPlusConnectionStatus status,
        string label,
        string detail) =>
        new(null, status, label, detail);
}
