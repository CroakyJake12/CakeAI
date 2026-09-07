using Haven.Desktop.Views.Pages.ActivityLog;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Tests;

public sealed class ActivityLogHavenSceneTests
{
    [Fact]
    public void Scene_renders_activity_rows_with_existing_metadata_and_stable_event_names()
    {
        using var scene = new ActivityLogHavenScene();
        var firstUpdated = new DateTimeOffset(2026, 8, 30, 18, 42, 0, TimeSpan.Zero);
        var secondUpdated = firstUpdated.AddMinutes(-20);

        scene.SetItems(
        [
            new ActivityLogRow("HUI migration", "Chat", firstUpdated),
            new ActivityLogRow("Revision plan", "Study", secondUpdated)
        ]);
        scene.SetStatus("2 conversations");

        Assert.Equal("Search conversations…", scene.Search.Placeholder);
        Assert.Equal(ButtonVariant.Primary, scene.Refresh.Variant);
        Assert.Equal(2, scene.ItemButtons.Count);
        Assert.Equal("ActivityLog.List.Item0", scene.ItemButtons[0].Name);
        Assert.Equal("ActivityLog.List.Item1", scene.ItemButtons[1].Name);
        Assert.Equal("HUI migration", scene.ItemButtons[0].Content);
        Assert.Contains(
            scene.Items.DescendantsAndSelf().OfType<HavenText>(),
            text => text.Content == $"Chat · {firstUpdated:MMM dd, HH:mm}");
        Assert.Equal("2 conversations", scene.Status.Content);
    }

    [Fact]
    public void Scene_translates_hui_interactions_to_activity_log_adapter_events()
    {
        using var scene = new ActivityLogHavenScene();
        var refreshCount = 0;
        string? search = null;
        string? item = null;
        var pointerEvents = new List<string>();
        scene.RefreshRequested += (_, _) => refreshCount++;
        scene.SearchChanged += (_, value) => search = value;
        scene.ItemInvoked += (_, value) => item = value;
        scene.PointerEventRequested += (_, value) => pointerEvents.Add(value);
        scene.SetItems([new ActivityLogRow("One", "Chat", DateTimeOffset.UtcNow)]);

        scene.Search.Text = "  one  ";
        scene.Refresh.Invoke();
        scene.ItemButtons[0].Invoke();
        scene.Refresh.SetState(HavenElementState.Hover, true);
        scene.Refresh.SetState(HavenElementState.Pressed, true);
        scene.Refresh.SetState(HavenElementState.Pressed, false);
        scene.Refresh.SetState(HavenElementState.Hover, false);

        Assert.Equal("one", search);
        Assert.Equal(1, refreshCount);
        Assert.Equal("ActivityLog.List.Item0", item);
        Assert.Equal(
        [
            "ActivityLog.Actions.Refresh.Hover",
            "ActivityLog.Actions.Refresh.Press",
            "ActivityLog.Actions.Refresh.Release",
            "ActivityLog.Actions.Refresh.Leave"
        ], pointerEvents);
    }
}
