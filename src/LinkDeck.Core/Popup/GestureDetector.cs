using LinkDeck.Core.Models;

namespace LinkDeck.Core.Popup;

/// <summary>Physical keys the gesture cares about. Everything else is <see cref="Other"/>.</summary>
public enum GestureKey
{
    Other,
    LeftCtrl,
    RightCtrl,
    LeftAlt,

    /// <summary>Right Alt, which is AltGr on many layouts (it also sends a fake LeftCtrl). Never part of the gesture.</summary>
    RightAlt,
    LeftShift,
    RightShift,
    LeftWin,
    RightWin,
    Space,
    Escape,
    Tab,
}

public enum GestureSignal
{
    None,

    /// <summary>Hold and step pressed together: open the popup.</summary>
    Started,

    /// <summary>Step tapped again while holding: next item.</summary>
    Step,

    /// <summary>Shift+step while holding: previous item.</summary>
    StepBack,

    /// <summary>Space while holding: keep the popup open (interactive).</summary>
    Space,

    /// <summary>Esc while holding.</summary>
    Escape,

    /// <summary>Tab while holding (swallowed so Windows' Alt+Tab does not open): next group.</summary>
    Tab,

    /// <summary>Shift+Tab while holding: previous group.</summary>
    TabBack,

    /// <summary>Hold key released: open the selection.</summary>
    Released,

    /// <summary>Another key was pressed (someone else's shortcut) or the hold key is no longer down: abort silently.</summary>
    Cancelled,
}

/// <param name="Suppress">True when the key event must not reach the foreground application.</param>
public readonly record struct GestureResult(bool Suppress, GestureSignal Signal)
{
    public static GestureResult None => default;
}

/// <summary>
/// Pure key-event logic for the hold-and-step gesture. Fed with key down/up events from a global hook;
/// tells the hook what to suppress and which signal to raise. Not thread-safe: call from the hook thread only.
/// </summary>
public sealed class GestureDetector
{
    private readonly HashSet<GestureKey> _down = [];
    private readonly HashSet<GestureKey> _suppressed = [];
    private GestureKey[] _holdKeys = [];
    private GestureKey[] _stepKeys = [];

    public GestureDetector(Hotkey hotkey) => SetHotkey(hotkey);

    public Hotkey Hotkey { get; private set; } = Hotkey.Default;

    /// <summary>True between <see cref="GestureSignal.Started"/> and release/cancel.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Keys that count for a modifier name. "Alt" is left Alt only so AltGr never triggers.</summary>
    public static GestureKey[] KeysFor(string modifier) => modifier.ToUpperInvariant() switch
    {
        "CTRL" => [GestureKey.LeftCtrl, GestureKey.RightCtrl],
        "ALT" => [GestureKey.LeftAlt],
        "WIN" => [GestureKey.LeftWin, GestureKey.RightWin],
        _ => [],
    };

    public void SetHotkey(Hotkey hotkey)
    {
        if (!hotkey.IsValid())
        {
            hotkey = Hotkey.Default;
        }

        Hotkey = hotkey;
        _holdKeys = KeysFor(hotkey.Hold);
        _stepKeys = KeysFor(hotkey.Step);
        Reset();
    }

    public void Reset()
    {
        IsActive = false;
        _down.Clear();
        _suppressed.Clear();
    }

    public bool HoldHeld => _holdKeys.Any(_down.Contains);

    private bool StepHeld => _stepKeys.Any(_down.Contains);

    private bool ShiftHeld => _down.Contains(GestureKey.LeftShift) || _down.Contains(GestureKey.RightShift);

    public bool IsHoldKey(GestureKey key) => _holdKeys.Contains(key);

    public GestureResult KeyDown(GestureKey key)
    {
        var repeat = !_down.Add(key);

        if (IsHoldKey(key) || _stepKeys.Contains(key))
        {
            if (repeat)
            {
                return GestureResult.None;
            }

            if (!IsActive)
            {
                if (HoldHeld && StepHeld)
                {
                    IsActive = true;
                    return new GestureResult(false, GestureSignal.Started);
                }

                return GestureResult.None;
            }

            return _stepKeys.Contains(key)
                ? new GestureResult(false, ShiftHeld ? GestureSignal.StepBack : GestureSignal.Step)
                : GestureResult.None;
        }

        if (!IsActive)
        {
            return GestureResult.None;
        }

        switch (key)
        {
            case GestureKey.Space:
                _suppressed.Add(key);
                return new GestureResult(true, repeat ? GestureSignal.None : GestureSignal.Space);
            case GestureKey.Escape:
                _suppressed.Add(key);
                return new GestureResult(true, repeat ? GestureSignal.None : GestureSignal.Escape);
            case GestureKey.Tab:
                _suppressed.Add(key);
                return new GestureResult(true, repeat ? GestureSignal.None : ShiftHeld ? GestureSignal.TabBack : GestureSignal.Tab);
            case GestureKey.LeftShift or GestureKey.RightShift:
                // Swallow Shift so Windows' Alt+Shift input-language switch does not fire mid-gesture.
                _suppressed.Add(key);
                return new GestureResult(true, GestureSignal.None);
            case GestureKey.Other:
                // Somebody else's Ctrl+Alt+X shortcut: step aside and let the key through.
                IsActive = false;
                return new GestureResult(false, GestureSignal.Cancelled);
            default:
                return GestureResult.None; // other modifiers (Win, RightAlt, the other Ctrl)
        }
    }

    public GestureResult KeyUp(GestureKey key)
    {
        _down.Remove(key);
        var suppress = _suppressed.Remove(key);
        if (IsActive && IsHoldKey(key) && !HoldHeld)
        {
            IsActive = false;
            return new GestureResult(suppress, GestureSignal.Released);
        }

        return new GestureResult(suppress, GestureSignal.None);
    }

    /// <summary>
    /// Call when the hold key is physically up although no release event arrived
    /// (e.g. it was released on the Ctrl+Alt+Del secure desktop). Cancels instead of launching.
    /// </summary>
    public GestureSignal HoldKeyLost()
    {
        // Release events were missed, so every remembered key state is suspect.
        _down.Clear();
        if (!IsActive)
        {
            return GestureSignal.None;
        }

        IsActive = false;
        return GestureSignal.Cancelled;
    }
}
