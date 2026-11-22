using LinkDeck.Core.Models;
using LinkDeck.Core.Popup;
using static LinkDeck.Core.Popup.GestureKey;
using static LinkDeck.Core.Popup.GestureSignal;

namespace LinkDeck.Core.Tests;

public class GestureDetectorTests
{
    private readonly GestureDetector _d = new(Hotkey.Default); // hold Alt, step Ctrl

    private GestureSignal Down(GestureKey k) => _d.KeyDown(k).Signal;

    private GestureSignal Up(GestureKey k) => _d.KeyUp(k).Signal;

    [Fact]
    public void Alt_then_ctrl_starts()
    {
        Assert.Equal(None, Down(LeftAlt));
        Assert.Equal(Started, Down(LeftCtrl));
        Assert.True(_d.IsActive);
    }

    [Fact]
    public void Ctrl_then_alt_starts()
    {
        Assert.Equal(None, Down(RightCtrl));
        Assert.Equal(Started, Down(LeftAlt));
    }

    [Fact]
    public void Holding_alt_and_tapping_ctrl_steps_and_releasing_alt_releases()
    {
        Down(LeftAlt);
        Down(LeftCtrl);
        Assert.Equal(None, Up(LeftCtrl));     // only Alt needs to stay down
        Assert.Equal(Step, Down(LeftCtrl));
        Assert.Equal(None, Up(LeftCtrl));
        Assert.Equal(Step, Down(RightCtrl));  // either Ctrl steps
        Assert.Equal(None, Up(RightCtrl));
        Assert.Equal(Released, Up(LeftAlt));
        Assert.False(_d.IsActive);
    }

    [Fact]
    public void Releasing_alt_while_ctrl_is_still_down_also_releases()
    {
        Down(LeftAlt);
        Down(LeftCtrl);

        Assert.Equal(Released, Up(LeftAlt));
    }

    [Fact]
    public void Shift_ctrl_steps_back_and_shift_is_swallowed()
    {
        Down(LeftAlt);
        Down(LeftCtrl);
        Up(LeftCtrl);

        var shift = _d.KeyDown(LeftShift);
        Assert.True(shift.Suppress); // no Alt+Shift input-language switch
        Assert.Equal(StepBack, Down(LeftCtrl));
        Assert.True(_d.KeyUp(LeftShift).Suppress);
    }

    [Fact]
    public void Space_and_escape_are_signalled_and_suppressed_including_their_release()
    {
        Down(LeftAlt);
        Down(LeftCtrl);

        var space = _d.KeyDown(GestureKey.Space);
        Assert.Equal(new GestureResult(true, GestureSignal.Space), space);
        Assert.Equal(new GestureResult(true, None), _d.KeyDown(GestureKey.Space)); // auto-repeat
        Assert.True(_d.KeyUp(GestureKey.Space).Suppress);
        Assert.Equal(new GestureResult(true, GestureSignal.Escape), _d.KeyDown(GestureKey.Escape));
    }

    [Fact]
    public void Space_without_the_gesture_passes_through()
    {
        Assert.Equal(GestureResult.None, _d.KeyDown(GestureKey.Space));
        Down(LeftAlt);
        Assert.Equal(GestureResult.None, _d.KeyDown(GestureKey.Space)); // Alt+Space (window menu) untouched
    }

    [Fact]
    public void Another_key_cancels_and_is_not_suppressed()
    {
        Down(LeftCtrl);
        Down(LeftAlt);

        var t = _d.KeyDown(Other); // e.g. someone's Ctrl+Alt+T

        Assert.Equal(new GestureResult(false, Cancelled), t);
        Assert.False(_d.IsActive);
        Assert.Equal(None, Up(LeftAlt)); // no launch afterwards
    }

    [Fact]
    public void AltGr_never_starts_the_gesture()
    {
        // AltGr arrives as a fake LeftCtrl plus RightAlt.
        Assert.Equal(None, Down(LeftCtrl));
        Assert.Equal(None, Down(RightAlt));
        Assert.Equal(None, Down(Other)); // e.g. AltGr+V = @
        Assert.False(_d.IsActive);
    }

    [Fact]
    public void Auto_repeat_of_ctrl_does_not_step()
    {
        Down(LeftAlt);
        Down(LeftCtrl);

        Assert.Equal(None, Down(LeftCtrl));
        Assert.Equal(None, Down(LeftAlt));
    }

    [Fact]
    public void Gesture_can_start_again_after_release()
    {
        Down(LeftAlt);
        Down(LeftCtrl);
        Up(LeftCtrl);
        Up(LeftAlt);

        Down(LeftAlt);
        Assert.Equal(Started, Down(LeftCtrl));
    }

    [Fact]
    public void Lost_hold_key_cancels_and_forgets_stale_keys()
    {
        Down(LeftCtrl);
        Down(LeftAlt); // then Ctrl+Alt+Del: the releases are never seen

        Assert.Equal(Cancelled, _d.HoldKeyLost());
        Assert.False(_d.IsActive);
        Assert.Equal(None, Down(LeftAlt)); // stale Ctrl was forgotten: no accidental restart
    }

    [Fact]
    public void Custom_hotkey_ctrl_hold_win_step()
    {
        _d.SetHotkey(new Hotkey { Hold = "Ctrl", Step = "Win" });

        Down(RightCtrl);
        Assert.Equal(Started, Down(LeftWin));
        Assert.Equal(Released, Up(RightCtrl));
    }

    [Fact]
    public void Invalid_hotkey_falls_back_to_default()
    {
        _d.SetHotkey(new Hotkey { Hold = "Alt", Step = "Alt" });

        Assert.Equal(Hotkey.Default, _d.Hotkey);
    }
}
