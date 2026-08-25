using RustPlusHelper.Application.Identity;
using RustPlusHelper.Application.Notifications;
using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Application.Security;
using RustPlusHelper.Application.Servers;
using RustPlusHelper.Application.Testing;

namespace RustPlusHelper.Tests;

public sealed class NotificationPreferencesTests
{
    [Fact]
    public void DefaultsToAllCategoriesEnabledWhenNothingStored()
    {
        using var secrets = new InMemoryApplicationSecretStore();
        var store = new NotificationPreferencesStore(secrets);

        Assert.Equal(NotificationPreferences.Default, store.Get());
    }

    [Fact]
    public void RoundTripsEachCategoryIndependently()
    {
        using var secrets = new InMemoryApplicationSecretStore();
        var store = new NotificationPreferencesStore(secrets);
        var preferences = new NotificationPreferences(
            ConnectionEvents: false,
            TeamEvents: true,
            MarkerEvents: false,
            VendingEvents: true,
            AlarmEvents: false);

        store.Save(preferences);

        Assert.Equal(preferences, store.Get());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoundTripsPlaySoundIndependently(bool playSound)
    {
        using var secrets = new InMemoryApplicationSecretStore();
        var store = new NotificationPreferencesStore(secrets);

        store.Save(NotificationPreferences.Default with { PlaySound = playSound });

        Assert.Equal(playSound, store.Get().PlaySound);
    }

    [Fact]
    public void PreferencesSavedBeforePlaySoundExistedDefaultToSoundOn()
    {
        using var secrets = new InMemoryApplicationSecretStore();
        // The 5 original category flags packed with no 6th (mute) bit — simulates a byte saved by a
        // version of the app that predates the sound feature.
        secrets.Store(ApplicationSecretKind.NotificationPreferences, [0x1F]);
        var store = new NotificationPreferencesStore(secrets);

        Assert.True(store.Get().PlaySound);
    }

    [Fact]
    public void RoundTripsQuietHoursWindow()
    {
        using var secrets = new InMemoryApplicationSecretStore();
        var store = new NotificationPreferencesStore(secrets);
        var preferences = NotificationPreferences.Default with
        {
            QuietHoursEnabled = true,
            QuietHoursStart = new TimeOnly(22, 30),
            QuietHoursEnd = new TimeOnly(7, 15)
        };

        store.Save(preferences);

        Assert.Equal(preferences, store.Get());
    }

    [Fact]
    public void PreferencesSavedBeforeQuietHoursExistedDefaultToDisabled()
    {
        using var secrets = new InMemoryApplicationSecretStore();
        // A single flags byte with no quiet-hours window bytes — simulates a value saved by a
        // version of the app that predates quiet hours (and also predates PlaySound, which the same
        // byte length covers).
        secrets.Store(ApplicationSecretKind.NotificationPreferences, [0x1F]);
        var store = new NotificationPreferencesStore(secrets);

        var preferences = store.Get();
        Assert.False(preferences.QuietHoursEnabled);
        Assert.Equal(default, preferences.QuietHoursStart);
        Assert.Equal(default, preferences.QuietHoursEnd);
    }

    [Theory]
    [InlineData(false, 23, 0, false)] // disabled window never applies regardless of time
    [InlineData(true, 23, 0, true)] // 22:00-07:00 window, non-wrapping check at 23:00 -> inside
    [InlineData(true, 3, 0, true)] // same window, past midnight at 03:00 -> inside (wraps)
    [InlineData(true, 12, 0, false)] // midday -> outside
    [InlineData(true, 22, 0, true)] // exactly at start -> inclusive
    [InlineData(true, 7, 0, false)] // exactly at end -> exclusive
    public void IsQuietHoursHandlesAWindowThatWrapsPastMidnight(
        bool enabled,
        int hour,
        int minute,
        bool expected)
    {
        var preferences = NotificationPreferences.Default with
        {
            QuietHoursEnabled = enabled,
            QuietHoursStart = new TimeOnly(22, 0),
            QuietHoursEnd = new TimeOnly(7, 0)
        };

        Assert.Equal(expected, preferences.IsQuietHours(new TimeOnly(hour, minute)));
    }

    [Theory]
    [InlineData(CompanionEventKind.ConnectionEstablished, true)]
    [InlineData(CompanionEventKind.TeamMemberDied, true)]
    [InlineData(CompanionEventKind.MarkerAppeared, false)]
    [InlineData(CompanionEventKind.OilRigActivated, false)]
    [InlineData(CompanionEventKind.VendingStockChanged, true)]
    [InlineData(CompanionEventKind.AlarmTriggered, false)]
    public void IsEnabledRoutesEachKindToItsOwnCategory(CompanionEventKind kind, bool categoryEnabled)
    {
        var preferences = kind switch
        {
            CompanionEventKind.ConnectionEstablished => NotificationPreferences.Default with { ConnectionEvents = categoryEnabled },
            CompanionEventKind.TeamMemberDied => NotificationPreferences.Default with { TeamEvents = categoryEnabled },
            CompanionEventKind.MarkerAppeared or CompanionEventKind.OilRigActivated =>
                NotificationPreferences.Default with { MarkerEvents = categoryEnabled },
            CompanionEventKind.VendingStockChanged => NotificationPreferences.Default with { VendingEvents = categoryEnabled },
            CompanionEventKind.AlarmTriggered => NotificationPreferences.Default with { AlarmEvents = categoryEnabled },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        Assert.Equal(categoryEnabled, preferences.IsEnabled(kind));
    }
}

public sealed class RustPlusAlarmNotificationListenerTests
{
    [Fact]
    public async Task MatchesTriggeredAlarmToServerByRustPlusServerIdAndRecordsEvent()
    {
        using var secrets = new InMemoryApplicationSecretStore();
        secrets.Store(ApplicationSecretKind.RustPlusFcmCredentials, "sanitized-fcm-credentials"u8);
        var rustPlusServerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var servers = CreateServerManager();
        var profile = servers.Save(new ServerProfileDraft(
            null, "Base", "companion.invalid", 28082, true, 76561198000000000, rustPlusServerId));
        var history = new InMemoryCompanionEventRepository();
        var liveSession = CreateLiveSession(servers, history);
        var provider = new FakeAlarmListenerProvider();
        await using var listener = new RustPlusAlarmNotificationListener(
            provider, secrets, servers, liveSession, PollingOptions());

        var toasts = new List<AlarmToastNotification>();
        listener.AlarmTriggered += (_, toast) => toasts.Add(toast);
        listener.Load();
        await provider.WaitForConnectionAsync();

        provider.TriggerAlarm(new AlarmTriggeredCapture(rustPlusServerId, "Base under attack", "Raiders detected", "persistent-1"));
        await WaitUntilAsync(() => toasts.Count > 0);

        var toast = Assert.Single(toasts);
        Assert.Equal(profile.Id, toast.ServerId);
        Assert.Contains(history.GetRecent(profile.Id, 200), item =>
            item.Kind == CompanionEventKind.AlarmTriggered && item.Title == "Base under attack");
    }

    [Fact]
    public async Task UnmatchedRustPlusServerIdStillNotifiesWithoutFabricatingACompanionEvent()
    {
        using var secrets = new InMemoryApplicationSecretStore();
        secrets.Store(ApplicationSecretKind.RustPlusFcmCredentials, "sanitized-fcm-credentials"u8);
        var servers = CreateServerManager();
        var liveSession = CreateLiveSession(servers);
        var provider = new FakeAlarmListenerProvider();
        await using var listener = new RustPlusAlarmNotificationListener(
            provider, secrets, servers, liveSession, PollingOptions());

        var toasts = new List<AlarmToastNotification>();
        listener.AlarmTriggered += (_, toast) => toasts.Add(toast);
        listener.Load();
        await provider.WaitForConnectionAsync();

        provider.TriggerAlarm(new AlarmTriggeredCapture(Guid.NewGuid(), "Unknown server alarm", "Message", "persistent-2"));
        await WaitUntilAsync(() => toasts.Count > 0);

        var toast = Assert.Single(toasts);
        Assert.Null(toast.ServerId);
        Assert.Empty(liveSession.Current.Events);
    }

    [Fact]
    public async Task PersistentIdsSurviveAReconnectAndSeedTheNextConnectionAttempt()
    {
        using var secrets = new InMemoryApplicationSecretStore();
        secrets.Store(ApplicationSecretKind.RustPlusFcmCredentials, "sanitized-fcm-credentials"u8);
        var servers = CreateServerManager();
        var liveSession = CreateLiveSession(servers);
        var provider = new FakeAlarmListenerProvider();
        await using var listener = new RustPlusAlarmNotificationListener(
            provider, secrets, servers, liveSession, PollingOptions());
        listener.Load();
        await provider.WaitForConnectionAsync();

        provider.ReceivePersistentId("already-seen-id");
        await WaitUntilAsync(() => secrets.Contains(ApplicationSecretKind.AlarmFcmPersistentIds));

        provider.EndCurrentAttempt();
        await provider.WaitForConnectionAsync(attempt: 2);

        Assert.Contains("already-seen-id", provider.Attempts[1]);
    }

    private static ServerManager CreateServerManager()
    {
        var repository = new InMemoryServerRepository();
        var secrets = new InMemorySecretStore();
        var identity = new PlayerIdentityManager(new InMemoryPlayerIdentityRepository(), TimeProvider.System, repository, secrets);
        return new ServerManager(repository, TimeProvider.System, secrets, identity);
    }

    private static RustPlusLiveSessionManager CreateLiveSession(
        ServerManager servers,
        ICompanionEventRepository? eventRepository = null) => new(
        new RustPlusSavedConnectionResolver(servers, new InMemorySecretStore()),
        new NeverConnectingClientFactory(),
        TimeProvider.System,
        PollingOptions(),
        eventRepository ?? new InMemoryCompanionEventRepository(),
        new InMemoryPairedEntityRepository(),
        new InMemoryMovementTrailRepository());

    private static RustPlusPollingOptions PollingOptions() => new(
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(1),
        TimeSpan.FromSeconds(1),
        [TimeSpan.FromMilliseconds(5)],
        TimeSpan.FromHours(1));

    private static Task WaitUntilAsync(Func<bool> condition) =>
        AsyncTestHelpers.WaitUntilAsync(condition, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(5));

    private sealed class NeverConnectingClientFactory : IRustPlusClientFactory
    {
        public IRustPlusClient Create() => throw new InvalidOperationException("Not used by these tests.");
    }

    private sealed class FakeAlarmListenerProvider : IRustPlusAlarmListenerProvider
    {
        private readonly List<TaskCompletionSource> _connectionSignals = [];
        private TaskCompletionSource _endCurrentAttempt = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Action<AlarmTriggeredCapture>? _onAlarmTriggered;
        private Action<string>? _onPersistentIdReceived;

        public List<IReadOnlyCollection<string>> Attempts { get; } = [];

        public async Task RunAsync(
            ReadOnlyMemory<byte> credentials,
            IReadOnlyCollection<string> seedPersistentIds,
            Action<AlarmTriggeredCapture> onAlarmTriggered,
            Action<string> onPersistentIdReceived,
            CancellationToken cancellationToken = default)
        {
            Attempts.Add(seedPersistentIds);
            _onAlarmTriggered = onAlarmTriggered;
            _onPersistentIdReceived = onPersistentIdReceived;
            var endSignal = _endCurrentAttempt;
            lock (_connectionSignals)
            {
                foreach (var signal in _connectionSignals)
                {
                    signal.TrySetResult();
                }
            }

            using var registration = cancellationToken.Register(() => endSignal.TrySetCanceled(cancellationToken));
            await endSignal.Task.ConfigureAwait(false);
        }

        public Task WaitForConnectionAsync(int attempt = 1)
        {
            lock (_connectionSignals)
            {
                if (Attempts.Count >= attempt)
                {
                    return Task.CompletedTask;
                }

                var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _connectionSignals.Add(signal);
                return signal.Task;
            }
        }

        public void TriggerAlarm(AlarmTriggeredCapture capture) => _onAlarmTriggered?.Invoke(capture);

        public void ReceivePersistentId(string id) => _onPersistentIdReceived?.Invoke(id);

        public void EndCurrentAttempt()
        {
            var previous = _endCurrentAttempt;
            _endCurrentAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            previous.TrySetResult();
        }
    }
}

public sealed class NotificationDispatcherTests
{
    [Fact]
    public void QuietHoursSuppressesTheNotification()
    {
        using var secrets = new InMemoryApplicationSecretStore();
        var preferencesStore = new NotificationPreferencesStore(secrets);
        preferencesStore.Save(NotificationPreferences.Default with
        {
            QuietHoursEnabled = true,
            QuietHoursStart = new TimeOnly(22, 0),
            QuietHoursEnd = new TimeOnly(7, 0)
        });
        var servers = CreateServerManager();
        var liveSession = CreateLiveSession(servers);
        using var alarmListener = new RustPlusAlarmNotificationListener(
            new NoopAlarmListenerProvider(), secrets, servers, liveSession, PollingOptions());
        var notifier = new RecordingNotifier();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 23, 0, 0, TimeSpan.Zero));
        using var dispatcher = new NotificationDispatcher(liveSession, alarmListener, preferencesStore, notifier, clock);

        liveSession.RecordExternalEvent(Guid.NewGuid(), CompanionEventKind.ConnectionEstablished, "Connected");

        Assert.Empty(notifier.Shown);
    }

    [Fact]
    public void OutsideTheQuietHoursWindowNotificationsStillShow()
    {
        using var secrets = new InMemoryApplicationSecretStore();
        var preferencesStore = new NotificationPreferencesStore(secrets);
        preferencesStore.Save(NotificationPreferences.Default with
        {
            QuietHoursEnabled = true,
            QuietHoursStart = new TimeOnly(22, 0),
            QuietHoursEnd = new TimeOnly(7, 0)
        });
        var servers = CreateServerManager();
        var liveSession = CreateLiveSession(servers);
        using var alarmListener = new RustPlusAlarmNotificationListener(
            new NoopAlarmListenerProvider(), secrets, servers, liveSession, PollingOptions());
        var notifier = new RecordingNotifier();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        using var dispatcher = new NotificationDispatcher(liveSession, alarmListener, preferencesStore, notifier, clock);

        liveSession.RecordExternalEvent(Guid.NewGuid(), CompanionEventKind.ConnectionEstablished, "Connected");

        Assert.Single(notifier.Shown);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [Fact]
    public void ShowsANotificationForAnEnabledCategoryAndSkipsADisabledOne()
    {
        using var secrets = new InMemoryApplicationSecretStore();
        var preferencesStore = new NotificationPreferencesStore(secrets);
        preferencesStore.Save(NotificationPreferences.Default with { MarkerEvents = false });
        var servers = CreateServerManager();
        var liveSession = CreateLiveSession(servers);
        using var alarmListener = new RustPlusAlarmNotificationListener(
            new NoopAlarmListenerProvider(), secrets, servers, liveSession, PollingOptions());
        var notifier = new RecordingNotifier();
        using var dispatcher = new NotificationDispatcher(liveSession, alarmListener, preferencesStore, notifier, TimeProvider.System);

        liveSession.RecordExternalEvent(Guid.NewGuid(), CompanionEventKind.ConnectionEstablished, "Connected");
        liveSession.RecordExternalEvent(Guid.NewGuid(), CompanionEventKind.MarkerAppeared, "Cargo ship appeared");

        Assert.Single(notifier.Shown, shown => shown.Title == "Connected");
        Assert.DoesNotContain(notifier.Shown, shown => shown.Title == "Cargo ship appeared");
    }

    [Fact]
    public void PassesThePlaySoundPreferenceThroughToTheNotifier()
    {
        using var secrets = new InMemoryApplicationSecretStore();
        var preferencesStore = new NotificationPreferencesStore(secrets);
        preferencesStore.Save(NotificationPreferences.Default with { PlaySound = false });
        var servers = CreateServerManager();
        var liveSession = CreateLiveSession(servers);
        using var alarmListener = new RustPlusAlarmNotificationListener(
            new NoopAlarmListenerProvider(), secrets, servers, liveSession, PollingOptions());
        var notifier = new RecordingNotifier();
        using var dispatcher = new NotificationDispatcher(liveSession, alarmListener, preferencesStore, notifier, TimeProvider.System);

        liveSession.RecordExternalEvent(Guid.NewGuid(), CompanionEventKind.ConnectionEstablished, "Connected");

        Assert.False(Assert.Single(notifier.Shown).PlaySound);
    }

    [Fact]
    public async Task UnattributedAlarmNotifiesOnceWithoutADuplicateFromEventRecorded()
    {
        using var secrets = new InMemoryApplicationSecretStore();
        secrets.Store(ApplicationSecretKind.RustPlusFcmCredentials, "sanitized-fcm-credentials"u8);
        var preferencesStore = new NotificationPreferencesStore(secrets);
        var servers = CreateServerManager();
        var liveSession = CreateLiveSession(servers);
        var provider = new TriggerableAlarmListenerProvider();
        await using var alarmListener = new RustPlusAlarmNotificationListener(
            provider, secrets, servers, liveSession, PollingOptions());
        var notifier = new RecordingNotifier();
        using var dispatcher = new NotificationDispatcher(liveSession, alarmListener, preferencesStore, notifier, TimeProvider.System);

        alarmListener.Load();
        await provider.WaitForConnectionAsync();
        provider.Trigger(new AlarmTriggeredCapture(Guid.NewGuid(), "Unknown alarm", "Message", "id"));
        await WaitUntilAsync(() => notifier.Shown.Count > 0);

        Assert.Single(notifier.Shown, shown => shown.Title == "Unknown alarm");
    }

    private static Task WaitUntilAsync(Func<bool> condition) =>
        AsyncTestHelpers.WaitUntilAsync(condition, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(5));

    private static ServerManager CreateServerManager()
    {
        var repository = new InMemoryServerRepository();
        var secrets = new InMemorySecretStore();
        var identity = new PlayerIdentityManager(new InMemoryPlayerIdentityRepository(), TimeProvider.System, repository, secrets);
        return new ServerManager(repository, TimeProvider.System, secrets, identity);
    }

    private static RustPlusLiveSessionManager CreateLiveSession(
        ServerManager servers,
        ICompanionEventRepository? eventRepository = null) => new(
        new RustPlusSavedConnectionResolver(servers, new InMemorySecretStore()),
        new NeverConnectingClientFactory(),
        TimeProvider.System,
        PollingOptions(),
        eventRepository ?? new InMemoryCompanionEventRepository(),
        new InMemoryPairedEntityRepository(),
        new InMemoryMovementTrailRepository());

    private static RustPlusPollingOptions PollingOptions() => new(
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(1),
        TimeSpan.FromSeconds(1),
        [TimeSpan.FromMilliseconds(5)],
        TimeSpan.FromHours(1));

    private sealed class NeverConnectingClientFactory : IRustPlusClientFactory
    {
        public IRustPlusClient Create() => throw new InvalidOperationException("Not used by these tests.");
    }

    private sealed class NoopAlarmListenerProvider : IRustPlusAlarmListenerProvider
    {
        public Task RunAsync(
            ReadOnlyMemory<byte> credentials,
            IReadOnlyCollection<string> seedPersistentIds,
            Action<AlarmTriggeredCapture> onAlarmTriggered,
            Action<string> onPersistentIdReceived,
            CancellationToken cancellationToken = default) => Task.Delay(Timeout.Infinite, cancellationToken);
    }

    private sealed class TriggerableAlarmListenerProvider : IRustPlusAlarmListenerProvider
    {
        private readonly TaskCompletionSource _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Action<AlarmTriggeredCapture>? _onAlarmTriggered;

        public async Task RunAsync(
            ReadOnlyMemory<byte> credentials,
            IReadOnlyCollection<string> seedPersistentIds,
            Action<AlarmTriggeredCapture> onAlarmTriggered,
            Action<string> onPersistentIdReceived,
            CancellationToken cancellationToken = default)
        {
            _onAlarmTriggered = onAlarmTriggered;
            _connected.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }

        public Task WaitForConnectionAsync() => _connected.Task;

        public void Trigger(AlarmTriggeredCapture capture) => _onAlarmTriggered?.Invoke(capture);
    }

    private sealed class RecordingNotifier : IDesktopNotifier
    {
        public List<(string Title, string Message, bool PlaySound)> Shown { get; } = [];

        public void Show(string title, string message, bool playSound = false) => Shown.Add((title, message, playSound));
    }
}
