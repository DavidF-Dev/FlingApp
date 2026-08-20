namespace Fling.Gui;

/// <summary>
/// Ensures one running tray app per user session, and lets a second launch ask the
/// running one to surface itself instead of starting a rival instance.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\Fling.Tray.Instance";
    private const string ActivateEventName = @"Local\Fling.Tray.Activate";

    private readonly Mutex _mutex = new(initiallyOwned: false, MutexName);
    private readonly EventWaitHandle _activate =
        new(initialState: false, EventResetMode.AutoReset, ActivateEventName);

    private RegisteredWaitHandle? _registration;
    private bool _owned;

    public bool TryAcquire()
    {
        try
        {
            _owned = _mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            // The previous owner died without releasing; ownership transfers to us.
            _owned = true;
        }

        return _owned;
    }

    /// <summary>
    /// Invokes <paramref name="onActivate"/> whenever another launch signals this
    /// instance. The callback arrives on a thread pool thread.
    /// </summary>
    public void ListenForActivation(Action onActivate)
    {
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _activate,
            (_, _) => onActivate(),
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void SignalExisting() => _activate.Set();

    public void Dispose()
    {
        _registration?.Unregister(null);

        if (_owned)
            _mutex.ReleaseMutex();

        _mutex.Dispose();
        _activate.Dispose();
    }
}
