namespace RustPlusHelper.Application.Servers;

/// <summary>A user-entered guess at how often this server wipes — Rust+ reports no wipe schedule,
/// only the timestamp of the last wipe (<c>ServerInfoSnapshot.WipeTimeUtc</c>). Used only to estimate
/// a "next wipe" display; never presented as a confirmed schedule.</summary>
public enum WipeCycle
{
    Unknown,
    Weekly,
    Biweekly,
    Monthly
}

public sealed record ServerProfile(
    Guid Id,
    string DisplayName,
    string Host,
    int Port,
    bool UseFacepunchProxy,
    ulong? PlayerId,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    DateTimeOffset? LastSelectedUtc,
    Guid? RustPlusServerId = null,
    WipeCycle WipeCycle = WipeCycle.Unknown);

public sealed record ServerProfileDraft(
    Guid? Id,
    string DisplayName,
    string Host,
    int Port,
    bool UseFacepunchProxy = true,
    ulong? PlayerId = null,
    Guid? RustPlusServerId = null);
