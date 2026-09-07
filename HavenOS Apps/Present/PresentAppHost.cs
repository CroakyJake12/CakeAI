using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Views.Pages.Present;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace HavenOS.Apps.Present;

/// <summary>
/// Standalone HavenOS host for the existing Present page and presentation engines.
/// </summary>
public sealed class PresentAppHost : IDisposable
{
    private readonly HavenEventBus _eventBus;
    private readonly IDisposable? _ownedServices;
    private bool _disposed;

    private PresentAppHost(
        PresentPage page,
        HavenEventBus eventBus,
        IDisposable? ownedServices)
    {
        Page = page;
        _eventBus = eventBus;
        _ownedServices = ownedServices;
    }

    public PresentPage Page { get; }

    public PresentDocument? Document => Page.Document;

    /// <summary>
    /// Creates the production surface with the same repository/import/export services as Haven Desktop.
    /// </summary>
    public static PresentAppHost CreateDefault()
    {
        var services = new ServiceCollection();
        services.AddHavenInfrastructure();
        ServiceProvider provider = services.BuildServiceProvider();

        try
        {
            return CreateCore(
                provider.GetRequiredService<IPresentRepository>(),
                provider.GetRequiredService<IPresentExportService>(),
                provider.GetRequiredService<IPresentImportService>(),
                provider);
        }
        catch
        {
            provider.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Creates the surface around supplied Present services, useful for isolated hosts and tests.
    /// </summary>
    public static PresentAppHost Create(
        IPresentRepository repository,
        IPresentExportService exporter,
        IPresentImportService? importer = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(exporter);
        return CreateCore(repository, exporter, importer, ownedServices: null);
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Page.InitializeAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Page.Dispose();
        _eventBus.Dispose();
        _ownedServices?.Dispose();
    }

    private static PresentAppHost CreateCore(
        IPresentRepository repository,
        IPresentExportService exporter,
        IPresentImportService? importer,
        IDisposable? ownedServices)
    {
        var eventBus = new HavenEventBus();
        try
        {
            var page = new PresentPage(eventBus, repository, exporter, importer);
            return new PresentAppHost(page, eventBus, ownedServices);
        }
        catch
        {
            eventBus.Dispose();
            throw;
        }
    }
}
