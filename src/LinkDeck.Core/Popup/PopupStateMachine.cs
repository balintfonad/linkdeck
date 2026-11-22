namespace LinkDeck.Core.Popup;

public enum PopupMode
{
    Hidden,
    Cycling,
    Interactive,
}

/// <summary>Side effects the UI must perform after an input.</summary>
public abstract record PopupEffect
{
    private PopupEffect()
    {
    }

    /// <summary>Show the popup (reset to the "All" list, empty search) in the given mode.</summary>
    public sealed record Show(PopupMode Mode) : PopupEffect;

    /// <summary>The popup stays visible but switches from Cycling to Interactive (take keyboard focus).</summary>
    public sealed record EnterInteractive : PopupEffect;

    /// <summary>Select the item at this index of the visible list (-1 = nothing).</summary>
    public sealed record Select(int Index) : PopupEffect;

    /// <summary>Launch the item at this index of the visible list.</summary>
    public sealed record Launch(int Index) : PopupEffect;

    /// <summary>Launch whatever is currently selected in the UI (Interactive mode, where the UI owns the selection).</summary>
    public sealed record LaunchSelection : PopupEffect;

    /// <summary>Hide the popup; the UI clears the search text and resets the selection.</summary>
    public sealed record Hide : PopupEffect;
}

/// <summary>
/// Pure logic for the Alt+Tab-style gesture (default: hold Alt, step with Ctrl). No UI, no timers.
/// <list type="bullet">
/// <item>Hidden + gesture started (hold+step) → Cycling, item 1 selected.</item>
/// <item>Cycling + step → next item (back: previous), wraps.</item>
/// <item>Cycling + hold released → after at least one step: launch the selection and hide; without stepping: stay open (interactive).</item>
/// <item>Cycling + Space → Interactive: the popup stays open for searching.</item>
/// <item>Cycling + cancelled (another key) or Esc → Hidden, nothing launched.</item>
/// <item>Interactive + Esc or focus lost (unless keep-open) → Hidden.</item>
/// <item>Interactive + gesture started again → stays open and is brought to the front; steps then move the selection
/// and, with <see cref="LaunchOnRelease"/>, releasing the hold key after a step launches it.</item>
/// </list>
/// </summary>
public sealed class PopupStateMachine
{
    private int _itemCount;
    private bool _stepped;

    /// <summary>The gesture was started again while Interactive and the hold key is still down.</summary>
    private bool _holdingInInteractive;

    public PopupMode Mode { get; private set; } = PopupMode.Hidden;

    public int SelectedIndex { get; private set; } = -1;

    /// <summary>
    /// Keep-open mode: releasing the hold key launches the selection but the popup stays open (interactive),
    /// and losing focus does not close it. Only Esc closes it.
    /// </summary>
    public bool KeepOpen { get; set; }

    /// <summary>
    /// Releasing the hold key after at least one step launches the selection. When false the launcher just stays
    /// open with the selection (Enter launches it).
    /// </summary>
    public bool LaunchOnRelease { get; set; } = true;

    /// <param name="itemCount">Number of items in the "All" list (what cycling iterates).</param>
    public IReadOnlyList<PopupEffect> GestureStarted(int itemCount)
    {
        switch (Mode)
        {
            case PopupMode.Hidden:
                _itemCount = Math.Max(0, itemCount);
                Mode = PopupMode.Cycling;
                _stepped = false;
                SelectedIndex = _itemCount > 0 ? 0 : -1;
                return [new PopupEffect.Show(PopupMode.Cycling), new PopupEffect.Select(SelectedIndex)];
            case PopupMode.Interactive:
                // Already open: the gesture never closes it (Esc or clicking elsewhere does); just bring it to the front.
                // This press does not step; the following step taps do.
                _holdingInInteractive = true;
                _stepped = false;
                return [new PopupEffect.EnterInteractive()];
            default:
                return [];
        }
    }

    /// <summary>
    /// Step key tapped while holding. While cycling this moves the selection; in Interactive mode the view model moves
    /// it and this only records that a step happened (for <see cref="LaunchOnRelease"/>).
    /// </summary>
    public IReadOnlyList<PopupEffect> Step(bool back)
    {
        if (Mode == PopupMode.Interactive)
        {
            _stepped |= _holdingInInteractive;
            return [];
        }

        if (Mode != PopupMode.Cycling || _itemCount == 0)
        {
            return [];
        }

        _stepped = true;
        SelectedIndex = back
            ? (SelectedIndex - 1 + _itemCount) % _itemCount
            : (SelectedIndex + 1) % _itemCount;
        return [new PopupEffect.Select(SelectedIndex)];
    }

    /// <summary>Space while holding: stop cycling and keep the popup open.</summary>
    public IReadOnlyList<PopupEffect> Space()
    {
        if (Mode != PopupMode.Cycling)
        {
            // Space (or a group switch) while holding over the open launcher: keep it open, launch nothing on release.
            _holdingInInteractive = false;
            return [];
        }

        Mode = PopupMode.Interactive;
        return [new PopupEffect.EnterInteractive()];
    }

    /// <summary>The hold key was released.</summary>
    public IReadOnlyList<PopupEffect> HoldReleased()
    {
        if (Mode == PopupMode.Interactive)
        {
            var launch = _holdingInInteractive && _stepped && LaunchOnRelease;
            _holdingInInteractive = false;
            _stepped = false;
            if (!launch)
            {
                return [];
            }

            return KeepOpen ? [new PopupEffect.LaunchSelection()] : [new PopupEffect.LaunchSelection(), .. HideCore()];
        }

        if (Mode != PopupMode.Cycling)
        {
            return [];
        }

        var index = SelectedIndex;

        // Opening and releasing without stepping only opens the launcher: nothing is launched.
        // With launch-on-release off, releasing never launches: the launcher stays open on the selection.
        if (!_stepped || index < 0 || !LaunchOnRelease)
        {
            Mode = PopupMode.Interactive;
            return [new PopupEffect.EnterInteractive()];
        }

        if (KeepOpen)
        {
            Mode = PopupMode.Interactive;
            return [new PopupEffect.EnterInteractive(), new PopupEffect.Launch(index)];
        }

        var hide = HideCore();
        return [new PopupEffect.Launch(index), .. hide];
    }

    /// <summary>The gesture was abandoned (another key pressed, or the hold key was lost). Only affects cycling.</summary>
    public IReadOnlyList<PopupEffect> Cancel()
    {
        _holdingInInteractive = false;
        return Mode == PopupMode.Cycling ? HideCore() : [];
    }

    public IReadOnlyList<PopupEffect> Escape() => Mode == PopupMode.Hidden ? [] : HideCore();

    /// <summary>The popup lost focus. Ignored while cycling (the popup does not take focus then).</summary>
    public IReadOnlyList<PopupEffect> FocusLost() => Mode == PopupMode.Interactive && !KeepOpen ? HideCore() : [];

    /// <summary>Open directly in Interactive mode (tray click, second instance).</summary>
    public IReadOnlyList<PopupEffect> ShowInteractive(int itemCount)
    {
        if (Mode == PopupMode.Interactive)
        {
            return [];
        }

        var wasHidden = Mode == PopupMode.Hidden;
        _itemCount = Math.Max(0, itemCount);
        Mode = PopupMode.Interactive;
        if (!wasHidden)
        {
            return [new PopupEffect.EnterInteractive()];
        }

        SelectedIndex = _itemCount > 0 ? 0 : -1;
        return [new PopupEffect.Show(PopupMode.Interactive), new PopupEffect.Select(SelectedIndex)];
    }

    /// <summary>The UI hid the popup itself (e.g. after launching from Interactive mode).</summary>
    public IReadOnlyList<PopupEffect> Hidden() => Mode == PopupMode.Hidden ? [] : HideCore();

    private PopupEffect[] HideCore()
    {
        Mode = PopupMode.Hidden;
        SelectedIndex = -1;
        _itemCount = 0;
        _holdingInInteractive = false;
        return [new PopupEffect.Hide()];
    }
}
