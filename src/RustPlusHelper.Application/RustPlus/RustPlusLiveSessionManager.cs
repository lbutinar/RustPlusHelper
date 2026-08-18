namespace RustPlusHelper.Application.RustPlus;

/// <summary>
/// Owns one persistent selected-server connection, centralized polling, reconnect backoff, and
/// bounded in-memory semantic events. It never downloads the five-token map on a timer.
/// </summary>
public sealed class RustPlusLiveSessionManager(
    RustPlusSavedConnectionResolver connectionResolver,
    IRustPlusClientFactory clientFactory,
    TimeProvider timeProvider,
    RustPlusPollingOptions pollingOptions,
    ICompanionEventRepository eventRepository) : IAsyncDisposable, IDisposable
{
    private const int EventLimit = 200;
    private readonly Lock _stateLock = new();
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly SemaphoreSlim _wakeSignal = new(0, 1);
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private int _forceRefresh;
    private bool _disposed;

    public event EventHandler? StateChanged;

    public RustPlusLiveSessionState Current { get; private set; } = RustPlusLiveSessionState.Stopped;

    public async Task StartAsync(
        Guid serverId,
        RustPlusLiveSessionSeed? seed = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_runTask is { IsCompleted: false } && Current.ServerId == serverId)
            {
                RequestRefresh();
                return;
            }

            await StopCoreAsync().ConfigureAwait(false);
            var initial = seed ?? new RustPlusLiveSessionSeed();
            var history = eventRepository.GetRecent(serverId, EventLimit);
            SetState(new RustPlusLiveSessionState(
                serverId,
                RustPlusLiveSessionStatus.Connecting,
                "Connecting background monitor",
                initial.Server,
                initial.Team,
                initial.Chat,
                initial.Markers,
                initial.RetrievedAtUtc,
                null,
                history));
            _runCancellation = new CancellationTokenSource();
            _runTask = RunAsync(serverId, _runCancellation.Token);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public void RequestRefresh()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Interlocked.Exchange(ref _forceRefresh, 1);
        try
        {
            _wakeSignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // A refresh wake-up is already pending.
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
            SetState(RustPlusLiveSessionState.Stopped);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _disposed = true;
        _lifecycleLock.Dispose();
        _wakeSignal.Dispose();
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private async Task RunAsync(Guid serverId, CancellationToken cancellationToken)
    {
        IRustPlusClient? client = null;
        var reconnectAttempt = 0;
        var hasConnected = false;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (client?.IsConnected != true)
                    {
                        if (client is not null)
                        {
                            await client.DisposeAsync().ConfigureAwait(false);
                            client = null;
                        }

                        var resolution = connectionResolver.Resolve(serverId);
                        if (!resolution.IsSuccess || resolution.Connection is not { } connection)
                        {
                            var status = resolution.FailureStatus == RustPlusConnectionStatus.PairingRequired
                                ? RustPlusLiveSessionStatus.PairingRequired
                                : RustPlusLiveSessionStatus.Reconnecting;
                            SetState(Current with
                            {
                                Status = status,
                                Label = resolution.FailureLabel ?? "Connection unavailable",
                                Error = resolution.FailureDetail
                            });
                            if (status == RustPlusLiveSessionStatus.PairingRequired)
                            {
                                return;
                            }

                            await WaitForRetryAsync(reconnectAttempt++, cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        SetState(Current with
                        {
                            Status = hasConnected
                                ? RustPlusLiveSessionStatus.Reconnecting
                                : RustPlusLiveSessionStatus.Connecting,
                            Label = hasConnected ? "Reconnecting Rust+" : "Connecting Rust+",
                            Error = null
                        });

                        client = clientFactory.Create();
                        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        connectTimeout.CancelAfter(pollingOptions.ConnectTimeout);
                        await client.ConnectAsync(connection.Options, connectTimeout.Token).ConfigureAwait(false);

                        var info = await client.GetServerInfoAsync(connectTimeout.Token).ConfigureAwait(false);
                        if (!info.IsSuccess || info.Data is null)
                        {
                            if (IsAuthenticationRejected(info.Error))
                            {
                                SetState(Current with
                                {
                                    Status = RustPlusLiveSessionStatus.AuthenticationRejected,
                                    Label = "Pairing rejected",
                                    Error = "The server rejected this player token. Re-pair the saved server."
                                });
                                return;
                            }

                            throw new LiveTransportException(DescribeError("Server info", info.Error));
                        }

                        var eventKind = hasConnected
                            ? CompanionEventKind.ConnectionRestored
                            : CompanionEventKind.ConnectionEstablished;
                        var eventTitle = hasConnected ? "Rust+ connection restored" : "Rust+ monitoring connected";
                        AddEvent(serverId, eventKind, CompanionEventSource.Transport, eventTitle);
                        hasConnected = true;
                        reconnectAttempt = 0;
                        SetState(Current with
                        {
                            Status = RustPlusLiveSessionStatus.Connected,
                            Label = "Live monitoring connected",
                            Server = info.Data,
                            LastRefreshUtc = timeProvider.GetUtcNow(),
                            Error = null
                        });
                    }

                    await PollConnectedAsync(serverId, client, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    if (client is not null)
                    {
                        await client.DisposeAsync().ConfigureAwait(false);
                        client = null;
                    }

                    if (hasConnected && Current.Status == RustPlusLiveSessionStatus.Connected)
                    {
                        AddEvent(
                            serverId,
                            CompanionEventKind.ConnectionLost,
                            CompanionEventSource.Transport,
                            "Rust+ connection lost");
                    }

                    SetState(Current with
                    {
                        Status = RustPlusLiveSessionStatus.Reconnecting,
                        Label = "Rust+ connection lost",
                        Error = SafeException(exception)
                    });
                    await WaitForRetryAsync(reconnectAttempt++, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            if (client is not null)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task PollConnectedAsync(
        Guid serverId,
        IRustPlusClient client,
        CancellationToken cancellationToken)
    {
        var state = Current;
        var now = timeProvider.GetUtcNow();
        var nextInfo = state.Server is null ? now : now + pollingOptions.ServerInfoInterval;
        var nextTeam = state.Team is null ? now : now + pollingOptions.TeamInterval;
        var nextChat = state.Chat is null ? now : now + pollingOptions.ChatInterval;
        var nextMarkers = state.Markers is null ? now : now + pollingOptions.MarkerInterval;

        while (client.IsConnected && !cancellationToken.IsCancellationRequested)
        {
            now = timeProvider.GetUtcNow();
            var force = Interlocked.Exchange(ref _forceRefresh, 0) == 1;
            var errors = new List<string>();
            var updated = Current;

            if (force || now >= nextInfo)
            {
                var result = await client.GetServerInfoAsync(cancellationToken).ConfigureAwait(false);
                ThrowIfTransportFailure("Server info", result.Error);
                if (result.IsSuccess && result.Data is not null)
                {
                    updated = updated with { Server = result.Data };
                }
                else
                {
                    errors.Add(DescribeError("Server info", result.Error));
                }

                nextInfo = now + RetryInterval(result.Error, pollingOptions.ServerInfoInterval);
            }

            if (force || now >= nextTeam)
            {
                var result = await client.GetTeamAsync(cancellationToken).ConfigureAwait(false);
                ThrowIfTransportFailure("Team", result.Error);
                if (result.IsSuccess && result.Data is not null)
                {
                    AddTeamEvents(serverId, updated.Team, result.Data);
                    updated = updated with { Team = result.Data };
                }
                else
                {
                    errors.Add(DescribeError("Team", result.Error));
                }

                nextTeam = now + RetryInterval(result.Error, pollingOptions.TeamInterval);
            }

            if (force || now >= nextChat)
            {
                var result = await client.GetTeamChatAsync(cancellationToken).ConfigureAwait(false);
                ThrowIfTransportFailure("Team chat", result.Error);
                if (result.IsSuccess && result.Data is not null)
                {
                    updated = updated with { Chat = result.Data };
                }
                else
                {
                    errors.Add(DescribeError("Team chat", result.Error));
                }

                nextChat = now + RetryInterval(result.Error, pollingOptions.ChatInterval);
            }

            if (force || now >= nextMarkers)
            {
                var result = await client.GetMapMarkersAsync(cancellationToken).ConfigureAwait(false);
                ThrowIfTransportFailure("Map markers", result.Error);
                if (result.IsSuccess && result.Data is not null)
                {
                    AddMarkerEvents(serverId, updated.Markers, result.Data);
                    updated = updated with { Markers = result.Data };
                }
                else
                {
                    errors.Add(DescribeError("Map markers", result.Error));
                }

                nextMarkers = now + RetryInterval(result.Error, pollingOptions.MarkerInterval);
            }

            SetState(updated with
            {
                Status = RustPlusLiveSessionStatus.Connected,
                Label = errors.Count == 0 ? "Live monitoring connected" : "Live monitoring partially available",
                LastRefreshUtc = now,
                Error = errors.Count == 0 ? null : string.Join(" ", errors),
                Events = Current.Events
            });

            var nextDue = new[] { nextInfo, nextTeam, nextChat, nextMarkers }.Min();
            var delay = nextDue - timeProvider.GetUtcNow();
            if (delay < TimeSpan.Zero)
            {
                delay = TimeSpan.Zero;
            }

            await _wakeSignal.WaitAsync(delay, cancellationToken).ConfigureAwait(false);
        }

        throw new LiveTransportException("The Rust+ WebSocket closed.");
    }

    private void AddTeamEvents(Guid serverId, TeamSnapshot? previous, TeamSnapshot current)
    {
        if (previous is null)
        {
            return;
        }

        var previousMembers = previous.Members.ToDictionary(member => member.SteamId);
        foreach (var member in current.Members)
        {
            if (!previousMembers.TryGetValue(member.SteamId, out var old))
            {
                continue;
            }

            var name = member.Name ?? "A team member";
            if (!old.IsOnline && member.IsOnline)
            {
                AddEvent(serverId, CompanionEventKind.TeamMemberConnected, CompanionEventSource.SnapshotDiff, $"{name} connected");
            }
            else if (old.IsOnline && !member.IsOnline)
            {
                AddEvent(serverId, CompanionEventKind.TeamMemberDisconnected, CompanionEventSource.SnapshotDiff, $"{name} disconnected");
            }

            if (old.IsAlive && !member.IsAlive)
            {
                AddEvent(serverId, CompanionEventKind.TeamMemberDied, CompanionEventSource.SnapshotDiff, $"{name} died");
            }
            else if (!old.IsAlive && member.IsAlive)
            {
                AddEvent(serverId, CompanionEventKind.TeamMemberRespawned, CompanionEventSource.SnapshotDiff, $"{name} respawned");
            }
        }
    }

    private void AddMarkerEvents(Guid serverId, MapMarkersSnapshot? previous, MapMarkersSnapshot current)
    {
        if (previous is null)
        {
            return;
        }

        var oldMarkers = ToMarkerDictionary(previous.Markers);
        var newMarkers = ToMarkerDictionary(current.Markers);
        foreach (var marker in newMarkers.Where(entry => !oldMarkers.ContainsKey(entry.Key)).Select(entry => entry.Value))
        {
            AddEvent(
                serverId,
                CompanionEventKind.MarkerAppeared,
                CompanionEventSource.SnapshotDiff,
                $"{MarkerLabel(marker)} appeared");
        }

        foreach (var marker in oldMarkers.Where(entry => !newMarkers.ContainsKey(entry.Key)).Select(entry => entry.Value))
        {
            AddEvent(
                serverId,
                CompanionEventKind.MarkerDisappeared,
                CompanionEventSource.SnapshotDiff,
                $"{MarkerLabel(marker)} disappeared");
        }
    }

    private void AddEvent(
        Guid serverId,
        CompanionEventKind kind,
        CompanionEventSource source,
        string title)
    {
        var item = new CompanionEvent(
            Guid.NewGuid(),
            serverId,
            timeProvider.GetUtcNow(),
            kind,
            source,
            title);
        eventRepository.Append(item, EventLimit);
        lock (_stateLock)
        {
            Current = Current with { Events = [item, .. Current.Events.Take(EventLimit - 1)] };
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetState(RustPlusLiveSessionState state)
    {
        lock (_stateLock)
        {
            Current = state;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task StopCoreAsync()
    {
        var cancellation = _runCancellation;
        var task = _runTask;
        _runCancellation = null;
        _runTask = null;
        if (cancellation is null || task is null)
        {
            cancellation?.Dispose();
            return;
        }

        cancellation.Cancel();
        try
        {
            _wakeSignal.Release();
        }
        catch (SemaphoreFullException)
        {
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private async Task WaitForRetryAsync(int attempt, CancellationToken cancellationToken)
    {
        var delays = pollingOptions.ReconnectDelays;
        var delay = delays.Count == 0
            ? TimeSpan.FromSeconds(5)
            : delays[Math.Min(attempt, delays.Count - 1)];
        await _wakeSignal.WaitAsync(delay, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsAuthenticationRejected(RustPlusError? error) =>
        error?.Code.Equals("AccessDenied", StringComparison.OrdinalIgnoreCase) == true
        || error?.Code.Equals("access_denied", StringComparison.OrdinalIgnoreCase) == true;

    private static void ThrowIfTransportFailure(string operation, RustPlusError? error)
    {
        if (error?.Code is "not_connected" or "transport_exception")
        {
            throw new LiveTransportException(DescribeError(operation, error));
        }
    }

    private static string DescribeError(string operation, RustPlusError? error) =>
        $"{operation} unavailable ({error?.Code ?? "unknown_error"}).";

    private static TimeSpan RetryInterval(RustPlusError? error, TimeSpan successInterval)
    {
        if (error is null)
        {
            return successInterval;
        }

        var failureInterval = error.Code.Equals("NoTeam", StringComparison.OrdinalIgnoreCase)
            ? TimeSpan.FromMinutes(1)
            : TimeSpan.FromSeconds(30);
        return failureInterval > successInterval ? failureInterval : successInterval;
    }

    private static string SafeException(Exception exception) => exception switch
    {
        RustPlusConnectionException connection => connection.Message,
        LiveTransportException transport => transport.Message,
        _ => $"Rust+ background monitoring failed ({exception.GetType().Name})."
    };

    private static string MarkerKey(MapMarkerSnapshot marker) => marker.Id is { } id
        ? $"id:{id}"
        : $"{marker.Kind}:{marker.RawType}:{marker.X:0}:{marker.Y:0}:{marker.Name}";

    private static IReadOnlyDictionary<string, MapMarkerSnapshot> ToMarkerDictionary(
        IEnumerable<MapMarkerSnapshot> markers) =>
        markers
            .GroupBy(MarkerKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

    private static string MarkerLabel(MapMarkerSnapshot marker) =>
        marker.Name ?? marker.Kind switch
        {
            MapMarkerKind.Ch47 => "CH47",
            MapMarkerKind.CargoShip => "Cargo ship",
            MapMarkerKind.PatrolHelicopter => "Patrol helicopter",
            MapMarkerKind.VendingMachine => "Vending machine",
            MapMarkerKind.TravellingVendor => "Travelling vendor",
            _ => marker.Kind.ToString()
        };

    private sealed class LiveTransportException(string message) : Exception(message);
}
