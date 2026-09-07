/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/WindowsCompatibility/WindowsCompatibilityServiceCollectionExtensions.cs.
 * What: Adds the Windows EXE compatibility service to a caller-controlled dependency-injection scope.
 * How: Registration is explicit and lazy; constructing the service does not probe Wine or start processes.
 * Why: Windows app compatibility must remain optional and non-boot-critical in HavenOS.
 */

using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure.WindowsCompatibility;

public static class WindowsCompatibilityServiceCollectionExtensions
{
    public static IServiceCollection AddHavenWindowsExeCompatibility(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IWindowsExeCompatibilityService, WineWindowsExeCompatibilityService>();
        return services;
    }
}
