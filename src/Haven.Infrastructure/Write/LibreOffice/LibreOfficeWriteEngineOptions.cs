namespace Haven.Infrastructure;

/// <summary>
/// Configuration for the Linux LibreOfficeKit-backed Haven Write helper.
/// The helper is intentionally external to the HUI process for crash and ABI isolation.
/// </summary>
public sealed record LibreOfficeWriteEngineOptions
{
    public bool Enabled { get; init; }
    public string HelperExecutablePath { get; init; } = "/usr/libexec/haven/haven-write-engine";
    public string LibreOfficeInstallPath { get; init; } = "/usr/lib/libreoffice";
    public string ProfileRootDirectory { get; init; } = string.Empty;
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public long MaximumTileBytes { get; init; } = 64L * 1024L * 1024L;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(HelperExecutablePath))
            errors.Add("A Haven Write helper executable path is required.");
        else if (!IsAbsoluteTargetPath(HelperExecutablePath))
            errors.Add("The Haven Write helper executable path must be absolute.");

        if (string.IsNullOrWhiteSpace(LibreOfficeInstallPath))
            errors.Add("A LibreOffice installation path is required.");
        else if (!IsAbsoluteTargetPath(LibreOfficeInstallPath))
            errors.Add("The LibreOffice installation path must be absolute.");

        if (!string.IsNullOrWhiteSpace(ProfileRootDirectory)
            && !IsAbsoluteTargetPath(ProfileRootDirectory))
        {
            errors.Add("The LibreOffice profile root must be absolute when configured.");
        }

        if (StartupTimeout <= TimeSpan.Zero || StartupTimeout > TimeSpan.FromMinutes(2))
            errors.Add("The Haven Write helper startup timeout must be greater than zero and no more than two minutes.");

        if (MaximumTileBytes is < 1L or > 256L * 1024L * 1024L)
            errors.Add("The Haven Write maximum tile size must be between 1 byte and 256 MiB.");

        return errors;
    }

    private static bool IsAbsoluteTargetPath(string value) =>
        value.StartsWith('/', StringComparison.Ordinal) || Path.IsPathFullyQualified(value);
}
