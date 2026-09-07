using Haven.Core;
using Haven.Desktop.Overlay;
using Haven.Desktop.Views.Pages.Imagine;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class OverlayShellHavenSceneTests
{
    [Fact]
    public void Scene_projects_sessions_permission_and_dynamic_actions()
    {
        var now = new DateTimeOffset(2026, 8, 17, 7, 0, 0, TimeSpan.Zero);
        var activeId = Guid.NewGuid();
        var pinnedId = Guid.NewGuid();
        var context = new OverlayContextEnvelope(
            OverlayContextKind.Text,
            "A bounded selection from another application.",
            [],
            null,
            new OverlayContextProvenance(
                "Browser",
                "Revision notes",
                new OverlaySelectionBounds(12, 24, 640, 280),
                now,
                now.AddMinutes(5),
                OverlayContextPermissionState.Granted,
                "Explicit selection."));

        var snapshot = new OverlayWorkspaceSnapshot(
            activeId,
            [
                Session(activeId, "Chat", false, context, now),
                Session(pinnedId, "Pinned chat", true, null, now.AddSeconds(1))
            ]);

        using var scene = new OverlayShellHavenScene();
        scene.ApplySnapshot(snapshot, activeId);
        scene.SetActions(
        [
            new OverlayContextActionDescriptor("ask-haven", "Ask Haven", "sparkles", true),
            new OverlayContextActionDescriptor(
                "capability:web-search:search", "Web Search · Search", "web-search", true, true,
                "browser.search", CapabilityRiskClass.ReadOnly, CapabilityAvailability.PermissionRequired,
                "haven.browser", "browser.search")
        ]);

        Assert.Equal("Chat", scene.TitleText.Content);
        Assert.Contains("Browser", scene.SourceText.Content);
        Assert.Contains("Capture allowed", scene.PermissionText.Content);
        Assert.Contains("bounded selection", scene.ContextSummary.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, scene.SessionTabs.Items.Count);
        Assert.Equal(ButtonVariant.Primary, scene.SessionTabs.GetItem(activeId.ToString("N")).GetComponent<Button>("Activate").Variant);
        Assert.Equal(ButtonVariant.Secondary, scene.SessionTabs.GetItem(pinnedId.ToString("N")).GetComponent<Button>("Activate").Variant);
        Assert.Equal(ButtonVariant.Primary, scene.Actions.GetItem("ask-haven-0").GetComponent<Button>("Invoke").Variant);
        Assert.Contains("asks", scene.Actions.GetItem("capability-web-search-search-1").GetComponent<Button>("Invoke").Content);
    }

    [Fact]
    public void Scene_summarises_universal_selection_items_in_context_panel()
    {
        var now = new DateTimeOffset(2026, 8, 18, 8, 0, 0, TimeSpan.Zero);
        var sessionId = Guid.NewGuid();
        var context = new OverlayContextEnvelope(
            OverlayContextKind.UiComponent,
            null,
            [],
            null,
            new OverlayContextProvenance("Browser", "Checkout", new OverlaySelectionBounds(20, 40, 100, 42), now, now.AddMinutes(5), OverlayContextPermissionState.Granted, "Explicit selection."),
            false,
            [new OverlaySelectionItem(
                "submit",
                OverlaySelectionKind.UiComponent,
                new OverlaySelectionBounds(20, 40, 100, 42),
                null,
                null,
                null,
                new OverlaySelectionSemanticMetadata("button", "Submit", "submit-button", "Button", true, false, null, null),
                "Submit button")]);

        using var scene = new OverlayShellHavenScene();
        scene.ApplySnapshot(new OverlayWorkspaceSnapshot(sessionId, [Session(sessionId, "Chat", false, context, now)]), sessionId);

        Assert.Equal("Selected UI component · Submit button · Button", scene.ContextSummary.Content);
    }

    [Fact]
    public void Scene_projects_collapsed_state_as_expand_action()
    {
        var now = new DateTimeOffset(2026, 8, 17, 16, 0, 0, TimeSpan.Zero);
        var sessionId = Guid.NewGuid();
        var collapsed = Session(sessionId, "Chat", false, null, now) with { IsCollapsed = true };

        using var scene = new OverlayShellHavenScene();
        scene.ApplySnapshot(new OverlayWorkspaceSnapshot(sessionId, [collapsed]), sessionId);

        Assert.Equal("Expand", scene.CollapseButton.Content);
        Assert.Equal(ButtonVariant.Secondary, scene.CollapseButton.Variant);
    }

    [Fact]
    public void Scene_projects_compact_app_identity_and_back_navigation()
    {
        var now = new DateTimeOffset(2026, 8, 17, 16, 0, 0, TimeSpan.Zero);
        var sessionId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var session = new OverlaySessionState(
            sessionId, "chat", "Chat", conversationId, false, true,
            OverlaySurfaceGeometry.Default, null, now, now, "Browser");

        using var scene = new OverlayShellHavenScene();
        scene.ApplySnapshot(new OverlayWorkspaceSnapshot(sessionId, [session]), sessionId);

        Assert.Equal("Chat", scene.AppHostTitle.Content);
        Assert.DoesNotContain(conversationId.ToString("N"), scene.AppHostIdentity.Content?.ToString());
        Assert.Equal(HavenVisibility.Collapsed, scene.BackButton.GetValue(HavenProperties.Visibility));

        scene.SetBackNavigation(true, "Go routing");

        Assert.Equal(HavenVisibility.Visible, scene.BackButton.GetValue(HavenProperties.Visibility));
        Assert.Equal("Back to Go routing", scene.BackButton.Accessibility.AccessibleName);
    }

    [Fact]
    public void Compact_host_restores_home_translate_and_vision_routes_in_order()
    {
        using var scene = new OverlayShellHavenScene();
        var translate = new OverlayCompactAppRoute("translate", "Translate", "Translate");
        var vision = new OverlayCompactAppRoute("vision", "Vision", "Vision");

        Assert.True(scene.NavigateTo(translate));
        Assert.Equal("Translate", scene.CurrentRoute.Title);
        Assert.Equal(HavenVisibility.Visible, scene.AppHostPanel.GetValue(HavenProperties.Visibility));

        Assert.True(scene.NavigateTo(vision));
        Assert.Equal("Vision", scene.CurrentRoute.Title);

        Assert.True(scene.NavigateBack());
        Assert.Equal("Translate", scene.CurrentRoute.Title);
        Assert.Equal("Back to Overlay home", scene.BackButton.Accessibility.AccessibleName);

        Assert.True(scene.NavigateBack());
        Assert.True(scene.CurrentRoute.IsHome);
        Assert.Equal(HavenVisibility.Collapsed, scene.AppHostPanel.GetValue(HavenProperties.Visibility));
        Assert.Equal(HavenVisibility.Collapsed, scene.BackButton.GetValue(HavenProperties.Visibility));
    }

    [Fact]
    public void Header_uses_overlay_accent_surface_tokens_and_floating_shadow()
    {
        using var scene = new OverlayShellHavenScene();
        var header = scene.Root.DescendantsAndSelf().Single(element => element.Name == "Overlay.Header");

        Assert.Equal("Overlay", header.GetValue(HavenProperties.Background));
        Assert.Equal("AccentSecondary", header.GetValue(HavenProperties.BorderColor));
        Assert.Equal("Floating", header.GetValue(HavenProperties.Shadow));
        Assert.Equal("AccentSecondaryGlow", header.GetValue(HavenProperties.Glow));
        Assert.Equal(HavenLength.Px(1), header.GetValue(HavenProperties.BorderWidth));
    }

    [Fact]
    public void Real_capture_preview_starts_in_select_mode_and_disables_apply_until_drag()
    {
        var root = Path.Combine(Path.GetTempPath(), "haven-overlay-preview-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "capture.jpg");
        File.WriteAllBytes(source, [1, 2, 3]);
        try
        {
            using var scene = new OverlayShellHavenScene();
            scene.ShowRegionDraft(source, "Captured real screen content.");

            Assert.Equal(VisionInteractionMode.SelectRegion, scene.RegionPreview.Mode);
            Assert.Equal(HavenVisibility.Visible, scene.RegionPreviewPanel.GetValue(HavenProperties.Visibility));
            Assert.False(scene.ApplySelectionButton.GetValue(HavenProperties.Enabled));
            Assert.Contains("Captured real", scene.RegionStatus.Content);

            scene.ClearRegionDraft();

            Assert.Equal(HavenVisibility.Collapsed, scene.RegionPreviewPanel.GetValue(HavenProperties.Visibility));
            Assert.False(scene.ApplySelectionButton.GetValue(HavenProperties.Enabled));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Capture_error_is_visible_without_fabricating_a_preview()
    {
        using var scene = new OverlayShellHavenScene();

        scene.SetRegionStatus("Screen capture denied by Windows.");

        Assert.Equal(HavenVisibility.Visible, scene.RegionPreviewPanel.GetValue(HavenProperties.Visibility));
        Assert.Equal(HavenVisibility.Visible, scene.ContextPanel.GetValue(HavenProperties.Visibility));
        Assert.Equal("Screen capture denied by Windows.", scene.RegionStatus.Content);
        Assert.Null(scene.RegionPreview.Source);
        Assert.False(scene.ApplySelectionButton.GetValue(HavenProperties.Enabled));
    }

    private static OverlaySessionState Session(
        Guid id,
        string title,
        bool pinned,
        OverlayContextEnvelope? context,
        DateTimeOffset now) =>
        new(id, "chat", title, Guid.NewGuid(), pinned, true, OverlaySurfaceGeometry.Default, context, now, now,
            context?.Provenance.SourceApplication);
}
