using LinkDeck.Core.Popup;
using static LinkDeck.Core.Popup.PopupEffect;

namespace LinkDeck.Core.Tests;

public class PopupStateMachineTests
{
    private readonly PopupStateMachine _m = new();

    [Fact]
    public void Starts_hidden()
    {
        Assert.Equal(PopupMode.Hidden, _m.Mode);
        Assert.Equal(-1, _m.SelectedIndex);
    }

    [Fact]
    public void Gesture_start_shows_cycling_with_first_item_selected()
    {
        Assert.Equal([new Show(PopupMode.Cycling), new Select(0)], _m.GestureStarted(5));
        Assert.Equal(PopupMode.Cycling, _m.Mode);
    }

    [Fact]
    public void Release_without_stepping_only_opens_the_launcher()
    {
        _m.GestureStarted(5);

        Assert.Equal([new EnterInteractive()], _m.HoldReleased());
        Assert.Equal(PopupMode.Interactive, _m.Mode);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(4, 4)]
    [InlineData(5, 0)] // wraps
    public void Each_step_moves_to_the_next_item_and_release_launches_it(int steps, int expected)
    {
        _m.GestureStarted(5);
        for (var i = 0; i < steps; i++)
        {
            _m.Step(back: false);
        }

        Assert.Equal([new Launch(expected), new Hide()], _m.HoldReleased());
    }

    [Fact]
    public void Step_back_wraps_to_the_last_item()
    {
        _m.GestureStarted(3);

        Assert.Equal([new Select(2)], _m.Step(back: true));
    }

    [Fact]
    public void Space_switches_to_interactive_and_release_then_keeps_it_open()
    {
        _m.GestureStarted(5);
        _m.Step(false);

        Assert.Equal([new EnterInteractive()], _m.Space());
        Assert.Empty(_m.HoldReleased());
        Assert.Equal(PopupMode.Interactive, _m.Mode);
        Assert.Equal(1, _m.SelectedIndex);
    }

    [Fact]
    public void Cancel_while_cycling_hides_without_launching()
    {
        _m.GestureStarted(5);
        _m.Step(false);

        Assert.Equal([new Hide()], _m.Cancel());
        Assert.Empty(_m.HoldReleased());
    }

    [Fact]
    public void Cancel_does_not_close_interactive_mode()
    {
        _m.GestureStarted(5);
        _m.Space();

        Assert.Empty(_m.Cancel());
        Assert.Equal(PopupMode.Interactive, _m.Mode);
    }

    [Fact]
    public void Escape_hides_in_any_visible_mode()
    {
        _m.GestureStarted(5);
        Assert.Equal([new Hide()], _m.Escape());

        _m.ShowInteractive(5);
        Assert.Equal([new Hide()], _m.Escape());

        Assert.Empty(_m.Escape());
    }

    [Fact]
    public void Gesture_again_in_interactive_keeps_it_open()
    {
        _m.ShowInteractive(5);

        Assert.Equal([new EnterInteractive()], _m.GestureStarted(5));
        Assert.Equal(PopupMode.Interactive, _m.Mode);
        Assert.Empty(_m.HoldReleased());
    }

    [Fact]
    public void Steps_are_ignored_outside_cycling()
    {
        Assert.Empty(_m.Step(false));
        _m.ShowInteractive(3);
        Assert.Empty(_m.Step(false));
    }

    [Fact]
    public void Focus_lost_hides_interactive_but_not_cycling()
    {
        _m.GestureStarted(5);
        Assert.Empty(_m.FocusLost());

        _m.Space();
        Assert.Equal([new Hide()], _m.FocusLost());
    }

    [Fact]
    public void Empty_list_opens_and_release_keeps_it_open()
    {
        Assert.Equal([new Show(PopupMode.Cycling), new Select(-1)], _m.GestureStarted(0));
        Assert.Empty(_m.Step(false));
        Assert.Equal([new EnterInteractive()], _m.HoldReleased());
    }

    [Fact]
    public void ShowInteractive_from_hidden_and_from_cycling()
    {
        Assert.Equal([new Show(PopupMode.Interactive), new Select(0)], _m.ShowInteractive(3));
        Assert.Empty(_m.ShowInteractive(3));

        _m.Hidden();
        _m.GestureStarted(3);
        Assert.Equal([new EnterInteractive()], _m.ShowInteractive(3));
    }

    [Fact]
    public void Hidden_resets_and_next_gesture_starts_fresh()
    {
        _m.GestureStarted(5);
        _m.Step(false);
        _m.Hidden();

        Assert.Equal([new Show(PopupMode.Cycling), new Select(0)], _m.GestureStarted(5));
    }
}

public class PopupStateMachineKeepOpenTests
{
    private readonly PopupStateMachine _m = new() { KeepOpen = true };

    [Fact]
    public void Release_launches_but_stays_open_in_interactive_mode()
    {
        _m.GestureStarted(5);
        _m.Step(false);

        Assert.Equal([new PopupEffect.EnterInteractive(), new PopupEffect.Launch(1)], _m.HoldReleased());
        Assert.Equal(PopupMode.Interactive, _m.Mode);
    }

    [Fact]
    public void Focus_loss_and_gesture_do_not_close_but_escape_does()
    {
        _m.ShowInteractive(3);

        Assert.Empty(_m.FocusLost());
        Assert.Equal([new PopupEffect.EnterInteractive()], _m.GestureStarted(3));
        Assert.Equal(PopupMode.Interactive, _m.Mode);

        Assert.Equal([new PopupEffect.Hide()], _m.Escape());
    }
}

public class PopupStateMachineLaunchOnReleaseTests
{
    [Fact]
    public void Gesture_start_alone_never_steps_the_first_step_tap_does()
    {
        var m = new PopupStateMachine();

        Assert.Equal([new PopupEffect.Show(PopupMode.Cycling), new PopupEffect.Select(0)], m.GestureStarted(3));
        Assert.Equal([new PopupEffect.Select(1)], m.Step(false));
    }

    [Fact]
    public void Reopen_gesture_then_step_then_release_launches_the_ui_selection_and_hides()
    {
        var m = new PopupStateMachine();
        m.ShowInteractive(3);

        m.GestureStarted(3);
        m.Step(false);

        Assert.Equal([new PopupEffect.LaunchSelection(), new PopupEffect.Hide()], m.HoldReleased());
        Assert.Equal(PopupMode.Hidden, m.Mode);
    }

    [Fact]
    public void Reopen_gesture_release_without_stepping_launches_nothing()
    {
        var m = new PopupStateMachine();
        m.ShowInteractive(3);

        m.GestureStarted(3);

        Assert.Empty(m.HoldReleased());
        Assert.Equal(PopupMode.Interactive, m.Mode);
    }

    [Fact]
    public void Reopen_with_keep_open_launches_and_stays_open()
    {
        var m = new PopupStateMachine { KeepOpen = true };
        m.ShowInteractive(3);

        m.GestureStarted(3);
        m.Step(false);

        Assert.Equal([new PopupEffect.LaunchSelection()], m.HoldReleased());
        Assert.Equal(PopupMode.Interactive, m.Mode);
    }

    [Fact]
    public void Off_cycling_release_after_stepping_keeps_the_launcher_open_on_the_selection()
    {
        var m = new PopupStateMachine { LaunchOnRelease = false };
        m.GestureStarted(3);
        m.Step(false);

        Assert.Equal([new PopupEffect.EnterInteractive()], m.HoldReleased());
        Assert.Equal(PopupMode.Interactive, m.Mode);
        Assert.Equal(1, m.SelectedIndex);
    }

    [Fact]
    public void Off_reopen_release_after_stepping_launches_nothing()
    {
        var m = new PopupStateMachine { LaunchOnRelease = false };
        m.ShowInteractive(3);
        m.GestureStarted(3);
        m.Step(false);

        Assert.Empty(m.HoldReleased());
    }

    [Fact]
    public void Steps_in_interactive_without_the_reopen_gesture_do_not_arm_a_launch()
    {
        var m = new PopupStateMachine();
        m.GestureStarted(3);
        m.Space();
        m.Step(false);

        Assert.Empty(m.HoldReleased());
    }

    [Fact]
    public void Space_after_reopen_cancels_the_launch_on_release()
    {
        var m = new PopupStateMachine();
        m.ShowInteractive(3);
        m.GestureStarted(3);
        m.Step(false);
        m.Space();

        Assert.Empty(m.HoldReleased());
    }
}

public class PopupStateMachineStepRuleTests
{
    [Fact]
    public void Stepping_back_to_the_first_item_then_releasing_launches_it()
    {
        var m = new PopupStateMachine();
        m.GestureStarted(3);
        m.Step(false);
        m.Step(true);

        Assert.Equal([new PopupEffect.Launch(0), new PopupEffect.Hide()], m.HoldReleased());
    }

    [Fact]
    public void Keep_open_release_without_stepping_launches_nothing()
    {
        var m = new PopupStateMachine { KeepOpen = true };
        m.GestureStarted(3);

        Assert.Equal([new PopupEffect.EnterInteractive()], m.HoldReleased());
    }
}
