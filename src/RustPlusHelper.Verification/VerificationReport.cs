namespace RustPlusHelper.Verification;

public sealed record VerificationRequestStatus(bool Success, string? ErrorCode = null, string? ErrorMessage = null);

public sealed record ServerVerificationSummary(
    uint? MapSize,
    DateTimeOffset? WipeTimeUtc,
    uint? PlayerCount,
    uint? MaxPlayerCount,
    uint? QueueCount);

public sealed record MapVerificationSummary(
    uint? Width,
    uint? Height,
    int? OceanMargin,
    int MonumentCount,
    int JpegBytes,
    string? JpegSha256);

public sealed record TeamVerificationSummary(
    int MemberCount,
    int OnlineCount,
    int AliveCount,
    bool LeaderAppearsInRoster);

public sealed record ChatVerificationSummary(
    int MessageCount,
    DateTimeOffset? OldestMessageUtc,
    DateTimeOffset? NewestMessageUtc);

public sealed record MarkerVerificationSummary(
    int MarkerCount,
    IReadOnlyDictionary<string, int> CountsByKind,
    IReadOnlyList<int> UnknownRawTypes,
    int VendingOrderCount);

/// <summary>
/// Deliberately aggregate-only report. It excludes endpoint, credentials, Steam IDs, player names,
/// chat bodies, coordinates, vending names, and server branding.
/// </summary>
public sealed record VerificationReport(
    int SchemaVersion,
    DateTimeOffset CapturedUtc,
    string Mode,
    string Transport,
    bool Success,
    IReadOnlyDictionary<string, VerificationRequestStatus> Requests,
    ServerVerificationSummary? Server,
    MapVerificationSummary? Map,
    TeamVerificationSummary? Team,
    ChatVerificationSummary? Chat,
    MarkerVerificationSummary? Markers);

public sealed record VerificationRunResult(VerificationReport Report, byte[] MapJpeg);
