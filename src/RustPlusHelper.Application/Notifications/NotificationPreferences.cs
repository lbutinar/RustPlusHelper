using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Application.Security;

namespace RustPlusHelper.Application.Notifications;

/// <summary>Per-category desktop notification toggles, all enabled by default.</summary>
public sealed record NotificationPreferences(
    bool ConnectionEvents = true,
    bool TeamEvents = true,
    bool MarkerEvents = true,
    bool VendingEvents = true,
    bool AlarmEvents = true,
    /// <summary>Global, not per-category: whether a shown notification also plays a sound.</summary>
    bool PlaySound = true,
    /// <summary>Global: while enabled and the local time-of-day falls in
    /// [<see cref="QuietHoursStart"/>, <see cref="QuietHoursEnd"/>), no toast/sound is shown for any
    /// category — the event is still recorded to history exactly as it always is; this only gates the
    /// desktop notification.</summary>
    bool QuietHoursEnabled = false,
    TimeOnly QuietHoursStart = default,
    TimeOnly QuietHoursEnd = default)
{
    public static NotificationPreferences Default { get; } = new();

    /// <summary>Whether the category covering <paramref name="kind"/> is enabled. Unmapped kinds
    /// default to enabled rather than silently never notifying for a future event kind.</summary>
    public bool IsEnabled(CompanionEventKind kind) => kind switch
    {
        CompanionEventKind.ConnectionEstablished
            or CompanionEventKind.ConnectionLost
            or CompanionEventKind.ConnectionRestored => ConnectionEvents,
        CompanionEventKind.TeamMemberConnected
            or CompanionEventKind.TeamMemberDisconnected
            or CompanionEventKind.TeamMemberDied
            or CompanionEventKind.TeamMemberRespawned
            or CompanionEventKind.TeamMemberChangedGrid => TeamEvents,
        CompanionEventKind.MarkerAppeared
            or CompanionEventKind.MarkerDisappeared
            or CompanionEventKind.OilRigActivated => MarkerEvents,
        CompanionEventKind.VendingPriceChanged
            or CompanionEventKind.VendingStockChanged
            or CompanionEventKind.VendingOfferAdded
            or CompanionEventKind.VendingOfferRemoved => VendingEvents,
        CompanionEventKind.AlarmTriggered => AlarmEvents,
        _ => true
    };

    /// <summary>Whether <paramref name="localTimeOfDay"/> falls inside the configured quiet-hours
    /// window. A start after end is treated as a window that wraps past midnight (e.g. 22:00-07:00)
    /// rather than an empty/invalid range.</summary>
    public bool IsQuietHours(TimeOnly localTimeOfDay)
    {
        if (!QuietHoursEnabled)
        {
            return false;
        }

        return QuietHoursStart <= QuietHoursEnd
            ? localTimeOfDay >= QuietHoursStart && localTimeOfDay < QuietHoursEnd
            : localTimeOfDay >= QuietHoursStart || localTimeOfDay < QuietHoursEnd;
    }
}

/// <summary>Persists <see cref="NotificationPreferences"/> as a single packed byte via the existing
/// <see cref="IApplicationSecretStore"/> — pragmatic reuse of its single-row key/value shape rather
/// than a new migration for one small settings blob (the DPAPI protection is unnecessary for
/// non-secret data here, but harmless).</summary>
public sealed class NotificationPreferencesStore(IApplicationSecretStore secrets)
{
    private const byte ConnectionFlag = 0x01;
    private const byte TeamFlag = 0x02;
    private const byte MarkerFlag = 0x04;
    private const byte VendingFlag = 0x08;
    private const byte AlarmFlag = 0x10;

    /// <summary>Stores "muted", not "play sound": the default is sound ON, and a byte saved before
    /// this flag existed has this bit unset — treating unset as "not muted" keeps that default for
    /// users who saved preferences before this feature shipped, instead of silently muting them.</summary>
    private const byte MuteSoundFlag = 0x20;

    private const byte QuietHoursFlag = 0x40;

    /// <summary>Bytes 1-4 (start/end minutes-of-day as little-endian ushorts) are absent from a value
    /// saved before quiet hours existed, or from any legacy single-byte value — both are read back as
    /// disabled quiet hours starting at midnight, never a crash or a nonsensical window.</summary>
    private const int QuietHoursByteLength = 5;

    public NotificationPreferences Get()
    {
        var stored = secrets.Retrieve(ApplicationSecretKind.NotificationPreferences);
        if (stored is not { Length: > 0 })
        {
            return NotificationPreferences.Default;
        }

        var flags = stored[0];
        var hasQuietHoursWindow = stored.Length >= QuietHoursByteLength;
        return new NotificationPreferences(
            (flags & ConnectionFlag) != 0,
            (flags & TeamFlag) != 0,
            (flags & MarkerFlag) != 0,
            (flags & VendingFlag) != 0,
            (flags & AlarmFlag) != 0,
            (flags & MuteSoundFlag) == 0,
            (flags & QuietHoursFlag) != 0,
            hasQuietHoursWindow ? MinutesToTimeOfDay(BitConverter.ToUInt16(stored, 1)) : default,
            hasQuietHoursWindow ? MinutesToTimeOfDay(BitConverter.ToUInt16(stored, 3)) : default);
    }

    public void Save(NotificationPreferences preferences)
    {
        byte flags = 0;
        if (preferences.ConnectionEvents)
        {
            flags |= ConnectionFlag;
        }

        if (preferences.TeamEvents)
        {
            flags |= TeamFlag;
        }

        if (preferences.MarkerEvents)
        {
            flags |= MarkerFlag;
        }

        if (preferences.VendingEvents)
        {
            flags |= VendingFlag;
        }

        if (preferences.AlarmEvents)
        {
            flags |= AlarmFlag;
        }

        if (!preferences.PlaySound)
        {
            flags |= MuteSoundFlag;
        }

        if (preferences.QuietHoursEnabled)
        {
            flags |= QuietHoursFlag;
        }

        var bytes = new byte[QuietHoursByteLength];
        bytes[0] = flags;
        BitConverter.GetBytes(TimeOfDayToMinutes(preferences.QuietHoursStart)).CopyTo(bytes, 1);
        BitConverter.GetBytes(TimeOfDayToMinutes(preferences.QuietHoursEnd)).CopyTo(bytes, 3);
        secrets.Store(ApplicationSecretKind.NotificationPreferences, bytes);
    }

    private static TimeOnly MinutesToTimeOfDay(ushort minutesSinceMidnight) =>
        new(minutesSinceMidnight / 60, minutesSinceMidnight % 60);

    private static ushort TimeOfDayToMinutes(TimeOnly timeOfDay) =>
        (ushort)((timeOfDay.Hour * 60) + timeOfDay.Minute);
}
