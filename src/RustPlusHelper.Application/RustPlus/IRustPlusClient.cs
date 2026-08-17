namespace RustPlusHelper.Application.RustPlus;

/// <summary>
/// Application-owned Rust+ boundary. No third-party protocol type may appear in this contract.
/// </summary>
public interface IRustPlusClient : IAsyncDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(RustPlusConnectionOptions options, CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task<RustPlusResult<ServerInfoSnapshot>> GetServerInfoAsync(CancellationToken cancellationToken = default);

    Task<RustPlusResult<ServerMapSnapshot>> GetMapAsync(CancellationToken cancellationToken = default);

    Task<RustPlusResult<TeamSnapshot>> GetTeamAsync(CancellationToken cancellationToken = default);

    Task<RustPlusResult<TeamChatSnapshot>> GetTeamChatAsync(CancellationToken cancellationToken = default);

    Task<RustPlusResult<MapMarkersSnapshot>> GetMapMarkersAsync(CancellationToken cancellationToken = default);
}
