using Haven.Application;
using Haven.Core;
using Haven.Desktop.Overlay;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell;

namespace Haven.Desktop.Tests;

public sealed class OverlayCompactRouteResolverTests
{
    [Fact]
    public async Task Registered_translate_uses_compact_route_and_preserves_instruction_and_attachments()
    {
        var context = new GoRoutingContext(["C:/tmp/reference.webp"], []);
        var result = await Resolver().ResolveGoAsync("translate this into French", context, TestContext.Current.CancellationToken);

        Assert.Equal(OverlayCompactRouteKind.Compact, result.Kind);
        Assert.Equal("translate", result.Mode!.Key);
        Assert.Equal(HavenAppRouteKind.Translate, result.Route!.Value.Kind);
        Assert.Equal("translate this into French", result.OriginalInstruction);
        Assert.Equal(["C:/tmp/reference.webp"], result.Attachments);
        Assert.NotSame(context, result.Context);
        Assert.Equal(context.ProjectNames, result.Context.ProjectNames);
    }

    [Fact]
    public async Task Vision_with_image_uses_compact_route()
    {
        var context = new GoRoutingContext(["C:/tmp/photo.jpg"], []);
        var result = await Resolver().ResolveGoAsync("what is in this image", context, TestContext.Current.CancellationToken);

        Assert.Equal(OverlayCompactRouteKind.Compact, result.Kind);
        Assert.Equal("vision", result.Mode!.Key);
        Assert.Equal(HavenAppRouteKind.Vision, result.Route!.Value.Kind);
    }

    [Fact]
    public async Task Tasks_uses_registered_compact_route()
    {
        var result = await Resolver().ResolveGoAsync("delegate this task", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(OverlayCompactRouteKind.Compact, result.Kind);
        Assert.Equal("tasks", result.Mode!.Key);
        Assert.Equal(HavenSurface.Tasks, result.Route!.Value.Surface);
    }

    [Fact]
    public async Task Unsupported_registered_app_returns_explicit_full_haven_escape()
    {
        var result = await Resolver().ResolveShortcutAsync("browse", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(OverlayCompactRouteKind.FullHaven, result.Kind);
        Assert.Equal("browse", result.Mode!.Key);
        Assert.Equal(HavenAppRouteKind.Browse, result.Route!.Value.Kind);
        Assert.Contains("full Haven", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Missing_registration_returns_explicit_full_haven_escape()
    {
        var result = await new OverlayCompactRouteResolver(new FakeModeRegistry(BuiltInModeSeed.Modes.Where(mode => mode.Key != "translate")))
            .ResolveShortcutAsync("translate", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(OverlayCompactRouteKind.FullHaven, result.Kind);
        Assert.Contains("not installed", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ambiguous_request_preserves_context_and_returns_clarification()
    {
        var context = new GoRoutingContext(["C:/tmp/photo.png"], ["Haven", "CAKE Bot"]);
        var result = await Resolver().ResolveGoAsync("open it", context, TestContext.Current.CancellationToken);

        Assert.Equal(OverlayCompactRouteKind.Clarification, result.Kind);
        Assert.Null(result.Mode);
        Assert.NotNull(result.Clarification);
        Assert.Equal("open it", result.OriginalInstruction);
        Assert.Equal(context.AttachmentPaths, result.Attachments);
        Assert.NotSame(context, result.Context);
        Assert.Equal(context.ProjectNames, result.Context.ProjectNames);
    }

    [Fact]
    public async Task Project_request_returns_full_haven_escape_instead_of_fake_compact_support()
    {
        var context = new GoRoutingContext([], ["Haven"]);
        var result = await Resolver().ResolveGoAsync("work on Haven", context, TestContext.Current.CancellationToken);

        Assert.Equal(OverlayCompactRouteKind.FullHaven, result.Kind);
        Assert.Null(result.Mode);
        Assert.Contains("Project", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.NotSame(context, result.Context);
        Assert.Equal(context.ProjectNames, result.Context.ProjectNames);
    }

    [Fact]
    public async Task Direct_shortcut_queries_registry_and_uses_the_same_route_policy()
    {
        var registry = new FakeModeRegistry(BuiltInModeSeed.Modes);
        var result = await new OverlayCompactRouteResolver(registry).ResolveShortcutAsync("vision", "inspect this image", new GoRoutingContext(["C:/tmp/x.png"], []), TestContext.Current.CancellationToken);

        Assert.Equal(OverlayCompactRouteKind.Compact, result.Kind);
        Assert.Equal("vision", result.Mode!.Key);
        Assert.True(registry.GetModesCallCount > 0);
        Assert.Equal(HavenAppRouteKind.Vision, result.Route!.Value.Kind);
    }

    [Fact]
    public async Task Arbitrary_tagged_utility_does_not_claim_compact_support()
    {
        var utility = CreateMode("weather", "Weather", tags: "[\"utility\"]");
        var result = await new OverlayCompactRouteResolver(new FakeModeRegistry(BuiltInModeSeed.Modes.Append(utility)))
            .ResolveShortcutAsync("weather", "show the weather", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(OverlayCompactRouteKind.FullHaven, result.Kind);
        Assert.Equal(utility.Id, result.Mode!.Id);
    }

    [Fact]
    public async Task Registered_calculator_can_use_its_real_compact_surface()
    {
        var calculator = CreateMode("calculator", "Calculator", tags: "[\"utility\"]");
        var result = await new OverlayCompactRouteResolver(new FakeModeRegistry(BuiltInModeSeed.Modes.Append(calculator)))
            .ResolveShortcutAsync("calculator", "calculate 2 + 2", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(OverlayCompactRouteKind.Compact, result.Kind);
        Assert.Equal(calculator.Id, result.Mode!.Id);
        Assert.DoesNotContain("Opened", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static OverlayCompactRouteResolver Resolver() => new(new FakeModeRegistry(BuiltInModeSeed.Modes));

    private static ModeDefinition CreateMode(string key, string name, string tags = "[]")
    {
        var now = DateTimeOffset.UtcNow;
        return new ModeDefinition(
            Guid.NewGuid(), key, name, "", "utility", HavenMode.Chat, "[]", "[]", "[]", "[]", "",
            ModeSource.Created, ModeInstallState.InstalledByUser, "Test", "1.0.0", tags, now, now);
    }

    private sealed class FakeModeRegistry(IEnumerable<ModeDefinition> modes) : IModeRegistry
    {
        private readonly IReadOnlyList<ModeDefinition> _modes = modes.ToArray();

        public int GetModesCallCount { get; private set; }

        public Task<IReadOnlyList<ModeDefinition>> GetModesAsync(CancellationToken cancellationToken)
        {
            GetModesCallCount++;
            return Task.FromResult(_modes);
        }

        public Task<ModeDefinition?> GetModeByKeyAsync(string key, CancellationToken cancellationToken)
            => Task.FromResult(_modes.FirstOrDefault(mode => mode.Key.Equals(key, StringComparison.OrdinalIgnoreCase)));

        public Task<ModeDefinition?> GetModeByIdAsync(Guid id, CancellationToken cancellationToken)
            => Task.FromResult(_modes.FirstOrDefault(mode => mode.Id == id));

        public Task UpsertModeAsync(ModeDefinition mode, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteModeByKeyAsync(string key, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<ModeVersion>> GetVersionsAsync(Guid modeId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ModeVersion>>([]);
        public Task AddVersionAsync(ModeVersion version, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<ModePermissionGrant>> GetGrantsAsync(Guid modeId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ModePermissionGrant>>([]);
        public Task UpsertGrantAsync(ModePermissionGrant grant, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
