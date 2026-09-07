using Haven.Application;
using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

public sealed class SpacesAppSurfaceTests
{
    [Fact]
    public void Navigation_exposes_the_bounded_Home_Chat_Study_Tasks_Research_order()
    {
        Assert.Equal(
            [
                SpacesDestination.Home,
                SpacesDestination.Chat,
                SpacesDestination.Study,
                SpacesDestination.Tasks,
                SpacesDestination.Research
            ],
            SpacesAppSurface.Navigation.Select(item => item.Destination));
    }

    [Fact]
    public async Task Home_navigation_stays_in_the_app_and_preserves_existing_space_scope()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        await registry.SetCurrentSpaceIdAsync(SpaceRegistry.ResearchSpaceId);
        var host = new RecordingHost();
        var surface = new SpacesAppSurface(registry, host);

        await surface.NavigateAsync(SpacesDestination.Home);

        Assert.True(host.HomeOpened);
        Assert.Null(host.Mode);
        Assert.Null(host.Space);
        Assert.Equal(SpaceRegistry.ResearchSpaceId, await registry.GetCurrentSpaceIdAsync());
        Assert.Equal(SpacesDestination.Home, surface.CurrentDestination);
    }

    [Fact]
    public async Task Chat_navigation_opens_existing_chat_mode_and_clears_space_scope()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        await registry.SetCurrentSpaceIdAsync(SpaceRegistry.StudySpaceId);
        var host = new RecordingHost();
        var surface = new SpacesAppSurface(registry, host);

        await surface.NavigateAsync(SpacesDestination.Chat);

        Assert.Equal(HavenMode.Chat, host.Mode);
        Assert.Null(host.Space);
        Assert.Null(await registry.GetCurrentSpaceIdAsync());
        Assert.Equal(SpacesDestination.Chat, surface.CurrentDestination);
    }

    [Theory]
    [InlineData(SpacesDestination.Study, "b1000000-0000-0000-0000-000000000001", SpaceKind.Study)]
    [InlineData(SpacesDestination.Tasks, "b1000000-0000-0000-0000-000000000004", SpaceKind.Agent)]
    [InlineData(SpacesDestination.Research, "b1000000-0000-0000-0000-000000000003", SpaceKind.Research)]
    public async Task Built_in_destinations_open_existing_space_records(
        SpacesDestination destination,
        string expectedSpaceId,
        SpaceKind expectedKind)
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var host = new RecordingHost();
        var surface = new SpacesAppSurface(registry, host);

        await surface.NavigateAsync(destination);

        var expectedId = Guid.Parse(expectedSpaceId);
        Assert.NotNull(host.Space);
        Assert.Equal(expectedId, host.Space!.Id);
        Assert.Equal(expectedKind, host.Space.Kind);
        Assert.Equal(expectedId, await registry.GetCurrentSpaceIdAsync());
        Assert.Equal(destination, surface.CurrentDestination);
    }

    [Fact]
    public async Task Failed_launch_restores_previous_scope_and_does_not_select_failed_destination()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        await registry.SetCurrentSpaceIdAsync(SpaceRegistry.ResearchSpaceId);
        var host = new RecordingHost { Failure = new InvalidOperationException("launch failed") };
        var surface = new SpacesAppSurface(registry, host);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => surface.NavigateAsync(SpacesDestination.Study));

        Assert.Equal("launch failed", error.Message);
        Assert.Equal(SpaceRegistry.ResearchSpaceId, await registry.GetCurrentSpaceIdAsync());
        Assert.Equal(SpacesDestination.Home, surface.CurrentDestination);
    }

    private sealed class RecordingHost : ISpacesNavigationHost
    {
        public bool HomeOpened { get; private set; }
        public HavenMode? Mode { get; private set; }
        public SpaceDefinition? Space { get; private set; }
        public Exception? Failure { get; init; }

        public Task OpenHomeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HomeOpened = true;
            return CompleteOrFail();
        }

        public Task OpenModeAsync(HavenMode mode, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Mode = mode;
            return CompleteOrFail();
        }

        public Task OpenSpaceAsync(SpaceDefinition space, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Space = space;
            return CompleteOrFail();
        }

        private Task CompleteOrFail() =>
            Failure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
    }

    private sealed class MemorySettingsStore : IVersionedSettingsStore
    {
        private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_values.TryGetValue(key, out var value) ? (T?)value : null);
        }

        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values.Remove(key);
            return Task.CompletedTask;
        }

        public Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SettingsImportResult> ImportAsync(
            SettingsExportManifest manifest,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
