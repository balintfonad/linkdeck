namespace LinkDeck.Desktop.Services;

/// <summary>
/// Single-instance guard: a named mutex plus a named event. A second instance signals the first
/// (which then shows its popup) and exits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly bool _owns;
    private RegisteredWaitHandle? _registration;

    public SingleInstance(string name)
    {
        var scope = $"{name}.{Environment.UserName}";
        _mutex = new Mutex(initiallyOwned: true, $@"Local\{scope}.Mutex", out _owns);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{scope}.Show");
    }

    public bool IsFirstInstance => _owns;

    /// <summary>Asks the running instance to show its popup.</summary>
    public void SignalFirstInstance() => _showEvent.Set();

    /// <summary>Invokes <paramref name="onShowRequested"/> (on a thread-pool thread) whenever another instance signals.</summary>
    public void Listen(Action onShowRequested)
    {
        _registration = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => onShowRequested(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _showEvent.Dispose();
        if (_owns)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}
