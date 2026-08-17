namespace RustPlusHelper.Application.RustPlus;

public sealed record RustPlusMapLoadResult(
    RustPlusConnectionState ConnectionState,
    ServerInfoSnapshot? ServerInfo = null,
    ServerMapSnapshot? Map = null)
{
    public bool IsSuccess =>
        ConnectionState.Status == RustPlusConnectionStatus.Succeeded
        && ServerInfo is not null
        && Map is not null;
}
