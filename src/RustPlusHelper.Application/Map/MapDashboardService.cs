using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Application.Security;
using RustPlusHelper.Application.Servers;

namespace RustPlusHelper.Application.Map;

/// <summary>
/// Owns the map-first read model. Saved servers use a persistent low-cost live session plus an
/// offline map cache; full map downloads remain explicit, and a deterministic source is available
/// when no server has been saved.
/// </summary>
public sealed class MapDashboardService(
    IRustPlusClient demoClient,
    RustPlusConnectionOptions demoConnectionOptions,
    ServerManager servers,
    RustPlusConnectionManager connections,
    RustPlusLiveSessionManager liveSession,
    IMapCacheRepository mapCache,
    MapTopologyManager topologyManager,
    TimeProvider timeProvider) : IDisposable, IAsyncDisposable
{
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly Lock _autoImportLock = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private Task _autoImportTask = Task.CompletedTask;
    private string? _lastAutoImportKey;
    private bool _liveSessionSubscribed;
    private bool _disposed;

    public event EventHandler? StateChanged;

    public MapDashboardState Current { get; private set; } = MapDashboardState.NotStarted;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        EnsureLiveSessionSubscription();
        await _initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Current.ConnectionState is DashboardConnectionState.Loading or DashboardConnectionState.Ready)
            {
                return;
            }

            if (servers.SelectedServerId is { } serverId)
            {
                await LoadServerCoreAsync(serverId, forceMapRefresh: false, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await LoadDemoAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task LoadServerAsync(
        Guid serverId,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LoadServerCoreAsync(serverId, forceRefresh, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public Task RefreshSelectedServerAsync(CancellationToken cancellationToken = default)
    {
        var serverId = Current.ServerId ?? servers.SelectedServerId;
        return serverId is { } selected
            ? LoadServerAsync(selected, forceRefresh: true, cancellationToken)
            : Task.CompletedTask;
    }

    public async Task RefreshLiveDataAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Current.ServerId is not { } serverId || Current.ConnectionState != DashboardConnectionState.Ready)
        {
            return;
        }

        if (liveSession.Current.ServerId == serverId
            && liveSession.Current.Status != RustPlusLiveSessionStatus.Stopped)
        {
            SetState(Current with
            {
                IsLiveDataRefreshing = true,
                LiveDataStatus = "Background refresh requested"
            });
            liveSession.RequestRefresh();
            return;
        }

        await StartLiveSessionAsync(serverId, cancellationToken).ConfigureAwait(false);
    }

    public async Task ImportTopologyAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Current.ServerId is not { } serverId)
        {
            SetState(Current with
            {
                TopologyError = "Save and open a server before importing its Rust .map file."
            });
            return;
        }

        SetState(Current with
        {
            IsTopologyImporting = true,
            TopologyStatus = "Reading external Rust map",
            TopologyError = null
        });

        MapTopologyImportResult result;
        try
        {
            result = await topologyManager.ImportAsync(
                serverId,
                filePath,
                Current.Server?.MapSize,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            SetState(Current with
            {
                IsTopologyImporting = false,
                TopologyStatus = "Map import cancelled"
            });
            return;
        }

        if (!result.IsSuccess || result.Topology is null)
        {
            SetState(Current with
            {
                IsTopologyImporting = false,
                TopologyStatus = "Map import failed",
                TopologyError = SecretRedactor.Redact(result.Message)
            });
            return;
        }

        SetState(Current with
        {
            Topology = result.Topology,
            IsTopologyImporting = false,
            TopologyStatus = result.Message,
            TopologyError = null,
            Layers = BuildLiveLayers(
                Current,
                Current.Team is not null,
                Current.Markers is not null,
                result.Topology)
        });
    }

    public void SetLayerVisibility(MapLayerKind kind, bool isVisible)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var layers = Current.Layers
            .Select(layer => layer.Kind == kind && layer.IsAvailable
                ? layer with { IsVisible = isVisible }
                : layer)
            .ToArray();

        SetState(Current with { Layers = layers });
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCancellation.Cancel();
        try
        {
            if (_liveSessionSubscribed)
            {
                liveSession.StateChanged -= HandleLiveSessionStateChanged;
            }

            await liveSession.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await demoClient.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            Task autoImportTask;
            lock (_autoImportLock)
            {
                autoImportTask = _autoImportTask;
            }

            try
            {
                await autoImportTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Application shutdown cancels any in-progress cache scan or map decode.
            }
        }
        finally
        {
            _lifetimeCancellation.Dispose();
            _initializationLock.Dispose();
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private async Task LoadServerCoreAsync(
        Guid serverId,
        bool forceMapRefresh,
        CancellationToken cancellationToken)
    {
        if (!servers.Select(serverId))
        {
            SetState(MapDashboardState.NotStarted with
            {
                ConnectionState = DashboardConnectionState.Failed,
                ConnectionLabel = "Server unavailable",
                ErrorMessage = "The selected saved server no longer exists."
            });
            return;
        }

        CachedServerMap? cached = null;
        string? cacheWarning = null;
        try
        {
            cached = mapCache.Get(serverId);
        }
        catch (InvalidDataException)
        {
            cacheWarning = "The previous cached map was invalid and will be replaced by a live download.";
        }

        var includeMap = forceMapRefresh || cached is null;
        if (forceMapRefresh && liveSession.Current.ServerId == serverId)
        {
            await liveSession.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!includeMap && cached is not null)
        {
            SetCachedState(cached, null);
            await StartLiveSessionAsync(serverId, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (cached is not null)
        {
            SetCachedState(cached, null);
            SetState(Current with
            {
                IsLiveDataRefreshing = true,
                LiveDataStatus = includeMap ? "Refreshing map and live data" : "Refreshing live data"
            });
        }
        else
        {
            SetState(MapDashboardState.NotStarted with
            {
                ConnectionState = DashboardConnectionState.Loading,
                ConnectionLabel = "Loading Rust+ dashboard",
                ServerId = serverId,
                ErrorMessage = null
            });
        }

        RustPlusDashboardLoadResult result;
        try
        {
            result = await connections.LoadDashboardAsync(serverId, includeMap, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            HandleRefreshException(serverId, cached, exception);
            return;
        }

        if (!result.IsAuthenticated || result.ServerInfo is null)
        {
            var failure = result.ConnectionState.Detail ?? result.ConnectionState.Label;
            if (cached is not null)
            {
                SetState(Current with
                {
                    IsLiveDataRefreshing = false,
                    LiveDataStatus = "Live data unavailable",
                    LiveDataError = failure
                });
                return;
            }

            SetFailedState(serverId, result.ConnectionState.Label, failure);
            return;
        }

        var map = result.Map?.IsSuccess == true ? result.Map.Data : null;
        MapDashboardState baseState;
        if (map is not null)
        {
            var retrievedAtUtc = timeProvider.GetUtcNow();
            var saved = new CachedServerMap(serverId, retrievedAtUtc, result.ServerInfo, map);
            try
            {
                mapCache.Upsert(saved);
            }
            catch (Exception exception)
            {
                cacheWarning = $"The live map loaded, but its offline cache could not be updated ({exception.GetType().Name}).";
            }

            var profile = servers.Profiles.First(profile => profile.Id == serverId);
            baseState = new MapDashboardState(
                DashboardConnectionState.Ready,
                profile.UseFacepunchProxy ? "Live Rust+ · secure proxy" : "Live Rust+ · direct",
                MapDashboardDataSource.Live,
                serverId,
                retrievedAtUtc,
                null,
                false,
                null,
                null,
                result.ServerInfo,
                map,
                null,
                null,
                null,
                [],
                MapDashboardState.CreateLiveMapLayers(),
                cacheWarning);
            baseState = AttachSavedTopology(baseState);
        }
        else if (cached is not null)
        {
            baseState = Current with
            {
                Server = result.ServerInfo,
                ErrorMessage = includeMap
                    ? $"Live map refresh failed; showing the cached map. {DescribeFailure(result.Map)}"
                    : Current.ErrorMessage
            };
        }
        else
        {
            SetFailedState(
                serverId,
                "Map request failed",
                DescribeFailure(result.Map));
            return;
        }

        SetState(MergeLiveData(baseState, result));
        RememberAutoImportKey(serverId, result.ServerInfo);
        await TryAutoImportTopologyAsync(serverId, result.ServerInfo, cancellationToken).ConfigureAwait(false);
        await StartLiveSessionAsync(serverId, cancellationToken).ConfigureAwait(false);
    }

    private async Task TryAutoImportTopologyAsync(
        Guid serverId,
        ServerInfoSnapshot server,
        CancellationToken cancellationToken)
    {
        var profile = servers.Profiles.FirstOrDefault(candidate => candidate.Id == serverId);
        if (profile is null || Current.ServerId != serverId)
        {
            return;
        }

        SetState(Current with
        {
            IsTopologyImporting = true,
            TopologyStatus = "Checking Rust's local map cache",
            TopologyError = null
        });

        MapTopologyAutoImportResult result;
        try
        {
            result = await topologyManager.TryAutoImportAsync(
                serverId,
                profile.Host,
                server,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!_disposed)
            {
                SetState(Current with { IsTopologyImporting = false });
            }

            throw;
        }
        catch (Exception)
        {
            if (!_disposed && Current.ServerId == serverId)
            {
                SetState(Current with
                {
                    IsTopologyImporting = false,
                    TopologyStatus = "Automatic map import failed",
                    TopologyError = "Rust's local map cache could not be processed automatically. Choose a .map file manually."
                });
            }

            return;
        }

        if (_disposed || Current.ServerId != serverId)
        {
            return;
        }

        var topology = result.Topology ?? Current.Topology;
        SetState(Current with
        {
            Topology = topology,
            IsTopologyImporting = false,
            TopologyStatus = result.Message,
            TopologyError = result.IsError ? result.Message : null,
            Layers = BuildLiveLayers(
                Current,
                Current.Team is not null,
                Current.Markers is not null,
                topology)
        });
    }

    private void QueueAutoImportTopology(Guid serverId, ServerInfoSnapshot server)
    {
        var key = AutoImportKey(serverId, server);
        lock (_autoImportLock)
        {
            if (_disposed || string.Equals(_lastAutoImportKey, key, StringComparison.Ordinal))
            {
                return;
            }

            _lastAutoImportKey = key;
            var previousTask = _autoImportTask;
            _autoImportTask = RunQueuedAutoImportAsync(previousTask, serverId, server);
        }
    }

    private async Task RunQueuedAutoImportAsync(
        Task previousTask,
        Guid serverId,
        ServerInfoSnapshot server)
    {
        try
        {
            await previousTask.ConfigureAwait(false);
            if (!_disposed)
            {
                await TryAutoImportTopologyAsync(
                    serverId,
                    server,
                    _lifetimeCancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // Expected during application shutdown.
        }
    }

    private void RememberAutoImportKey(Guid serverId, ServerInfoSnapshot server)
    {
        lock (_autoImportLock)
        {
            _lastAutoImportKey = AutoImportKey(serverId, server);
        }
    }

    private static string AutoImportKey(Guid serverId, ServerInfoSnapshot server) =>
        $"{serverId:N}:{server.MapSize}:{server.Seed}:{server.Salt}:{server.WipeTimeUtc?.UtcTicks}";

    private MapDashboardState MergeLiveData(
        MapDashboardState state,
        RustPlusDashboardLoadResult result)
    {
        var team = result.Team?.IsSuccess == true ? result.Team.Data : state.Team;
        var chat = result.Chat?.IsSuccess == true ? result.Chat.Data : state.Chat;
        var markers = result.Markers?.IsSuccess == true ? result.Markers.Data : state.Markers;
        var errors = new List<string>();
        AddFailure(errors, "Team", result.Team);
        AddFailure(errors, "Chat", result.Chat);
        AddFailure(errors, "Map markers", result.Markers);

        return state with
        {
            ConnectionLabel = result.ConnectionState.Label,
            Server = result.ServerInfo ?? state.Server,
            LiveDataRetrievedAtUtc = timeProvider.GetUtcNow(),
            IsLiveDataRefreshing = false,
            LiveDataStatus = errors.Count == 0 ? "Live data refreshed" : "Live data partially available",
            LiveDataError = errors.Count == 0 ? null : string.Join(" ", errors),
            Team = team,
            Chat = chat,
            Markers = markers,
            Layers = BuildLiveLayers(state, team is not null, markers is not null, state.Topology)
        };
    }

    private async Task LoadDemoAsync(CancellationToken cancellationToken)
    {
        SetState(Current with
        {
            ConnectionState = DashboardConnectionState.Loading,
            ConnectionLabel = "Loading demo session",
            DataSource = MapDashboardDataSource.Fake,
            ErrorMessage = null
        });

        try
        {
            await demoClient.ConnectAsync(demoConnectionOptions, cancellationToken).ConfigureAwait(false);

            var server = Require(await demoClient.GetServerInfoAsync(cancellationToken).ConfigureAwait(false), "server information");
            var map = Require(await demoClient.GetMapAsync(cancellationToken).ConfigureAwait(false), "map");
            var team = Require(await demoClient.GetTeamAsync(cancellationToken).ConfigureAwait(false), "team information");
            var chat = Require(await demoClient.GetTeamChatAsync(cancellationToken).ConfigureAwait(false), "team chat");
            var markers = Require(await demoClient.GetMapMarkersAsync(cancellationToken).ConfigureAwait(false), "map markers");
            var now = timeProvider.GetUtcNow();

            SetState(new MapDashboardState(
                DashboardConnectionState.Ready,
                "Demo connected",
                MapDashboardDataSource.Fake,
                null,
                now,
                now,
                false,
                "Deterministic fake snapshot",
                null,
                server,
                map,
                team,
                chat,
                markers,
                [],
                Current.Layers,
                null));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            SetState(Current with
            {
                ConnectionState = DashboardConnectionState.Failed,
                ConnectionLabel = "Demo unavailable",
                ErrorMessage = SecretRedactor.Redact(exception.Message)
            });
        }
    }

    private void SetCachedState(CachedServerMap cached, string? warning)
    {
        var preserveLiveState = Current.ServerId == cached.ServerId;
        var team = preserveLiveState ? Current.Team : null;
        var chat = preserveLiveState ? Current.Chat : null;
        var markers = preserveLiveState ? Current.Markers : null;
        var state = new MapDashboardState(
            DashboardConnectionState.Ready,
            "Cached map · refreshing live data",
            MapDashboardDataSource.Cache,
            cached.ServerId,
            cached.RetrievedAtUtc,
            preserveLiveState ? Current.LiveDataRetrievedAtUtc : null,
            preserveLiveState && Current.IsLiveDataRefreshing,
            preserveLiveState ? Current.LiveDataStatus : null,
            preserveLiveState ? Current.LiveDataError : null,
            cached.Server,
            cached.Map,
            team,
            chat,
            markers,
            preserveLiveState ? Current.Events : [],
            BuildLiveLayers(Current, team is not null, markers is not null),
            warning);
        SetState(AttachSavedTopology(state));
    }

    private void HandleRefreshException(Guid serverId, CachedServerMap? cached, Exception exception)
    {
        var safeMessage = SecretRedactor.Redact(exception.Message);
        if (cached is not null)
        {
            SetState(Current with
            {
                IsLiveDataRefreshing = false,
                LiveDataStatus = "Live refresh failed",
                LiveDataError = safeMessage
            });
            return;
        }

        SetFailedState(serverId, "Rust+ refresh failed", safeMessage);
    }

    private void SetFailedState(Guid serverId, string label, string detail)
    {
        SetState(MapDashboardState.NotStarted with
        {
            ConnectionState = DashboardConnectionState.Failed,
            ConnectionLabel = label,
            ServerId = serverId,
            ErrorMessage = SecretRedactor.Redact(detail)
        });
    }

    private void SetState(MapDashboardState state)
    {
        Current = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task StartLiveSessionAsync(Guid serverId, CancellationToken cancellationToken)
    {
        EnsureLiveSessionSubscription();
        var seed = new RustPlusLiveSessionSeed(
            Current.Server,
            Current.Team,
            Current.Chat,
            Current.Markers,
            Current.LiveDataRetrievedAtUtc);
        await liveSession.StartAsync(serverId, seed, cancellationToken).ConfigureAwait(false);
    }

    private void EnsureLiveSessionSubscription()
    {
        if (_liveSessionSubscribed)
        {
            return;
        }

        liveSession.StateChanged += HandleLiveSessionStateChanged;
        _liveSessionSubscribed = true;
    }

    private void HandleLiveSessionStateChanged(object? sender, EventArgs args)
    {
        var live = liveSession.Current;
        if (_disposed || live.ServerId is null || live.ServerId != Current.ServerId)
        {
            return;
        }

        var team = live.Team ?? Current.Team;
        var chat = live.Chat ?? Current.Chat;
        var markers = live.Markers ?? Current.Markers;
        SetState(Current with
        {
            ConnectionLabel = live.Label,
            Server = live.Server ?? Current.Server,
            Team = team,
            Chat = chat,
            Markers = markers,
            LiveDataRetrievedAtUtc = live.LastRefreshUtc ?? Current.LiveDataRetrievedAtUtc,
            IsLiveDataRefreshing = live.Status is RustPlusLiveSessionStatus.Connecting
                or RustPlusLiveSessionStatus.Reconnecting,
            LiveDataStatus = live.Label,
            LiveDataError = live.Error,
            Events = live.Events,
            Layers = BuildLiveLayers(
                Current with { Events = live.Events },
                team is not null,
                markers is not null,
                Current.Topology)
        });

        if (live.Status == RustPlusLiveSessionStatus.Connected && live.Server is not null)
        {
            QueueAutoImportTopology(live.ServerId.Value, live.Server);
        }
    }

    private static string DescribeFailure<T>(RustPlusResult<T>? result) =>
        result?.Error is { } error
            ? $"Rust+ request failed ({error.Code})."
            : "Rust+ returned no data.";

    private static void AddFailure<T>(ICollection<string> errors, string label, RustPlusResult<T>? result)
    {
        if (result?.IsSuccess == true)
        {
            return;
        }

        errors.Add($"{label} unavailable ({result?.Error?.Code ?? "unknown_error"}).");
    }

    private static IReadOnlyList<MapLayerState> BuildLiveLayers(
        MapDashboardState previous,
        bool teamAvailable,
        bool markersAvailable,
        SavedMapTopology? topology = null)
    {
        var deathHistoryAvailable = previous.Events.Any(item =>
            item.Kind == CompanionEventKind.TeamMemberDied
            && item.Position is not null
            && (previous.Server?.WipeTimeUtc is null
                || item.OccurredAtUtc >= previous.Server.WipeTimeUtc));
        var layers = MapDashboardState.CreateLiveMapLayers(
            teamAvailable,
            markersAvailable,
            deathHistoryAvailable,
            topology);
        return layers.Select(layer =>
        {
            var existing = previous.Layers.FirstOrDefault(candidate => candidate.Kind == layer.Kind);
            return existing?.IsAvailable == true && layer.IsAvailable
                ? layer with { IsVisible = existing.IsVisible }
                : layer;
        }).ToArray();
    }

    private MapDashboardState AttachSavedTopology(MapDashboardState state)
    {
        if (state.ServerId is not { } serverId)
        {
            return state;
        }

        SavedMapTopology? topology;
        try
        {
            topology = topologyManager.Get(serverId);
        }
        catch (InvalidDataException)
        {
            return state with
            {
                TopologyError = "The saved topology cache is invalid. Import the server's .map file again."
            };
        }

        if (topology is null)
        {
            return state;
        }

        if (state.Server?.MapSize is { } mapSize && topology.Data.WorldSize != mapSize)
        {
            return state with
            {
                TopologyError = "The saved topology has a different world size and was not overlaid. Import the current .map file."
            };
        }

        return state with
        {
            Topology = topology,
            TopologyStatus = $"External map imported {topology.ImportedAtUtc.ToLocalTime():dd MMM · HH:mm}",
            Layers = BuildLiveLayers(
                state,
                state.Team is not null,
                state.Markers is not null,
                topology)
        };
    }

    private static T Require<T>(RustPlusResult<T> result, string operation)
    {
        if (result.IsSuccess && result.Data is not null)
        {
            return result.Data;
        }

        var code = result.Error?.Code ?? "unknown_error";
        var message = result.Error?.Message ?? "No response data was returned.";
        throw new InvalidOperationException($"Failed to load {operation} ({code}): {message}");
    }
}
