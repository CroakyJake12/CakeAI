using Haven.UI;
using Haven.UI.Components;
using Xunit;

namespace Haven.UI.Tests;

public sealed class HavenAccessibilityPerformanceContractTests
{
    [Fact]
    public void Scene_state_flags_keep_accessibility_state_in_sync()
    {
        var button = new Button();

        button.SetState(HavenElementState.Selected, true);
        button.SetState(HavenElementState.Disabled, true);
        button.SetState(HavenElementState.Checked, true);

        Assert.True(button.Accessibility.Selected);
        Assert.False(button.Accessibility.Enabled);
        Assert.True(button.Accessibility.Checked);

        button.SetState(HavenElementState.Selected | HavenElementState.Disabled | HavenElementState.Checked, false);

        Assert.False(button.Accessibility.Selected);
        Assert.True(button.Accessibility.Enabled);
        Assert.False(button.Accessibility.Checked);
    }

    [Fact]
    public void Button_owns_enter_and_space_keyboard_activation_semantics()
    {
        var button = new Button { Content = "Save" };
        var keyboard = Assert.IsAssignableFrom<IHavenKeyboardInputTarget>(button);
        var invoked = 0;
        button.Invoked += (_, _) => invoked++;

        Assert.False(keyboard.KeyDown(new HavenKeyInput(HavenKey.Left, HavenKeyModifiers.None)));
        Assert.True(keyboard.KeyDown(new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None)));
        Assert.True(button.State.HasFlag(HavenElementState.Pressed));
        Assert.True(keyboard.KeyUp(new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None)));
        Assert.False(button.State.HasFlag(HavenElementState.Pressed));
        Assert.Equal(1, invoked);

        button.SetState(HavenElementState.Disabled, true);
        Assert.True(keyboard.KeyDown(new HavenKeyInput(HavenKey.Space, HavenKeyModifiers.None)));
        Assert.True(keyboard.KeyUp(new HavenKeyInput(HavenKey.Space, HavenKeyModifiers.None)));
        Assert.Equal(1, invoked);
    }

    [Fact]
    public void Enabling_reduced_motion_settles_existing_animation_without_another_frame()
    {
        var element = new Container();
        element.SetValue(HavenProperties.Opacity, .25d);
        var definition = new HavenTransitionDefinition(
            "Opacity",
            TimeSpan.FromSeconds(2),
            "Linear",
            ["Opacity"],
            1);
        var engine = new HavenAnimationEngine();
        var lifecycle = new List<HavenAnimationLifecycleState>();
        engine.LifecycleChanged += (_, args) => lifecycle.Add(args.State);
        var from = engine.Capture(element, definition.Properties, includeAnimationValues: false);
        element.SetValue(HavenProperties.Opacity, 1d);
        var to = engine.Capture(element, definition.Properties, includeAnimationValues: false);

        Assert.True(engine.StartTransition(element, definition, from, to, DateTimeOffset.UtcNow));
        Assert.True(engine.HasActiveAnimations);

        engine.MotionPolicy = new HavenMotionPolicy(ReducedMotion: true);

        Assert.False(engine.HasActiveAnimations);
        Assert.Equal(1d, element.GetValue(HavenProperties.Opacity));
        Assert.Equal([HavenAnimationLifecycleState.Started, HavenAnimationLifecycleState.Completed], lifecycle);
    }

    [Fact]
    public void Property_precedence_reads_are_allocation_free_after_warmup()
    {
        var button = new Button();
        button.SetValue(HavenProperties.Opacity, .9d, HavenValueSource.SystemClass);
        button.SetValue(HavenProperties.Opacity, .8d, HavenValueSource.UserClass);
        button.SetValue(HavenProperties.Opacity, .7d, HavenValueSource.Explicit);
        button.SetValue(HavenProperties.Opacity, .6d, HavenValueSource.State);
        button.SetValue(HavenProperties.Opacity, .5d, HavenValueSource.Animation);

        for (var index = 0; index < 256; index++)
            _ = button.GetValue(HavenProperties.Opacity, HavenValueSource.State);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var sum = 0d;
        for (var index = 0; index < 10_000; index++)
            sum += (double)button.GetValue(HavenProperties.Opacity, HavenValueSource.State)!;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        GC.KeepAlive(sum);
        Assert.Equal(0, allocated);
        Assert.Equal(.6d, button.GetValue(HavenProperties.Opacity, HavenValueSource.State));
    }
}
