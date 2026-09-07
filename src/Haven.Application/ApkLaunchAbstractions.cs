namespace Haven.Application;

/// <summary>Stable string outcomes for the optional APK launch integration seam.</summary>
public static class ApkLaunchStatus
{
    public const string InvalidRequest = "invalid_request";
    public const string RuntimeUnavailable = "runtime_unavailable";
    public const string LaunchFailed = "launch_failed";
    public const string Launched = "launched";
}

/// <summary>Describes whether one optional host APK runtime can currently accept launches.</summary>
public sealed record ApkRuntimeCapability(
    bool IsAvailable,
    string RuntimeId,
    string DisplayName,
    string? UnavailableReason = null);

/// <summary>A bounded request to open one existing local Android package file.</summary>
public sealed record ApkLaunchRequest(string ApkPath);

/// <summary>Observed result returned by the APK launch seam.</summary>
public sealed record ApkLaunchResult(
    bool Succeeded,
    string Status,
    string Message,
    string? RuntimeId = null)
{
    public static ApkLaunchResult Invalid(string message) =>
        new(false, ApkLaunchStatus.InvalidRequest, message);

    public static ApkLaunchResult Unavailable(string message, string? runtimeId = null) =>
        new(false, ApkLaunchStatus.RuntimeUnavailable, message, runtimeId);

    public static ApkLaunchResult Failed(string message, string? runtimeId = null) =>
        new(false, ApkLaunchStatus.LaunchFailed, message, runtimeId);

    public static ApkLaunchResult Success(string message, string runtimeId) =>
        new(true, ApkLaunchStatus.Launched, message, runtimeId);
}

/// <summary>
/// Optional host/runtime adapter for APK execution. Implementations may use an Android compatibility
/// runtime, emulator, container, or device bridge, but must not expose arbitrary command execution.
/// </summary>
public interface IApkRuntimeProvider
{
    Task<ApkRuntimeCapability> ProbeAsync(CancellationToken cancellationToken);
    Task<ApkLaunchResult> LaunchAsync(ApkLaunchRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Capability-gated APK launcher. The service is fail-closed when no verified runtime provider is available.
/// Callers remain responsible for the normal Haven approval path before requesting an external launch.
/// </summary>
public interface IApkLaunchService
{
    Task<ApkRuntimeCapability> GetCapabilityAsync(CancellationToken cancellationToken);
    Task<ApkLaunchResult> LaunchAsync(ApkLaunchRequest request, CancellationToken cancellationToken);
}
