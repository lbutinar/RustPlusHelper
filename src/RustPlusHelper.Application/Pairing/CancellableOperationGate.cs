namespace RustPlusHelper.Application.Pairing;

/// <summary>
/// Serializes a single cancellable long-running operation. Used identically by
/// <see cref="RustPlusPairingManager"/> and <see cref="RustPlusEntityPairingManager"/> for their
/// register/listen flows: rejects starting a second operation while one is already running, and lets
/// <see cref="Cancel"/>/<see cref="Dispose"/> safely tear down whichever operation (if any) is
/// currently active.
/// </summary>
internal sealed class CancellableOperationGate : IDisposable
{
    private readonly Lock _lock = new();
    private CancellationTokenSource? _current;

    /// <summary>
    /// Starts a new operation, throwing <paramref name="alreadyInProgressMessage"/> if one is already
    /// running. <paramref name="onBegin"/> runs under the same lock as the CancellationTokenSource's
    /// creation so a concurrent <see cref="Cancel"/>/<see cref="Dispose"/> can't observe a half-started
    /// operation.
    /// </summary>
    public CancellationTokenSource Begin(string alreadyInProgressMessage, Action onBegin)
    {
        lock (_lock)
        {
            if (_current is not null)
            {
                throw new InvalidOperationException(alreadyInProgressMessage);
            }

            _current = new CancellationTokenSource();
            onBegin();
            return _current;
        }
    }

    public void End(CancellationTokenSource operation)
    {
        lock (_lock)
        {
            if (ReferenceEquals(_current, operation))
            {
                _current = null;
            }
        }
    }

    public void Cancel()
    {
        lock (_lock)
        {
            _current?.Cancel();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _current?.Cancel();
            _current?.Dispose();
            _current = null;
        }
    }
}
