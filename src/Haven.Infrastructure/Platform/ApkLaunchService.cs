using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>
/// Routes bounded APK launch requests to an explicitly registered, currently available host runtime.
/// This is an integration seam only: Haven does not embed or claim an Android OS/runtime here.
/// </summary>
public sealed class ApkLaunchService : IApkLaunchService
{
    private const string DefaultRuntimeId = "none";
    private const string DefaultRuntimeName = "Android APK runtime";
    private readonly IApkRuntimeProvider[] _runtimeProviders;

    public ApkLaunchService(IEnumerable<IApkRuntimeProvider>? runtimeProviders = null)
    {
        _runtimeProviders = runtimeProviders?.ToArray() ?? [];
    }

    public async Task<ApkRuntimeCapability> GetCapabilityAsync(CancellationToken cancellationToken)
    {
        string? firstUnavailableReason = null;

        foreach (var provider in _runtimeProviders)
        {
            var probe = await ProbeFailClosedAsync(provider, cancellationToken).ConfigureAwait(false);
            if (probe.IsAvailable)
                return probe;

            firstUnavailableReason ??= probe.UnavailableReason;
        }

        return new ApkRuntimeCapability(
            false,
            DefaultRuntimeId,
            DefaultRuntimeName,
            firstUnavailableReason ?? "No APK runtime provider is registered for this host.");
    }

    public async Task<ApkLaunchResult> LaunchAsync(ApkLaunchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var pathValidation = ValidateApkPath(request.ApkPath);
        if (pathValidation.Error is not null)
            return ApkLaunchResult.Invalid(pathValidation.Error);

        var canonicalRequest = new ApkLaunchRequest(pathValidation.CanonicalPath!);
        string? firstUnavailableReason = null;
        string? firstUnavailableRuntimeId = null;

        foreach (var provider in _runtimeProviders)
        {
            var capability = await ProbeFailClosedAsync(provider, cancellationToken).ConfigureAwait(false);
            if (!capability.IsAvailable)
            {
                firstUnavailableReason ??= capability.UnavailableReason;
                firstUnavailableRuntimeId ??= capability.RuntimeId;
                continue;
            }

            try
            {
                return await provider.LaunchAsync(canonicalRequest, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return ApkLaunchResult.Failed(
                    $"The APK runtime '{capability.DisplayName}' failed before a launch result was observed: {exception.GetType().Name}.",
                    capability.RuntimeId);
            }
        }

        return ApkLaunchResult.Unavailable(
            firstUnavailableReason ?? "No APK runtime provider is registered for this host.",
            firstUnavailableRuntimeId);
    }

    private static async Task<ApkRuntimeCapability> ProbeFailClosedAsync(
        IApkRuntimeProvider provider,
        CancellationToken cancellationToken)
    {
        try
        {
            var capability = await provider.ProbeAsync(cancellationToken).ConfigureAwait(false);
            if (capability is null)
            {
                return new ApkRuntimeCapability(
                    false,
                    provider.GetType().Name,
                    provider.GetType().Name,
                    "The APK runtime provider returned no capability result.");
            }

            if (capability.IsAvailable &&
                (string.IsNullOrWhiteSpace(capability.RuntimeId) || string.IsNullOrWhiteSpace(capability.DisplayName)))
            {
                return capability with
                {
                    IsAvailable = false,
                    UnavailableReason = "The APK runtime provider reported availability without a stable runtime identity."
                };
            }

            return capability;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var providerName = provider.GetType().Name;
            return new ApkRuntimeCapability(
                false,
                providerName,
                providerName,
                $"APK runtime capability probe failed closed: {exception.GetType().Name}.");
        }
    }

    private static (string? CanonicalPath, string? Error) ValidateApkPath(string apkPath)
    {
        if (string.IsNullOrWhiteSpace(apkPath))
            return (null, "An APK path is required.");

        if (!Path.IsPathFullyQualified(apkPath))
            return (null, "The APK path must be a fully qualified local path.");

        string canonicalPath;
        try
        {
            canonicalPath = Path.GetFullPath(apkPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return (null, "The APK path is not a valid local path.");
        }

        if (!string.Equals(Path.GetExtension(canonicalPath), ".apk", StringComparison.OrdinalIgnoreCase))
            return (null, "Only files with the .apk extension can be sent to the APK runtime seam.");

        if (!File.Exists(canonicalPath))
            return (null, "The APK file does not exist at the requested local path.");

        return (canonicalPath, null);
    }
}
