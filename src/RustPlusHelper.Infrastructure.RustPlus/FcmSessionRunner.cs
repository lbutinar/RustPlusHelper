using System.Text;
using RustPlusApi.Fcm;
using RustPlusApi.Fcm.Registration;
using RustPlusApi.Fcm.Registration.Steps;

namespace RustPlusHelper.Infrastructure.RustPlus;

/// <summary>
/// Runs one <see cref="RustPlusFcm"/> connect/listen/cleanup session: deserialize credentials, check in,
/// connect, wait for the caller's completion source, then always unsubscribe and disconnect. Used
/// identically by <see cref="RustPlusApiPairingProvider"/>'s two wait-for-pairing methods and
/// <see cref="RustPlusApiAlarmListenerProvider.RunAsync"/> — only which events those callers subscribe
/// to (and what they do with them) differs.
/// </summary>
internal static class FcmSessionRunner
{
    public static async Task<T> RunAsync<T>(
        ReadOnlyMemory<byte> credentials,
        IReadOnlyCollection<string>? seedPersistentIds,
        Func<RustPlusFcm, TaskCompletionSource<T>, Action> subscribe,
        CancellationToken cancellationToken)
    {
        var serialized = Encoding.UTF8.GetString(credentials.Span);
        var parsed = CredentialsStore.Deserialize(serialized);
        using var fcm = seedPersistentIds is null
            ? new RustPlusFcm(parsed)
            : new RustPlusFcm(parsed, new HashSet<string>(seedPersistentIds));
        var androidFcmRegister = new AndroidFcmRegister(null);
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));

        var unsubscribe = subscribe(fcm, completion);
        try
        {
            await androidFcmRegister.CheckInAsync(parsed.Gcm, cancellationToken).ConfigureAwait(false);
            await fcm.ConnectAsync(cancellationToken).ConfigureAwait(false);
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            unsubscribe();
            fcm.Disconnect();
        }
    }
}
