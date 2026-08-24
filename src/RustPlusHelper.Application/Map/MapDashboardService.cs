using RustPlusHelper.Application.Concurrency;
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

    private readonly MutableStateBox<MapDashboardState> _state = new(MapDashboardState.NotStarted);

    public event EventHandler? StateChanged;

    public MapDashboardState Current => _state.Value;

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
            UpdateState(current => current with
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
            UpdateState(current => current with
            {
                TopologyError = "Save and open a server before importing its Rust .map file."
            });
            return;
        }

        UpdateState(current => current with
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
            UpdateState(current => current with
            {
                IsTopologyImporting = false,
                TopologyStatus = "Map import cancelled"
            });
            return;
        }

        if (!result.IsSuccess || result.Topology is null)
        {
            UpdateState(current => current with
            {
                IsTopologyImporting = false,
                TopologyStatus = "Map import failed",
                TopologyError = SecretRedactor.Redact(result.Message)
            });
            return;
        }

        UpdateState(current => current with
        {
            Topology = result.Topology,
            IsTopologyImporting = false,
            TopologyStatus = result.Message,
            TopologyError = null,
            Layers = BuildLiveLayers(
                current,
                current.Team is not null,
                current.Markers is not null,
                result.Topology)
        });
    }

    public void SetLayerVisibility(MapLayerKind kind, bool isVisible)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        UpdateState(current => current with
        {
            Layers = current.Layers
                .Select(layer => layer.Kind == kind && layer.IsAvailable
                    ? layer with { IsVisible = isVisible }
                    : layer)
                .ToArray()
        });
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
            UpdateState(current => current with
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
                UpdateState(current => current with
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
            var baseState = new MapDashboardState(
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

            // baseState here doesn't derive from Current, so a plain (still atomically-written) SetState
            // is fine — unlike the cached-only branch below, there's no prior-state read to race on.
            SetState(MergeLiveData(baseState, result));
        }
        else if (cached is not null)
        {
            UpdateState(current => MergeLiveData(current with
            {
                Server = result.ServerInfo,
                ErrorMessage = includeMap
                    ? $"Live map refresh failed; showing the cached map. {DescribeFailure(result.Map)}"
                    : current.ErrorMessage
            }, result));
        }
        else
        {
            SetFailedState(
                serverId,
                "Map request failed",
                DescribeFailure(result.Map));
            return;
        }

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

        UpdateState(current => current.ServerId != serverId
            ? current
            : current with
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
                UpdateState(current => current.ServerId != serverId
                    ? current
                    : current with { IsTopologyImporting = false });
            }

            throw;
        }
        catch (Exception)
        {
            if (!_disposed)
            {
                UpdateState(current => current.ServerId != serverId
                    ? current
                    : current with
                    {
                        IsTopologyImporting = false,
                        TopologyStatus = "Automatic map import failed",
                        TopologyError = "Rust's local map cache could not be processed automatically. Choose a .map file manually."
                    });
            }

            return;
        }

        if (_disposed)
        {
            return;
        }

        UpdateState(current => current.ServerId != serverId
            ? current
            : current with
            {
                Topology = result.Topology ?? current.Topology,
                IsTopologyImporting = false,
                TopologyStatus = result.Message,
                TopologyError = result.IsError ? result.Message : null,
                Layers = BuildLiveLayers(
                    current,
                    current.Team is not null,
                    current.Markers is not null,
                    result.Topology ?? current.Topology)
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
        UpdateState(current => current with
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

            UpdateState(current => new MapDashboardState(
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
                current.Layers,
                null));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            UpdateState(current => current with
            {
                ConnectionState = DashboardConnectionState.Failed,
                ConnectionLabel = "Demo unavailable",
                ErrorMessage = SecretRedactor.Redact(exception.Message)
            });
        }
    }

    private void SetCachedState(CachedServerMap cached, string? warning) =>
        UpdateState(current => AttachSavedTopology(ComputeCachedState(current, cached, warning)));

    private static MapDashboardState ComputeCachedState(
        MapDashboardState current,
        CachedServerMap cached,
        string? warning)
    {
        var preserveLiveState = current.ServerId == cached.ServerId;
        var team = preserveLiveState ? current.Team : null;
        var chat = preserveLiveState ? current.Chat : null;
        var markers = preserveLiveState ? current.Markers : null;
        return new MapDashboardState(
            DashboardConnectionState.Ready,
            "Cached map · refreshing live data",
            MapDashboardDataSource.Cache,
            cached.ServerId,
            cached.RetrievedAtUtc,
            preserveLiveState ? current.LiveDataRetrievedAtUtc : null,
            preserveLiveState && current.IsLiveDataRefreshing,
            preserveLiveState ? current.LiveDataStatus : null,
            preserveLiveState ? current.LiveDataError : null,
            cached.Server,
            cached.Map,
            team,
            chat,
            markers,
            preserveLiveState ? current.Events : [],
            BuildLiveLayers(current, team is not null, markers is not null),
            warning);
    }

    private void HandleRefreshException(Guid serverId, CachedServerMap? cached, Exception exception)
    {
        var safeMessage = SecretRedactor.Redact(exception.Message);
        if (cached is not null)
        {
            UpdateState(current => current with
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

    /// <summary>
    /// Overwrites the state outright. Only safe when <paramref name="state"/> does not derive any of
    /// its fields from <see cref="Current"/> — otherwise use <see cref="UpdateState"/> so the read of
    /// the prior state and the write of the new one happen atomically relative to every other writer
    /// (in particular <see cref="HandleLiveSessionStateChanged"/>, which runs on a different thread).
    /// </summary>
    private void SetState(MapDashboardState state) => UpdateState(_ => state);

    /// <summary>
    /// Atomically reads <see cref="Current"/>, computes the next state from it via
    /// <paramref name="updater"/>, and stores the result — use this instead of
    /// <c>SetState(Current with {...})</c> any time the new state is derived from the old one, so a
    /// concurrent updater can't compute from the same now-stale snapshot and silently clobber this
    /// change (or have this change clobber theirs).
    /// </summary>
    private void UpdateState(Func<MapDashboardState, MapDashboardState> updater)
    {
        _state.Update(updater);
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

    // The one place this class historically raced against itself: this fires from
    // RustPlusLiveSessionManager.StateChanged, which runs on whatever thread the live-session poll
    // loop or FCM listener is currently on — never the thread driving InitializeAsync/LoadServerAsync.
    // Both sides read-then-write Current, so the read (of live.ServerId vs Current.ServerId) and the
    // write must happen as one atomic step via UpdateState, or a concurrent LoadServerCoreAsync write
    // sandwiched between this method's read and its write would get silently discarded.
    private void HandleLiveSessionStateChanged(object? sender, EventArgs args)
    {
        var live = liveSession.Current;
        if (_disposed || live.ServerId is null)
        {
            return;
        }

        UpdateState(current =>
        {
            if (live.ServerId != current.ServerId)
            {
                return current;
            }

            var team = live.Team ?? current.Team;
            var chat = live.Chat ?? current.Chat;
            var markers = live.Markers ?? current.Markers;
            return current with
            {
                ConnectionLabel = live.Label,
                Server = live.Server ?? current.Server,
                Team = team,
                Chat = chat,
                Markers = markers,
                LiveDataRetrievedAtUtc = live.LastRefreshUtc ?? current.LiveDataRetrievedAtUtc,
                IsLiveDataRefreshing = live.Status is RustPlusLiveSessionStatus.Connecting
                    or RustPlusLiveSessionStatus.Reconnecting,
                LiveDataStatus = live.Label,
                LiveDataError = live.Error,
                Events = live.Events,
                Layers = BuildLiveLayers(
                    current with { Events = live.Events },
                    team is not null,
                    markers is not null,
                    current.Topology)
            };
        });

        // Re-checked against the post-update value rather than reusing the pre-update guard above:
        // if the update no-opped because the server had already changed, this must not queue an
        // auto-import against the now-stale live.ServerId either.
        if (live.Status == RustPlusLiveSessionStatus.Connected
            && live.Server is not null
            && Current.ServerId == live.ServerId)
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
