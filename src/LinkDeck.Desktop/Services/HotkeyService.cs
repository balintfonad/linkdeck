using LinkDeck.Core.Abstractions;
using LinkDeck.Core.Logging;
using LinkDeck.Core.Models;
using LinkDeck.Core.Popup;
using SharpHook;
using SharpHook.Data;

namespace LinkDeck.Desktop.Services;

/// <summary>
/// Global hold-and-step gesture via SharpHook. Key events are mapped to <see cref="GestureKey"/> and fed to the
/// pure <see cref="GestureDetector"/> synchronously on the hook thread (so suppression works); signals are
/// raised through <c>post</c> (the UI dispatcher). No keystroke is ever logged or stored.
/// </summary>
public sealed class HotkeyService : IHotkeyService
{
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromMilliseconds(200);

    private readonly ILog _log;
    private readonly Action<Action> _post;
    private readonly IKeyStateProbe? _probe;
    private readonly object _gate = new();
    private readonly GestureDetector _detector = new(Hotkey.Default);
    private readonly Timer _probeTimer;
    private SimpleGlobalHook? _hook;
    private int _holdMissingCount;
    private bool _paused;

    public HotkeyService(ILog log, Action<Action> post, IKeyStateProbe? probe = null)
    {
        _log = log;
        _post = post;
        _probe = probe;
        _probeTimer = new Timer(_ => ProbeHoldKey(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public event EventHandler? GestureStarted;

    public event EventHandler<bool>? Stepped;

    public event EventHandler? SpacePressed;

    public event EventHandler? EscapePressed;

    public event EventHandler<bool>? TabPressed;

    public event EventHandler? HoldReleased;

    public event EventHandler? Cancelled;

    public bool IsPaused
    {
        get => _paused;
        set
        {
            lock (_gate)
            {
                _paused = value;
                _detector.Reset();
            }
        }
    }

    public void Start(Hotkey hotkey)
    {
        UpdateHotkey(hotkey);
        if (_hook is not null)
        {
            return;
        }

        _hook = new SimpleGlobalHook();
        _hook.KeyPressed += OnKeyPressed;
        _hook.KeyReleased += OnKeyReleased;
        _ = RunHookAsync(_hook);
    }

    public void UpdateHotkey(Hotkey hotkey)
    {
        lock (_gate)
        {
            _detector.SetHotkey(hotkey);
        }

        _log.Info($"Gesture set to hold {_detector.Hotkey.Hold}, step {_detector.Hotkey.Step}.");
    }

    public void Dispose()
    {
        _probeTimer.Dispose();
        if (_hook is null)
        {
            return;
        }

        _hook.KeyPressed -= OnKeyPressed;
        _hook.KeyReleased -= OnKeyReleased;
        _hook.Dispose();
        _hook = null;
    }

    private async Task RunHookAsync(SimpleGlobalHook hook)
    {
        try
        {
            await hook.RunAsync(GlobalHookType.Keyboard).ConfigureAwait(false);
        }
        catch (HookException ex)
        {
            _log.Error("The global keyboard hook could not be started.", ex);
        }
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e) => Handle(e, down: true);

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e) => Handle(e, down: false);

    private void Handle(KeyboardHookEventArgs e, bool down)
    {
        GestureResult result;
        bool active;
        lock (_gate)
        {
            if (_paused)
            {
                return;
            }

            var key = Map(e.Data.KeyCode);
            result = down ? _detector.KeyDown(key) : _detector.KeyUp(key);
            active = _detector.IsActive;
        }

        if (result.Suppress)
        {
            e.SuppressEvent = true;
        }

        if (result.Signal == GestureSignal.Started)
        {
            _holdMissingCount = 0;
            _probeTimer.Change(ProbeInterval, ProbeInterval);
        }
        else if (!active)
        {
            _probeTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        Raise(result.Signal);
    }

    /// <summary>
    /// While the gesture is active, checks the physical hold-key state. Two consecutive "up" readings mean the
    /// release event was lost (e.g. Ctrl+Alt+Del): cancel instead of launching. Two readings avoid racing a
    /// release event that is about to be delivered.
    /// </summary>
    private void ProbeHoldKey()
    {
        if (_probe is null)
        {
            return;
        }

        var signal = GestureSignal.None;
        lock (_gate)
        {
            if (!_detector.IsActive)
            {
                _probeTimer.Change(Timeout.Infinite, Timeout.Infinite);
                return;
            }

            var held = GestureDetector.KeysFor(_detector.Hotkey.Hold).Any(_probe.IsDown);
            _holdMissingCount = held ? 0 : _holdMissingCount + 1;
            if (_holdMissingCount >= 2)
            {
                signal = _detector.HoldKeyLost();
                _probeTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        if (signal != GestureSignal.None)
        {
            _log.Info("Gesture cancelled: the hold key is no longer down.");
            Raise(signal);
        }
    }

    private void Raise(GestureSignal signal)
    {
        Action? raise = signal switch
        {
            GestureSignal.Started => () => GestureStarted?.Invoke(this, EventArgs.Empty),
            GestureSignal.Step => () => Stepped?.Invoke(this, false),
            GestureSignal.StepBack => () => Stepped?.Invoke(this, true),
            GestureSignal.Space => () => SpacePressed?.Invoke(this, EventArgs.Empty),
            GestureSignal.Escape => () => EscapePressed?.Invoke(this, EventArgs.Empty),
            GestureSignal.Tab => () => TabPressed?.Invoke(this, false),
            GestureSignal.TabBack => () => TabPressed?.Invoke(this, true),
            GestureSignal.Released => () => HoldReleased?.Invoke(this, EventArgs.Empty),
            GestureSignal.Cancelled => () => Cancelled?.Invoke(this, EventArgs.Empty),
            _ => null,
        };
        if (raise is not null)
        {
            _post(raise);
        }
    }

    internal static GestureKey Map(KeyCode code) => code switch
    {
        KeyCode.VcLeftControl => GestureKey.LeftCtrl,
        KeyCode.VcRightControl => GestureKey.RightCtrl,
        KeyCode.VcLeftAlt => GestureKey.LeftAlt,
        KeyCode.VcRightAlt => GestureKey.RightAlt,
        KeyCode.VcLeftShift => GestureKey.LeftShift,
        KeyCode.VcRightShift => GestureKey.RightShift,
        KeyCode.VcLeftMeta => GestureKey.LeftWin,
        KeyCode.VcRightMeta => GestureKey.RightWin,
        KeyCode.VcSpace => GestureKey.Space,
        KeyCode.VcEscape => GestureKey.Escape,
        KeyCode.VcTab => GestureKey.Tab,
        _ => GestureKey.Other,
    };
}
