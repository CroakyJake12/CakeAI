using Haven.Application;
using Haven.Core;
using Haven.Desktop.Overlay;

namespace Haven.Desktop.Tests;

public sealed class OverlayUniversalSelectionTests
{
    [Fact]
    public void Envelope_bounds_first_class_selection_payloads_and_semantic_metadata()
    {
        var now = new DateTimeOffset(2026, 8, 17, 13, 0, 0, TimeSpan.Zero);
        var selections = Enumerable.Range(0, 20)
            .Select(index => new OverlaySelectionItem(
                $"control-{index}",
                OverlaySelectionKind.UiComponent,
                new OverlaySelectionBounds(index, index + 1, 120, 42),
                new string('x', 10_000),
                null,
                null,
                new OverlaySelectionSemanticMetadata(
                    "button",
                    new string('n', 700),
                    $"button-{index}",
                    "Button",
                    true,
                    false,
                    null,
                    null),
                new string('d', 700)))
            .ToList();
        var context = Context(OverlayContextKind.UiComponent, now, selections);

        var bounded = context.Bound();

        Assert.Equal(16, bounded.SelectedItems.Count);
        Assert.True(bounded.WasTruncated);
        Assert.Equal(8_192, bounded.SelectedItems[0].Text!.Length);
        Assert.Equal(512, bounded.SelectedItems[0].DisplayName!.Length);
        Assert.Equal(512, bounded.SelectedItems[0].Semantic!.AccessibleName!.Length);
        Assert.True(bounded.HasInteractiveSelection);
        Assert.True(bounded.HasPayload);
    }

    [Fact]
    public void Envelope_drops_invalid_bounds_and_marks_the_payload_bounded()
    {
        var now = DateTimeOffset.UtcNow;
        var context = Context(
            OverlayContextKind.Region,
            now,
            [new OverlaySelectionItem(
                "invalid-region",
                OverlaySelectionKind.Region,
                new OverlaySelectionBounds(double.NaN, 0, 100, 40),
                null,
                null,
                null,
                null,
                null)]);

        var bounded = context.Bound();

        Assert.Null(Assert.Single(bounded.SelectedItems).Bounds);
        Assert.True(bounded.WasTruncated);
        Assert.False(bounded.HasVisualSelection);
    }

    [Fact]
    public void Envelope_preserves_normalized_subunit_region_bounds()
    {
        var now = DateTimeOffset.UtcNow;
        var bounds = new OverlaySelectionBounds(.2, .3, .4, .25);
        var context = Context(
            OverlayContextKind.Region,
            now,
            [new OverlaySelectionItem("region", OverlaySelectionKind.Region, bounds, null, "region.png", null, null, null)]);

        var bounded = context.Bound();

        Assert.Equal(bounds, Assert.Single(bounded.SelectedItems).Bounds);
        Assert.Equal(bounds, bounded.Provenance.Bounds);
    }

    [Fact]
    public void Mixed_text_and_ui_context_does_not_gain_visual_actions_without_visual_payload()
    {
        var now = DateTimeOffset.UtcNow;
        var context = Context(
            OverlayContextKind.Mixed,
            now,
            [
                new OverlaySelectionItem("text", OverlaySelectionKind.Text, null, "hello", null, null, null, "Text"),
                new OverlaySelectionItem("button", OverlaySelectionKind.UiComponent, null, null, null, null,
                    new OverlaySelectionSemanticMetadata("button", "Submit", "submit", "Button", true, false, null, null), "Submit")
            ]);

        Assert.True(context.HasTextualSelection);
        Assert.True(context.HasInteractiveSelection);
        Assert.False(context.HasVisualSelection);
        Assert.DoesNotContain(OverlayContextActionCatalog.BuildFixed(context), action => action.Id == "analyse");
    }

    [Fact]
    public void Fixed_actions_are_specific_to_text_control_video_and_region_context()
    {
        var now = DateTimeOffset.UtcNow;
        var text = Context(
            OverlayContextKind.Text,
            now,
            [new OverlaySelectionItem("text", OverlaySelectionKind.Text, null, "Selected paragraph", null, null, null, "Paragraph")]);
        var control = Context(
            OverlayContextKind.UiComponent,
            now,
            [new OverlaySelectionItem("button", OverlaySelectionKind.UiComponent, new OverlaySelectionBounds(1, 2, 80, 36), null, null, null,
                new OverlaySelectionSemanticMetadata("button", "Submit", "submit-button", "Button", true, false, null, null), "Submit")]);
        var video = Context(
            OverlayContextKind.Video,
            now,
            [new OverlaySelectionItem("video", OverlaySelectionKind.Video, new OverlaySelectionBounds(0, 0, 640, 360), null, "frame.png", null,
                new OverlaySelectionSemanticMetadata(null, "Lesson video", null, null, true, null, "video", 42.5), "Lesson video")]);
        var region = Context(
            OverlayContextKind.Region,
            now,
            [new OverlaySelectionItem("region", OverlaySelectionKind.Region, new OverlaySelectionBounds(10, 20, 500, 300), null, "region.png", null, null, "Screen region")]);

        var textActions = OverlayContextActionCatalog.BuildFixed(text);
        Assert.Contains(textActions, action => action.Id == "copy");
        Assert.Contains(textActions, action => action.Id == "summarise");
        Assert.DoesNotContain(textActions, action => action.Id == "run-automation");

        var controlActions = OverlayContextActionCatalog.BuildFixed(control);
        Assert.Contains(controlActions, action => action.Id == "inspect-control");
        Assert.Contains(controlActions, action => action.Id == "run-automation");
        Assert.Contains(controlActions, action => action.Id == "open-in-app");
        Assert.DoesNotContain(controlActions, action => action.Id == "summarise");

        var videoActions = OverlayContextActionCatalog.BuildFixed(video);
        Assert.Contains(videoActions, action => action.Id == "analyse-frame");
        Assert.Contains(videoActions, action => action.Id == "summarise-media");
        Assert.DoesNotContain(videoActions, action => action.Id == "run-automation");

        var regionActions = OverlayContextActionCatalog.BuildFixed(region);
        Assert.Contains(regionActions, action => action.Id == "analyse");
        Assert.Contains(regionActions, action => action.Id == "ocr-copy");
    }

    [Fact]
    public async Task Ui_component_context_can_offer_permission_preserving_interaction_capabilities()
    {
        var now = new DateTimeOffset(2026, 8, 17, 13, 0, 0, TimeSpan.Zero);
        var repository = new FakeCapabilityRepository(
            Definition(
                "computer-device-use",
                "Computer / Device Use",
                "computer-use",
                "device.control",
                "[\"inspect\",\"interact\",\"verify\"]",
                CapabilityRiskClass.Consequential,
                CapabilityAvailability.PermissionRequired,
                "haven.device"),
            Definition(
                "run-command",
                "Run Command",
                "terminal",
                "workspace.run-command",
                "[\"inspect\",\"interact\"]",
                CapabilityRiskClass.Restricted,
                CapabilityAvailability.PermissionRequired,
                "haven.workspace"));
        var service = new OverlayContextActionCandidateService(new CapabilityRegistryService(repository), () => now);
        var context = Context(
            OverlayContextKind.UiComponent,
            now,
            [new OverlaySelectionItem("button", OverlaySelectionKind.UiComponent, new OverlaySelectionBounds(1, 2, 80, 36), null, null, null,
                new OverlaySelectionSemanticMetadata("button", "Submit", "submit-button", "Button", true, false, null, null), "Submit")]);

        var actions = await service.DiscoverAsync(context, CapabilityPlatform.Windows, CancellationToken.None);

        var interact = Assert.Single(actions, action => action.Id == "capability:computer-device-use:interact");
        Assert.Equal(CapabilityRiskClass.Consequential, interact.RiskClass);
        Assert.Equal(CapabilityAvailability.PermissionRequired, interact.Availability);
        Assert.Equal("haven.device", interact.ProviderId);
        Assert.Contains(actions, action => action.Id == "capability:computer-device-use:inspect");
        Assert.Contains(actions, action => action.Id == "capability:computer-device-use:verify");
        Assert.DoesNotContain(actions, action => action.Id.Contains("run-command", StringComparison.OrdinalIgnoreCase));
    }

    private static OverlayContextEnvelope Context(
        OverlayContextKind kind,
        DateTimeOffset capturedAt,
        List<OverlaySelectionItem> selections) =>
        new(
            kind,
            null,
            [],
            null,
            new OverlayContextProvenance(
                "Browser",
                "Reference page",
                selections.FirstOrDefault()?.Bounds,
                capturedAt,
                capturedAt.AddMinutes(5),
                OverlayContextPermissionState.Granted,
                "Explicit bounded user selection."),
            false,
            selections);

    private static CapabilityDefinition Definition(
        string key,
        string name,
        string iconKey,
        string implementationKey,
        string semanticActionsJson,
        CapabilityRiskClass riskClass,
        CapabilityAvailability availability,
        string providerId) =>
        new(
            Guid.NewGuid(),
            key,
            name,
            name + " description",
            CapabilityRegistryCatalog.GeneralOwner,
            iconKey,
            "Use through the registered provider.",
            implementationKey,
            semanticActionsJson,
            CapabilityPlatform.Windows,
            riskClass,
            availability,
            "[]",
            providerId,
            IsAttachable: true,
            IsAgentUsable: true,
            IsBuiltIn: true,
            IsEnabled: true,
            UpdatedAt: DateTimeOffset.UnixEpoch);

    private sealed class FakeCapabilityRepository(params CapabilityDefinition[] capabilities) : ICapabilityRepository
    {
        private readonly IReadOnlyList<CapabilityDefinition> _capabilities = capabilities;

        public Task<IReadOnlyList<CapabilityDefinition>> GetCapabilitiesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_capabilities);
        }

        public Task UpsertCapabilityAsync(CapabilityDefinition capability, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SetCapabilityEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteCustomCapabilityAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
