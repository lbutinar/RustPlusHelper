using RustPlusHelper.Application.RustPlus;

namespace RustPlusHelper.Application.Notifications;

/// <summary>
/// Subscribes to every source of a user-facing companion event — <see cref="RustPlusLiveSessionManager.EventRecorded"/>
/// (this manager's own polling/diffing, plus any alarm attributed to a saved server via
/// <see cref="RustPlusLiveSessionManager.RecordExternalEvent"/>) and <see cref="RustPlusAlarmNotificationListener.AlarmTriggered"/>
/// (for the fallback case where an alarm couldn't be attributed to a saved server, so no
/// <see cref="CompanionEvent"/> exists to key off of) — and shows a desktop notification for each,
/// gated by <see cref="NotificationPreferences"/>.
/// </summary>
public sealed class NotificationDispatcher : IDisposable
{
    private readonly RustPlusLiveSessionManager _liveSession;
    private readonly RustPlusAlarmNotificationListener _alarmListener;
    private readonly NotificationPreferencesStore _preferencesStore;
    private readonly IDesktopNotifier _notifier;
    private readonly TimeProvider _timeProvider;

    public NotificationDispatcher(
        RustPlusLiveSessionManager liveSession,
        RustPlusAlarmNotificationListener alarmListener,
        NotificationPreferencesStore preferencesStore,
        IDesktopNotifier notifier,
        TimeProvider timeProvider)
    {
        _liveSession = liveSession;
        _alarmListener = alarmListener;
        _preferencesStore = preferencesStore;
        _notifier = notifier;
        _timeProvider = timeProvider;
        _liveSession.EventRecorded += HandleEventRecorded;
        _alarmListener.AlarmTriggered += HandleAlarmTriggered;
    }

    private void HandleEventRecorded(object? sender, CompanionEvent item)
    {
        var preferences = _preferencesStore.Get();
        if (!preferences.IsEnabled(item.Kind) || IsQuietHoursNow(preferences))
        {
            return;
        }

        _notifier.Show(item.Title, item.Detail ?? "Rust+ companion event.", preferences.PlaySound);
    }

    private void HandleAlarmTriggered(object? sender, AlarmToastNotification notification)
    {
        // A server-attributed alarm already went through RecordExternalEvent, which raised
        // EventRecorded above — showing it again here would double-notify. Only the unattributed
        // fallback (no matching saved server) needs handling on this path.
        if (notification.ServerId is not null)
        {
            return;
        }

        var preferences = _preferencesStore.Get();
        if (!preferences.AlarmEvents || IsQuietHoursNow(preferences))
        {
            return;
        }

        _notifier.Show(notification.Title, notification.Message, preferences.PlaySound);
    }

    /// <summary>Quiet hours only gate the toast/sound shown here — the event itself is always
    /// recorded to history by its own source (RustPlusLiveSessionManager/RecordExternalEvent),
    /// regardless of this dispatcher's preferences.</summary>
    private bool IsQuietHoursNow(NotificationPreferences preferences) =>
        preferences.IsQuietHours(TimeOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime));

    public void Dispose()
    {
        _liveSession.EventRecorded -= HandleEventRecorded;
        _alarmListener.AlarmTriggered -= HandleAlarmTriggered;
    }
}
