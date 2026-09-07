using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class LibreOfficeWriteEngineOptionsTests
{
    [Fact]
    public void Validate_accepts_safe_default_paths_and_bounds()
    {
        var options = new LibreOfficeWriteEngineOptions();

        Assert.Empty(options.Validate());
        Assert.Equal("/usr/lib/libreoffice/program", options.LibreOfficeProgramPath);
    }

    [Fact]
    public void Validate_rejects_relative_paths_and_unsafe_resource_bounds()
    {
        var options = new LibreOfficeWriteEngineOptions
        {
            HelperExecutablePath = "bin/haven-write-engine",
            LibreOfficeProgramPath = "lib/libreoffice/program",
            ProfileRootDirectory = "profiles",
            StartupTimeout = TimeSpan.Zero,
            MaximumTileBytes = 300L * 1024L * 1024L
        };

        var errors = options.Validate();

        Assert.Contains(errors, value => value.Contains("helper executable path must be absolute", StringComparison.Ordinal));
        Assert.Contains(errors, value => value.Contains("LibreOffice program path must be absolute", StringComparison.Ordinal));
        Assert.Contains(errors, value => value.Contains("profile root must be absolute", StringComparison.Ordinal));
        Assert.Contains(errors, value => value.Contains("startup timeout", StringComparison.Ordinal));
        Assert.Contains(errors, value => value.Contains("maximum tile size", StringComparison.Ordinal));
    }

    [Fact]
    public void Availability_never_reports_ready_when_engine_is_disabled()
    {
        var result = LibreOfficeWriteEngineAvailability.Inspect(new LibreOfficeWriteEngineOptions
        {
            Enabled = false
        });

        Assert.False(result.Ready);
        Assert.False(result.Enabled);
    }

    [Fact]
    public void Availability_fails_closed_on_non_linux_hosts()
    {
        if (OperatingSystem.IsLinux())
            return;

        var result = LibreOfficeWriteEngineAvailability.Inspect(new LibreOfficeWriteEngineOptions
        {
            Enabled = true
        });

        Assert.False(result.Ready);
        Assert.False(result.LinuxHost);
        Assert.Contains(result.Issues, value => value.Contains("supported only", StringComparison.Ordinal));
    }
}
