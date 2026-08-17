using RustPlusApi.Data;
using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Application.Security;
using ApiClient = RustPlusApi.RustPlus;
using ApiConnection = RustPlusApi.RustPlusConnection;
using ApiInterface = RustPlusApi.Interfaces.IRustPlus;

namespace RustPlusHelper.Infrastructure.RustPlus;

/// <summary>
/// Adapter around HandyS11/RustPlusApi. This is the only production class allowed to create the
/// third-party client; callers receive only application-owned snapshots.
/// </summary>
public sealed class RustPlusApiClient : IRustPlusClient
{
    private ApiInterface? _client;
    private string? _tokenText;

    public bool IsConnected => _client?.IsConnected == true;

    public async Task ConnectAsync(RustPlusConnectionOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (_client is not null)
        {
            throw new InvalidOperationException("This Rust+ client already has an active connection lifecycle.");
        }

        _tokenText = options.PlayerToken.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var client = new ApiClient(new ApiConnection(
            options.Server,
            options.Port,
            options.PlayerId,
            options.PlayerToken,
            options.UseFacepunchProxy));

        try
        {
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            _client = client;
        }
        catch (OperationCanceledException)
        {
            await client.DisposeAsync().ConfigureAwait(false);
            _tokenText = null;
            throw;
        }
        catch (Exception exception)
        {
            await client.DisposeAsync().ConfigureAwait(false);
            var safeMessage = SecretRedactor.Redact(exception.Message, _tokenText);
            _tokenText = null;
            throw new InvalidOperationException($"Rust+ connection failed: {safeMessage}");
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var client = _client;
        _client = null;
        _tokenText = null;

        if (client is null)
        {
            return;
        }

        try
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    public Task<RustPlusResult<ServerInfoSnapshot>> GetServerInfoAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            client => client.GetInfoAsync(cancellationToken),
            RustPlusApiMapper.Map,
            cancellationToken);

    public Task<RustPlusResult<ServerMapSnapshot>> GetMapAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            client => client.GetMapAsync(cancellationToken),
            RustPlusApiMapper.Map,
            cancellationToken);

    public Task<RustPlusResult<TeamSnapshot>> GetTeamAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            client => client.GetTeamInfoAsync(cancellationToken),
            RustPlusApiMapper.Map,
            cancellationToken);

    public Task<RustPlusResult<TeamChatSnapshot>> GetTeamChatAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            client => client.GetTeamChatAsync(cancellationToken),
            RustPlusApiMapper.Map,
            cancellationToken);

    public Task<RustPlusResult<MapMarkersSnapshot>> GetMapMarkersAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            client => client.GetMapMarkersAsync(cancellationToken),
            RustPlusApiMapper.Map,
            cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }

    private async Task<RustPlusResult<TSnapshot>> ExecuteAsync<TApi, TSnapshot>(
        Func<ApiInterface, Task<Response<TApi?>>> operation,
        Func<TApi, TSnapshot> mapper,
        CancellationToken cancellationToken)
        where TApi : class
    {
        cancellationToken.ThrowIfCancellationRequested();

        var client = _client;
        if (client?.IsConnected != true)
        {
            return RustPlusResult<TSnapshot>.Failure("not_connected", "The Rust+ client is not connected.");
        }

        try
        {
            var response = await operation(client).ConfigureAwait(false);
            if (!response.IsSuccess || response.Data is null)
            {
                var code = response.Error?.Code.ToString() ?? "unknown_error";
                var message = SecretRedactor.Redact(response.Error?.Message ?? "Rust+ returned no data.", _tokenText);
                return RustPlusResult<TSnapshot>.Failure(code, message);
            }

            return RustPlusResult<TSnapshot>.Success(mapper(response.Data));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return RustPlusResult<TSnapshot>.Failure(
                "transport_exception",
                SecretRedactor.Redact(exception.Message, _tokenText));
        }
    }
}
