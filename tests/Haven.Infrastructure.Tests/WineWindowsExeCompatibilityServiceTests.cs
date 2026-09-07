/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/WineWindowsExeCompatibilityServiceTests.cs.
 * What: Protects the optional Wine-backed Windows EXE compatibility seam.
 * How: Tests inject platform, Wine discovery, and process-start seams so no real Wine process is required.
 * Why: Missing Wine or an unsupported host must remain a concrete, non-boot-critical failure rather than falling through to unsafe shell execution.
 */

using System.Diagnostics;
using Haven.Infrastructure.WindowsCompatibility;

namespace Haven.Infrastructure.Tests;

public sealed class WineWindowsExeCompatibilityServiceTests
{
    [Fact]
    public void Probe_WhenHostIsNotLinux_FailsClosedWithoutLookingForWine()
    {
        var locatorCalled = false;
        var service = new WineWindowsExeCompatibilityService(
            isLinux: () => false,
            wineLocator: () =>
            {
                locatorCalled = true;
                return "/usr/bin/wine";
            },
            processStarter: _ => new Process());

        var capability = service.Probe();

        Assert.False(capability.IsAvailable);
        Assert.Equal(WindowsExeCompatibilityStatus.UnsupportedPlatform, capability.Status);
        Assert.False(locatorCalled);
    }

    [Fact]
    public void Probe_WhenWineIsMissing_ReturnsConcreteBlocker()
    {
        var service = new WineWindowsExeCompatibilityService(
            isLinux: () => true,
            wineLocator: () => null,
            processStarter: _ => new Process());

        var capability = service.Probe();

        Assert.False(capability.IsAvailable);
        Assert.Equal(WindowsExeCompatibilityStatus.WineNotFound, capability.Status);
        Assert.Contains("wine/wine64", capability.Detail, StringComparison.Ordinal);
        Assert.Contains("HAVEN_WINE_PATH", capability.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaunchAsync_WhenWineIsMissing_DoesNotStartAProcess()
    {
        using var executable = TemporaryExe.Create();
        var starterCalled = false;
        var service = new WineWindowsExeCompatibilityService(
            isLinux: () => true,
            wineLocator: () => null,
            processStarter: _ =>
            {
                starterCalled = true;
                return new Process();
            });

        var result = await service.LaunchAsync(executable.Path);

        Assert.False(result.Started);
        Assert.Equal(WindowsExeCompatibilityStatus.WineNotFound, result.Status);
        Assert.False(starterCalled);
    }

    [Fact]
    public async Task LaunchAsync_WhenWineIsAvailable_InvokesWineDirectlyWithoutShell()
    {
        using var executable = TemporaryExe.Create();
        ProcessStartInfo? captured = null;
        var service = new WineWindowsExeCompatibilityService(
            isLinux: () => true,
            wineLocator: () => "/opt/wine/bin/wine",
            processStarter: startInfo =>
            {
                captured = startInfo;
                return new Process();
            });

        var result = await service.LaunchAsync(executable.Path, ["--safe", "value with spaces"]);

        Assert.True(result.Started);
        Assert.Equal(WindowsExeCompatibilityStatus.Available, result.Status);
        Assert.NotNull(captured);
        Assert.Equal("/opt/wine/bin/wine", captured.FileName);
        Assert.False(captured.UseShellExecute);
        Assert.Collection(
            captured.ArgumentList,
            first => Assert.Equal(System.IO.Path.GetFullPath(executable.Path), first),
            second => Assert.Equal("--safe", second),
            third => Assert.Equal("value with spaces", third));
    }

    [Theory]
    [InlineData("not-an-executable.txt")]
    [InlineData("missing.exe")]
    public async Task LaunchAsync_WhenExecutableIsInvalid_FailsBeforeCapabilityProbe(string fileName)
    {
        var locatorCalled = false;
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"), fileName);
        var service = new WineWindowsExeCompatibilityService(
            isLinux: () => true,
            wineLocator: () =>
            {
                locatorCalled = true;
                return "/usr/bin/wine";
            },
            processStarter: _ => new Process());

        var result = await service.LaunchAsync(path);

        Assert.False(result.Started);
        Assert.Equal(WindowsExeCompatibilityStatus.InvalidExecutable, result.Status);
        Assert.False(locatorCalled);
    }

    private sealed class TemporaryExe : IDisposable
    {
        private TemporaryExe(string directory, string path)
        {
            Directory = directory;
            Path = path;
        }

        private string Directory { get; }

        public string Path { get; }

        public static TemporaryExe Create()
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"haven-wine-test-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, "sample.exe");
            File.WriteAllBytes(path, []);
            return new TemporaryExe(directory, path);
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
    }
}
