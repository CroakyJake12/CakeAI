using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenImage = Haven.UI.Components.Image;

namespace Haven.Desktop.Views.Shell.TopRail;

/// <summary>
/// View-only density states for the desktop taskbar. They deliberately avoid
/// session, compositor, or privileged platform dependencies so the GNOME login
/// and boot path remain outside this shell surface.
/// </summary>
internal enum TaskbarShelfState
{
    Compact,
    Standard,
    Expanded
}

internal static class TaskbarShelfLayout
{
    internal static TaskbarShelfState Resolve(double width) => width switch
    {
        < 1040d => TaskbarShelfState.Compact,
        >= 1560d => TaskbarShelfState.Expanded,
        _ => TaskbarShelfState.Standard
    };

    internal static void Apply(TopRailFinalScene scene, TaskbarShelfState state)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var metrics = state switch
        {
            TaskbarShelfState.Compact => new ShelfMetrics(60d, 12d, 6d, 82d, 86d, 106d, 176d),
            TaskbarShelfState.Expanded => new ShelfMetrics(76d, 24d, 10d, 112d, 104d, 154d, 236d),
            _ => new ShelfMetrics(68d, 18d, 8d, 96d, 96d, 124d, 204d)
        };

        scene.Root.Name = "Taskbar.Root";
        scene.Root.SetValue(HavenProperties.Height, HavenLength.Px(metrics.Height));
        scene.Root.SetValue(HavenProperties.Padding, HavenThickness.Parse($"0px {metrics.HorizontalPadding}px"));
        scene.Root.SetValue(HavenProperties.Gap, HavenLength.Px(metrics.Gap));
        scene.Root.SetValue(HavenProperties.Background, "Surface");

        // Reuse the existing HomeRequested seam. New Haven already resolves it
        // to the real Go route, so this is a functional launcher rather than a
        // decorative taskbar label.
        foreach (var image in scene.LogoHost.DescendantsAndSelf().OfType<HavenImage>())
            image.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);

        scene.LogoHost.Name = "Taskbar.GoHost";
        scene.LogoHost.SetValue(HavenProperties.Width, HavenLength.Px(metrics.GoWidth));
        scene.LogoHost.SetValue(HavenProperties.Height, HavenLength.Px(42));
        scene.LogoButton.Name = "Taskbar.Go";
        scene.LogoButton.Variant = ButtonVariant.Primary;
        scene.LogoButton.IconKey = "search";
        scene.LogoButton.Content = "Go";
        scene.LogoButton.Accessibility.AccessibleName = "Go";
        scene.LogoButton.SetValue(HavenProperties.Width, HavenLength.Px(metrics.GoWidth));
        scene.LogoButton.SetValue(HavenProperties.MinWidth, HavenLength.Px(metrics.GoWidth));
        scene.LogoButton.SetValue(HavenProperties.Height, HavenLength.Px(42));
        scene.LogoButton.SetValue(HavenProperties.MinHeight, HavenLength.Px(42));
        scene.LogoButton.SetValue(HavenProperties.Background, "Accent");
        scene.LogoButton.SetValue(HavenProperties.Foreground, "TextOnAccent");

        SetWidth(scene.AppsHost, scene.AppsButton, metrics.AppsWidth);
        SetWidth(scene.ActionsHost, scene.ActionsButton, metrics.ActionsWidth);
        SetWidth(scene.ModelHost, scene.ModelButton, metrics.ModelWidth);

        scene.AppsHost.Name = "Taskbar.AppsHost";
        scene.ActionsHost.Name = "Taskbar.ActionsHost";
        scene.ModelHost.Name = "Taskbar.ModelHost";
        scene.NotificationHost.Name = "Taskbar.Notifications";
        scene.SearchButton.Name = "Taskbar.Search";

        // Preserve semantic event names and button behavior while exposing the
        // visible shell composition as bottom taskbar chrome.
        scene.AppsButton.Accessibility.AccessibleName = "Apps";
        scene.ActionsButton.Accessibility.AccessibleName = "Actions";
        scene.SearchButton.Accessibility.AccessibleName = "Search Haven";
    }

    private static void SetWidth(Container host, HavenButton button, double width)
    {
        host.SetValue(HavenProperties.MinWidth, HavenLength.Px(width));
        button.SetValue(HavenProperties.MinWidth, HavenLength.Px(width));
    }

    private readonly record struct ShelfMetrics(
        double Height,
        double HorizontalPadding,
        double Gap,
        double GoWidth,
        double AppsWidth,
        double ActionsWidth,
        double ModelWidth);
}
