namespace Haven.Infrastructure;

/// <summary>
/// Performs a non-mutating readiness check for the LibreOfficeKit-backed Haven Write engine.
/// This is not runtime proof: it only verifies platform, configuration, and required paths.
/// </summary>
public static class LibreOfficeWriteEngineAvailability
{
    public static LibreOfficeWriteEngineAvailabilityResult Inspect(LibreOfficeWriteEngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var issues = new List<string>(options.Validate());
        if (!OperatingSystem.IsLinux())
            issues.Add("The LibreOffice Write engine is currently supported only by the Linux HavenOS host.");

        if (issues.Count == 0 && !File.Exists(options.HelperExecutablePath))
            issues.Add($"The Haven Write helper was not found at '{options.HelperExecutablePath}'.");

        if (issues.Count == 0 && !Directory.Exists(options.LibreOfficeProgramPath))
            issues.Add($"The LibreOffice program directory was not found at '{options.LibreOfficeProgramPath}'.");

        var sofficeApp = Path.Combine(options.LibreOfficeProgramPath, "libsofficeapp.so");
        var merged = Path.Combine(options.LibreOfficeProgramPath, "libmergedlo.so");
        if (issues.Count == 0 && !File.Exists(sofficeApp) && !File.Exists(merged))
        {
            issues.Add("LibreOfficeKit could not find libsofficeapp.so or libmergedlo.so in the configured LibreOffice program directory.");
        }

        return new LibreOfficeWriteEngineAvailabilityResult(
            options.Enabled && issues.Count == 0,
            options.Enabled,
            OperatingSystem.IsLinux(),
            issues);
    }
}

public sealed record LibreOfficeWriteEngineAvailabilityResult(
    bool Ready,
    bool Enabled,
    bool LinuxHost,
    IReadOnlyList<string> Issues);
