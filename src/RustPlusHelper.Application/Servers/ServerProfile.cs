namespace RustPlusHelper.Application.Servers;

public sealed record ServerProfile(
    Guid Id,
    string DisplayName,
    string Host,
    int Port,
    bool UseFacepunchProxy,
    ulong? PlayerId,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    DateTimeOffset? LastSelectedUtc);

public sealed record ServerProfileDraft(
    Guid? Id,
    string DisplayName,
    string Host,
    int Port,
    bool UseFacepunchProxy = true,
    ulong? PlayerId = null);
