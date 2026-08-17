namespace RustPlusHelper.Application.RustPlus;

/// <summary>
/// One authenticated, read-only Rust+ refresh cycle. Optional request failures remain independent so
/// a server can still provide markers when the player has no team, for example.
/// </summary>
public sealed record RustPlusDashboardLoadResult(
    RustPlusConnectionState ConnectionState,
    ServerInfoSnapshot? ServerInfo = null,
    RustPlusResult<ServerMapSnapshot>? Map = null,
    RustPlusResult<TeamSnapshot>? Team = null,
    RustPlusResult<TeamChatSnapshot>? Chat = null,
    RustPlusResult<MapMarkersSnapshot>? Markers = null)
{
    public bool IsAuthenticated =>
        ConnectionState.Status == RustPlusConnectionStatus.Succeeded
        && ServerInfo is not null;

    public bool HasCompleteLiveData =>
        Team?.IsSuccess == true
        && Chat?.IsSuccess == true
        && Markers?.IsSuccess == true;
}
