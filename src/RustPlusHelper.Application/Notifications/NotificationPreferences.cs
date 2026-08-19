using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Application.Security;

namespace RustPlusHelper.Application.Notifications;

/// <summary>Per-category desktop notification toggles, all enabled by default.</summary>
public sealed record NotificationPreferences(
    bool ConnectionEvents = true,
    bool TeamEvents = true,
    bool MarkerEvents = true,
    bool VendingEvents = true,
    bool AlarmEvents = true)
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
        CompanionEventKind.MarkerAppeared or CompanionEventKind.MarkerDisappeared => MarkerEvents,
        CompanionEventKind.VendingPriceChanged
            or CompanionEventKind.VendingStockChanged
            or CompanionEventKind.VendingOfferAdded
            or CompanionEventKind.VendingOfferRemoved => VendingEvents,
        CompanionEventKind.AlarmTriggered => AlarmEvents,
        _ => true
    };
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

    public NotificationPreferences Get()
    {
        var stored = secrets.Retrieve(ApplicationSecretKind.NotificationPreferences);
        if (stored is not { Length: > 0 })
        {
            return NotificationPreferences.Default;
        }

        var flags = stored[0];
        return new NotificationPreferences(
            (flags & ConnectionFlag) != 0,
            (flags & TeamFlag) != 0,
            (flags & MarkerFlag) != 0,
            (flags & VendingFlag) != 0,
            (flags & AlarmFlag) != 0);
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

        secrets.Store(ApplicationSecretKind.NotificationPreferences, [flags]);
    }
}
