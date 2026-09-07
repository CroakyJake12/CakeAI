using Haven.Application;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class ApkLaunchServiceTests
{
    [Fact]
    public async Task GetCapabilityAsync_FailsClosed_WhenNoRuntimeProviderIsRegistered()
    {
        var service = new ApkLaunchService();

        var capability = await service.GetCapabilityAsync(CancellationToken.None);

        Assert.False(capability.IsAvailable);
        Assert.Equal("none", capability.RuntimeId);
        Assert.Contains("No APK runtime provider", capability.UnavailableReason ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaunchAsync_RejectsNonApkBeforeRuntimeProbe()
    {
        var provider = new RecordingProvider(available: true);
        var service = new ApkLaunchService([provider]);
        var path = Path.Combine(Path.GetTempPath(), "haven-apk-launch-test.txt");

        var result = await service.LaunchAsync(new ApkLaunchRequest(path), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ApkLaunchStatus.InvalidRequest, result.Status);
        Assert.Equal(0, provider.ProbeCount);
        Assert.Equal(0, provider.LaunchCount);
    }

    [Fact]
    public async Task LaunchAsync_DoesNotDispatch_WhenRuntimeIsUnavailable()
    {
        var path = CreateTemporaryApk();
        try
        {
            var provider = new RecordingProvider(available: false, unavailableReason: "Runtime is not installed.");
            var service = new ApkLaunchService([provider]);

            var result = await service.LaunchAsync(new ApkLaunchRequest(path), CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal(ApkLaunchStatus.RuntimeUnavailable, result.Status);
            Assert.Equal(1, provider.ProbeCount);
            Assert.Equal(0, provider.LaunchCount);
            Assert.Contains("not installed", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LaunchAsync_DelegatesCanonicalExistingApk_WhenRuntimeIsAvailable()
    {
        var path = CreateTemporaryApk();
        try
        {
            var provider = new RecordingProvider(available: true);
            var service = new ApkLaunchService([provider]);

            var result = await service.LaunchAsync(new ApkLaunchRequest(path), CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal(ApkLaunchStatus.Launched, result.Status);
            Assert.Equal("test-runtime", result.RuntimeId);
            Assert.Equal(1, provider.ProbeCount);
            Assert.Equal(1, provider.LaunchCount);
            Assert.Equal(Path.GetFullPath(path), provider.LastRequest?.ApkPath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LaunchAsync_ConvertsProviderExceptionToObservedFailure()
    {
        var path = CreateTemporaryApk();
        try
        {
            var provider = new RecordingProvider(available: true, throwOnLaunch: true);
            var service = new ApkLaunchService([provider]);

            var result = await service.LaunchAsync(new ApkLaunchRequest(path), CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal(ApkLaunchStatus.LaunchFailed, result.Status);
            Assert.Equal("test-runtime", result.RuntimeId);
            Assert.Contains(nameof(InvalidOperationException), result.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTemporaryApk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"haven-apk-launch-{Guid.NewGuid():N}.apk");
        File.WriteAllBytes(path, [0x50, 0x4B, 0x03, 0x04]);
        return path;
    }

    private sealed class RecordingProvider(
        bool available,
        string? unavailableReason = null,
        bool throwOnLaunch = false) : IApkRuntimeProvider
    {
        public int ProbeCount { get; private set; }
        public int LaunchCount { get; private set; }
        public ApkLaunchRequest? LastRequest { get; private set; }

        public Task<ApkRuntimeCapability> ProbeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProbeCount++;
            return Task.FromResult(new ApkRuntimeCapability(
                available,
                "test-runtime",
                "Test APK Runtime",
                available ? null : unavailableReason ?? "Unavailable for test."));
        }

        public Task<ApkLaunchResult> LaunchAsync(ApkLaunchRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LaunchCount++;
            LastRequest = request;

            if (throwOnLaunch)
                throw new InvalidOperationException("Synthetic runtime failure.");

            return Task.FromResult(ApkLaunchResult.Success("Observed test launch.", "test-runtime"));
        }
    }
}
