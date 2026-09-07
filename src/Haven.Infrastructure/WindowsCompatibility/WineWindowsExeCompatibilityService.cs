/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/WindowsCompatibility/WineWindowsExeCompatibilityService.cs.
 * What: Provides a bounded Windows .exe compatibility seam for Linux hosts through Wine.
 * How: Callers probe capability first; LaunchAsync repeats the probe and invokes Wine directly without a shell.
 * Why: HavenOS may offer Windows app compatibility when Wine is installed, but missing compatibility support must never affect boot or existing app startup.
 * Maintenance: Keep this service fail-closed, side-effect free during construction/probing, and free of shell command composition.
 */

using System.ComponentModel;
using System.Diagnostics;

namespace Haven.Infrastructure.WindowsCompatibility;

public enum WindowsExeCompatibilityStatus
{
    Available,
    UnsupportedPlatform,
    WineNotFound,
    InvalidExecutable,
    LaunchFailed,
}

public sealed record WindowsExeCompatibilityCapability(
    bool IsAvailable,
    WindowsExeCompatibilityStatus Status,
    string Detail,
    string? WineExecutable = null);

public sealed record WindowsExeLaunchResult(
    bool Started,
    WindowsExeCompatibilityStatus Status,
    string Detail);

public interface IWindowsExeCompatibilityService
{
    WindowsExeCompatibilityCapability Probe();

    Task<WindowsExeLaunchResult> LaunchAsync(
        string executablePath,
        IReadOnlyList<string>? arguments = null,
        CancellationToken cancellationToken = default);
}

public sealed class WineWindowsExeCompatibilityService : IWindowsExeCompatibilityService
{
    private const string WinePathEnvironmentVariable = "HAVEN_WINE_PATH";

    private readonly Func<bool> _isLinux;
    private readonly Func<string?> _wineLocator;
    private readonly Func<ProcessStartInfo, Process?> _processStarter;

    public WineWindowsExeCompatibilityService()
        : this(OperatingSystem.IsLinux, LocateWineExecutable, Process.Start)
    {
    }

    internal WineWindowsExeCompatibilityService(
        Func<bool> isLinux,
        Func<string?> wineLocator,
        Func<ProcessStartInfo, Process?> processStarter)
    {
        _isLinux = isLinux ?? throw new ArgumentNullException(nameof(isLinux));
        _wineLocator = wineLocator ?? throw new ArgumentNullException(nameof(wineLocator));
        _processStarter = processStarter ?? throw new ArgumentNullException(nameof(processStarter));
    }

    public WindowsExeCompatibilityCapability Probe()
    {
        if (!_isLinux())
        {
            return new WindowsExeCompatibilityCapability(
                false,
                WindowsExeCompatibilityStatus.UnsupportedPlatform,
                "Wine-backed Windows EXE compatibility is only exposed on Linux hosts.");
        }

        var wineExecutable = _wineLocator();
        if (string.IsNullOrWhiteSpace(wineExecutable))
        {
            return new WindowsExeCompatibilityCapability(
                false,
                WindowsExeCompatibilityStatus.WineNotFound,
                $"Wine is unavailable. Set {WinePathEnvironmentVariable} to an executable Wine binary or install wine/wine64 on PATH.");
        }

        return new WindowsExeCompatibilityCapability(
            true,
            WindowsExeCompatibilityStatus.Available,
            "Wine executable is available for an explicit EXE launch request.",
            wineExecutable);
    }

    public Task<WindowsExeLaunchResult> LaunchAsync(
        string executablePath,
        IReadOnlyList<string>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var executableValidation = ValidateExecutable(executablePath);
        if (executableValidation is not null)
        {
            return Task.FromResult(executableValidation);
        }

        var fullExecutablePath = Path.GetFullPath(executablePath);
        var capability = Probe();
        if (!capability.IsAvailable || string.IsNullOrWhiteSpace(capability.WineExecutable))
        {
            return Task.FromResult(new WindowsExeLaunchResult(false, capability.Status, capability.Detail));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = capability.WineExecutable,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(fullExecutablePath) ?? Environment.CurrentDirectory,
        };

        startInfo.ArgumentList.Add(fullExecutablePath);
        if (arguments is not null)
        {
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        try
        {
            var process = _processStarter(startInfo);
            if (process is null)
            {
                return Task.FromResult(new WindowsExeLaunchResult(
                    false,
                    WindowsExeCompatibilityStatus.LaunchFailed,
                    "Wine did not start a process for the requested executable."));
            }

            return Task.FromResult(new WindowsExeLaunchResult(
                true,
                WindowsExeCompatibilityStatus.Available,
                "The executable was handed to Wine successfully."));
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return Task.FromResult(new WindowsExeLaunchResult(
                false,
                WindowsExeCompatibilityStatus.LaunchFailed,
                $"Wine launch failed: {exception.Message}"));
        }
    }

    private static WindowsExeLaunchResult? ValidateExecutable(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return new WindowsExeLaunchResult(
                false,
                WindowsExeCompatibilityStatus.InvalidExecutable,
                "An executable path is required.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(executablePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new WindowsExeLaunchResult(
                false,
                WindowsExeCompatibilityStatus.InvalidExecutable,
                $"The executable path is invalid: {exception.Message}");
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return new WindowsExeLaunchResult(
                false,
                WindowsExeCompatibilityStatus.InvalidExecutable,
                "Windows compatibility launch accepts only .exe files.");
        }

        if (!File.Exists(fullPath))
        {
            return new WindowsExeLaunchResult(
                false,
                WindowsExeCompatibilityStatus.InvalidExecutable,
                "The requested .exe file does not exist.");
        }

        return null;
    }

    private static string? LocateWineExecutable()
    {
        var configuredPath = Environment.GetEnvironmentVariable(WinePathEnvironmentVariable)?.Trim();
        if (!string.IsNullOrWhiteSpace(configuredPath) &&
            Path.IsPathFullyQualified(configuredPath) &&
            IsExecutableFile(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var candidateName in new[] { "wine", "wine64" })
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(directory, candidateName);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (IsExecutableFile(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        return null;
    }

    private static bool IsExecutableFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var mode = File.GetUnixFileMode(path);
            const UnixFileMode executeBits = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return (mode & executeBits) != 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
