using System.Text;
using RustPlusApi.Fcm;
using RustPlusApi.Fcm.Data.Events;
using RustPlusApi.Fcm.Registration;
using RustPlusApi.Fcm.Registration.Steps;
using RustPlusHelper.Application.Notifications;

namespace RustPlusHelper.Infrastructure.RustPlus;

/// <summary>
/// Runs one <see cref="RustPlusFcm"/> connection attempt for Smart Alarm "triggered" pushes.
/// <see cref="RustPlusFcm"/> has no built-in auto-reconnect (confirmed via its own source: "Instances
/// are single-connection: after Disconnect or disposal, create a new instance") and no age/order
/// information on its de-duplication ids, so both the reconnect loop and persisted-id pruning are the
/// caller's job — see <see cref="Application.Notifications.RustPlusAlarmNotificationListener"/>.
/// </summary>
public sealed class RustPlusApiAlarmListenerProvider : IRustPlusAlarmListenerProvider
{
    public async Task RunAsync(
        ReadOnlyMemory<byte> credentials,
        IReadOnlyCollection<string> seedPersistentIds,
        Action<AlarmTriggeredCapture> onAlarmTriggered,
        Action<string> onPersistentIdReceived,
        CancellationToken cancellationToken = default)
    {
        _ = await FcmSessionRunner.RunAsync<bool>(
            credentials,
            seedPersistentIds,
            (fcm, completion) =>
            {
                void OnAlarmTriggered(object? sender, AlarmNotification? notification)
                {
                    if (notification is null)
                    {
                        return;
                    }

                    onAlarmTriggered(new AlarmTriggeredCapture(
                        notification.ServerId,
                        notification.Title,
                        notification.Message,
                        notification.PersistentId));
                }

                void OnPersistentIdReceived(object? sender, string id) => onPersistentIdReceived(id);

                // Both a clean disconnect and the socket's own inactivity-timeout self-detection
                // surface here — either way, this attempt is over and the caller decides whether/when
                // to retry.
                void OnDisconnected(object? sender, EventArgs e) => completion.TrySetResult(true);
                void OnError(object? sender, Exception exception) => completion.TrySetException(exception);

                fcm.OnAlarmTriggered += OnAlarmTriggered;
                fcm.PersistentIdReceived += OnPersistentIdReceived;
                fcm.Disconnected += OnDisconnected;
                fcm.ErrorOccurred += OnError;
                return () =>
                {
                    fcm.OnAlarmTriggered -= OnAlarmTriggered;
                    fcm.PersistentIdReceived -= OnPersistentIdReceived;
                    fcm.Disconnected -= OnDisconnected;
                    fcm.ErrorOccurred -= OnError;
                };
            },
            cancellationToken).ConfigureAwait(false);
    }
}
