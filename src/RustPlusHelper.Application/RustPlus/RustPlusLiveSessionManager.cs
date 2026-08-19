using RustPlusHelper.Application.Map;
using RustPlusHelper.Application.Vending;

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
    private static readonly TimeSpan MovementEventCooldown = TimeSpan.FromMinutes(1);

    // Caps how often a camera frame updates the UI-visible state, independent of how often the
    // server broadcasts rays — the renderer keeps accumulating every frame regardless, only the
    // published rate is capped. See AGENTS.md's "Map rendering rules" for why this matters.
    private static readonly TimeSpan CameraFrameThrottle = TimeSpan.FromMilliseconds(200);

    private readonly Lock _stateLock = new();
    private readonly Dictionary<ulong, DateTimeOffset> _lastMovementEventUtc = [];
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly SemaphoreSlim _wakeSignal = new(0, 1);
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private int _forceRefresh;
    private bool _disposed;

    // The connection is owned locally inside RunAsync's reconnect loop; this mirrors whichever
    // client instance is currently connected so camera calls (user-initiated, not polled) can
    // reach it. Guarded by _stateLock since RunAsync and camera calls run on different tasks.
    private IRustPlusClient? _activeClient;
    private DateTimeOffset _lastCameraFramePublishUtc;

    public event EventHandler? StateChanged;

    public RustPlusLiveSessionState Current { get; private set; } = RustPlusLiveSessionState.Stopped;

    public CameraSessionState CurrentCamera { get; private set; } = CameraSessionState.Inactive;

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
            _lastMovementEventUtc.Clear();
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
            SetCameraState(CameraSessionState.Inactive);
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
                            DetachCameraEvents(client);
                            await client.DisposeAsync().ConfigureAwait(false);
                            client = null;
                            ClearActiveClient();
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
                        AttachCameraEvents(client);

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
                        DetachCameraEvents(client);
                        await client.DisposeAsync().ConfigureAwait(false);
                        client = null;
                        ClearActiveClient();
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
                DetachCameraEvents(client);
                await client.DisposeAsync().ConfigureAwait(false);
                ClearActiveClient();
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
                    AddTeamEvents(serverId, updated.Team, result.Data, updated.Server?.MapSize);
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

    private void AddTeamEvents(
        Guid serverId,
        TeamSnapshot? previous,
        TeamSnapshot current,
        uint? mapSize)
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
                AddEvent(
                    serverId,
                    CompanionEventKind.TeamMemberDied,
                    CompanionEventSource.SnapshotDiff,
                    $"{name} died",
                    "Position from the Rust+ team snapshot where death was detected.",
                    new MapPositionSnapshot(member.X, member.Y));
            }
            else if (!old.IsAlive && member.IsAlive)
            {
                AddEvent(serverId, CompanionEventKind.TeamMemberRespawned, CompanionEventSource.SnapshotDiff, $"{name} respawned");
            }

            if (mapSize is not { } size
                || !old.IsOnline
                || !member.IsOnline
                || !old.IsAlive
                || !member.IsAlive)
            {
                continue;
            }

            var oldGrid = MapGrid.WorldToGrid(old.X, old.Y, size)?.Label;
            var newGrid = MapGrid.WorldToGrid(member.X, member.Y, size)?.Label;
            if (oldGrid is null || newGrid is null || oldGrid == newGrid)
            {
                continue;
            }

            var now = timeProvider.GetUtcNow();
            if (_lastMovementEventUtc.TryGetValue(member.SteamId, out var lastEventUtc)
                && now - lastEventUtc < MovementEventCooldown)
            {
                continue;
            }

            _lastMovementEventUtc[member.SteamId] = now;
            AddEvent(
                serverId,
                CompanionEventKind.TeamMemberChangedGrid,
                CompanionEventSource.SnapshotDiff,
                $"{name} entered {newGrid}",
                $"Moved from {oldGrid} to {newGrid}.");
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

        // Only diff offers for markers present before and after; a marker that just appeared or
        // disappeared already got its own event above and should not also flood offer-level events.
        foreach (var key in oldMarkers.Keys.Where(newMarkers.ContainsKey))
        {
            var previousMarker = oldMarkers[key];
            var currentMarker = newMarkers[key];
            if (currentMarker.Kind == MapMarkerKind.VendingMachine)
            {
                AddVendingOfferEvents(serverId, previousMarker, currentMarker);
            }
        }
    }

    private void AddVendingOfferEvents(Guid serverId, MapMarkerSnapshot previous, MapMarkerSnapshot current)
    {
        var oldOffers = ToOfferDictionary(previous.VendingOrders);
        var newOffers = ToOfferDictionary(current.VendingOrders);
        var position = current.X is { } x && current.Y is { } y ? new MapPositionSnapshot(x, y) : null;
        var machineName = MarkerLabel(current);

        foreach (var (key, offer) in newOffers)
        {
            if (!oldOffers.TryGetValue(key, out var previousOffer))
            {
                AddEvent(
                    serverId,
                    CompanionEventKind.VendingOfferAdded,
                    CompanionEventSource.SnapshotDiff,
                    $"New offer at {machineName}",
                    $"{ItemLabel(offer.ItemId)} for {offer.Cost} {ItemLabel(offer.CurrencyId)}",
                    position);
                continue;
            }

            if (previousOffer.Cost != offer.Cost)
            {
                AddEvent(
                    serverId,
                    CompanionEventKind.VendingPriceChanged,
                    CompanionEventSource.SnapshotDiff,
                    $"{ItemLabel(offer.ItemId)} price changed at {machineName}",
                    $"{previousOffer.Cost} → {offer.Cost} {ItemLabel(offer.CurrencyId)}",
                    position);
            }

            if (previousOffer.Stock != offer.Stock)
            {
                AddEvent(
                    serverId,
                    CompanionEventKind.VendingStockChanged,
                    CompanionEventSource.SnapshotDiff,
                    $"{ItemLabel(offer.ItemId)} stock changed at {machineName}",
                    $"{previousOffer.Stock} → {offer.Stock} in stock",
                    position);
            }
        }

        foreach (var (key, offer) in oldOffers)
        {
            if (!newOffers.ContainsKey(key))
            {
                AddEvent(
                    serverId,
                    CompanionEventKind.VendingOfferRemoved,
                    CompanionEventSource.SnapshotDiff,
                    $"Offer removed at {machineName}",
                    $"{ItemLabel(offer.ItemId)} for {offer.Cost} {ItemLabel(offer.CurrencyId)}",
                    position);
            }
        }
    }

    private static IReadOnlyDictionary<(int ItemId, int CurrencyId, bool IsItemBlueprint, bool IsCurrencyBlueprint), VendingOrderSnapshot>
        ToOfferDictionary(IReadOnlyList<VendingOrderSnapshot>? offers) =>
        (offers ?? [])
            .GroupBy(offer => (offer.ItemId, offer.CurrencyId, offer.IsItemBlueprint, offer.IsCurrencyBlueprint))
            .ToDictionary(group => group.Key, group => group.First());

    private static string ItemLabel(int itemId) => ItemCatalog.TryResolve(itemId)?.Name ?? $"item #{itemId}";

    /// <summary>Subscribes to a camera on the shared live connection, replacing any previous
    /// camera view. Cameras are viewed only on this explicit call, never on a background timer.</summary>
    public async Task<RustPlusResult<CameraInfoSnapshot>> ViewCameraAsync(
        string cameraCode,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(cameraCode);

        var client = GetActiveClient();
        if (client is null)
        {
            return RustPlusResult<CameraInfoSnapshot>.Failure(
                "not_connected",
                "The live Rust+ connection is not ready.");
        }

        SetCameraState(new CameraSessionState(CameraSessionStatus.Subscribing, cameraCode, null, null, null));

        var result = await client.SubscribeToCameraAsync(cameraCode, cancellationToken).ConfigureAwait(false);

        // A ray frame can arrive on the shared connection while the subscribe call is still in
        // flight; HandleCameraFrame will have already advanced CurrentCamera to Active with it.
        // Keep that frame rather than clobbering it back to null on the "just subscribed" state.
        var precedingFrame = CurrentCamera.CameraCode == cameraCode ? CurrentCamera.LatestFrame : null;
        SetCameraState(result.IsSuccess && result.Data is not null
            ? new CameraSessionState(CameraSessionStatus.Active, cameraCode, result.Data, precedingFrame, null)
            : new CameraSessionState(
                CameraSessionStatus.Failed,
                cameraCode,
                null,
                null,
                result.Error?.Message ?? "Camera subscription failed."));
        return result;
    }

    /// <summary>Ends the current camera view, if any.</summary>
    public async Task StopViewingCameraAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var client = GetActiveClient();
        if (client is not null)
        {
            await client.UnsubscribeFromCameraAsync(cancellationToken).ConfigureAwait(false);
        }

        SetCameraState(CameraSessionState.Inactive);
    }

    public Task<RustPlusResult<bool>> ZoomCameraAsync(CancellationToken cancellationToken = default) =>
        ExecuteCameraCommandAsync(client => client.ZoomCameraAsync(cancellationToken));

    public Task<RustPlusResult<bool>> ShootCameraAsync(CancellationToken cancellationToken = default) =>
        ExecuteCameraCommandAsync(client => client.ShootCameraAsync(cancellationToken));

    public Task<RustPlusResult<bool>> ReloadCameraAsync(CancellationToken cancellationToken = default) =>
        ExecuteCameraCommandAsync(client => client.ReloadCameraAsync(cancellationToken));

    public Task<RustPlusResult<bool>> LookCameraAsync(
        float deltaX,
        float deltaY,
        CancellationToken cancellationToken = default) =>
        ExecuteCameraCommandAsync(client => client.LookCameraAsync(deltaX, deltaY, cancellationToken));

    public Task<RustPlusResult<bool>> MoveCameraAsync(
        CameraMoveDirection direction,
        CancellationToken cancellationToken = default) =>
        ExecuteCameraCommandAsync(client => client.MoveCameraAsync(direction, cancellationToken));

    private async Task<RustPlusResult<bool>> ExecuteCameraCommandAsync(
        Func<IRustPlusClient, Task<RustPlusResult<bool>>> operation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var client = GetActiveClient();
        if (client is null || CurrentCamera.Status != CameraSessionStatus.Active)
        {
            return RustPlusResult<bool>.Failure("no_active_camera", "No camera subscription is active.");
        }

        return await operation(client).ConfigureAwait(false);
    }

    private IRustPlusClient? GetActiveClient()
    {
        lock (_stateLock)
        {
            return _activeClient;
        }
    }

    private void AttachCameraEvents(IRustPlusClient client)
    {
        client.CameraFrameReceived += HandleCameraFrame;
        client.CameraSubscriptionFailed += HandleCameraSubscriptionFailed;
        lock (_stateLock)
        {
            _activeClient = client;
        }
    }

    private void DetachCameraEvents(IRustPlusClient client)
    {
        client.CameraFrameReceived -= HandleCameraFrame;
        client.CameraSubscriptionFailed -= HandleCameraSubscriptionFailed;
    }

    private void ClearActiveClient()
    {
        lock (_stateLock)
        {
            _activeClient = null;
        }

        // The connection that owned the camera subscription is gone; never leave the UI showing
        // a stale "still active" view once it truthfully is not.
        if (CurrentCamera.Status == CameraSessionStatus.Active)
        {
            SetCameraState(CurrentCamera with
            {
                Status = CameraSessionStatus.Failed,
                Error = "The Rust+ connection was lost."
            });
        }
    }

    private void HandleCameraFrame(object? sender, CameraFrameSnapshot frame)
    {
        var shouldPublish = false;
        lock (_stateLock)
        {
            var now = timeProvider.GetUtcNow();
            if (now - _lastCameraFramePublishUtc >= CameraFrameThrottle)
            {
                _lastCameraFramePublishUtc = now;
                shouldPublish = true;
            }
        }

        if (shouldPublish)
        {
            SetCameraState(CurrentCamera with { Status = CameraSessionStatus.Active, LatestFrame = frame, Error = null });
        }
    }

    private void HandleCameraSubscriptionFailed(object? sender, RustPlusError error) =>
        SetCameraState(CurrentCamera with { Status = CameraSessionStatus.Failed, Error = error.Message });

    private void SetCameraState(CameraSessionState state)
    {
        lock (_stateLock)
        {
            CurrentCamera = state;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AddEvent(
        Guid serverId,
        CompanionEventKind kind,
        CompanionEventSource source,
        string title,
        string? detail = null,
        MapPositionSnapshot? position = null)
    {
        var item = new CompanionEvent(
            Guid.NewGuid(),
            serverId,
            timeProvider.GetUtcNow(),
            kind,
            source,
            title,
            detail,
            position);
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
