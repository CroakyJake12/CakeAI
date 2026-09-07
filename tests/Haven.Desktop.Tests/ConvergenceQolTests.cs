using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Canvas;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Shell.TopRail;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class ConvergenceQolTests
{
    [AvaloniaFact]
    public void Streaming_composer_queues_typed_follow_up_but_empty_composer_still_stops()
    {
        using var scene = new ChatHavenScene();
        scene.AttachmentRemoveRequested += (_, _) => { };
        var sends = 0;
        var stops = 0;
        scene.SendRequested += (_, _) => sends++;
        scene.StopRequested += (_, _) => stops++;

        var host = new HavenSceneControl { Root = scene.Root };
        var window = new Window { Width = 1000, Height = 760, Content = host };
        try
        {
            window.Show();
            window.UpdateLayout();
            var router = new HavenInputRouter(scene.Root);

            scene.SetSending(true, modelAvailable: true);
            scene.Instruction.Text = "Follow this with the test results";
            Click(router, scene.SendButton);

            Assert.Equal(0, sends);
            Assert.Equal(0, stops);
            Assert.Equal(string.Empty, scene.Instruction.Text);
            Assert.Contains("queued", scene.Status.Content, StringComparison.OrdinalIgnoreCase);

            Click(router, scene.SendButton);
            Assert.Equal(0, sends);
            Assert.Equal(1, stops);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Composer_thread_settings_route_through_existing_chat_response_selection()
    {
        using var scene = new ChatHavenScene();
        scene.AttachmentRemoveRequested += (_, _) => { };
        AddMenuSelection? selected = null;
        scene.CatalogItemSelected += (_, selection) => selected = selection;

        var host = new HavenSceneControl { Root = scene.Root };
        var window = new Window { Width = 1000, Height = 760, Content = host };
        try
        {
            window.Show();
            window.UpdateLayout();
            var router = new HavenInputRouter(scene.Root);
            var settings = scene.Chatbox.GetComponent<Haven.UI.Components.Button>("ChatSettings");

            Assert.Equal("Manage chat response settings", settings.Accessibility.AccessibleName);
            Click(router, settings);
            window.UpdateLayout();

            var popup = Assert.Single(scene.Root.Children.OfType<PopupMenu>());
            var justChat = popup.Card.DescendantsAndSelf()
                .OfType<Haven.UI.Components.Button>()
                .Single(button => button.Content == "Actions: Just Chat");
            Click(router, justChat);

            Assert.NotNull(selected);
            Assert.Equal(AddMenu.AddMenuAction.AllowActions, selected!.Kind);
            Assert.Equal(ChatActionMode.JustChat, Assert.IsType<ChatActionMode>(selected.Item));
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Canvas_library_landing_exposes_new_import_and_empty_recent_state_without_release_chrome()
    {
        using var scene = new CanvasHavenScene();

        scene.SetLibrary(Array.Empty<NotesDocumentSummary>());

        var library = scene.Root.DescendantsAndSelf().OfType<Container>()
            .Single(element => element.Name == "Canvas.Library");
        Assert.Equal(HavenVisibility.Visible, library.GetValue(HavenProperties.Visibility));
        Assert.Contains(library.DescendantsAndSelf().OfType<Haven.UI.Components.Button>(), button => button.Name == "Canvas.Library.New");
        Assert.Contains(library.DescendantsAndSelf().OfType<Haven.UI.Components.Button>(), button => button.Name == "Canvas.Library.Import");
        Assert.Contains(library.DescendantsAndSelf().OfType<Text>(), text => text.Content.Contains("No canvases yet", StringComparison.Ordinal));

        scene.ShowWorkspace();
        Assert.Equal(HavenVisibility.Collapsed, library.GetValue(HavenProperties.Visibility));
    }

    private static void Click(HavenInputRouter router, HavenElement element)
    {
        var point = new HavenPoint(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);
        router.PointerPressed(point);
        Assert.True(router.PointerReleased(point));
    }
}
