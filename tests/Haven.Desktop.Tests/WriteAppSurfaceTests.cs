using Haven.Application;
using Haven.Core;
using Haven.Desktop.Apps.Write;
using Haven.Desktop.Views.Pages.Write;
using Haven.Desktop.Views.Shell;

namespace Haven.Desktop.Tests;

public sealed class WriteAppSurfaceTests
{
    [Fact]
    public void Standalone_surface_preserves_the_real_write_editor()
    {
        Assert.Equal("Write", WriteAppSurface.DisplayName);
        Assert.Same(typeof(WritePage), WriteAppSurface.EditorPageType);
    }

    [Fact]
    public void Standalone_surface_preserves_the_existing_write_route()
    {
        var app = Assert.Single(BuiltInModeSeed.Modes, item => item.Key == WriteAppSurface.AppKey);

        var route = HavenAppRoutePolicy.Resolve(app);

        Assert.Equal(HavenAppRouteKind.Write, route.Kind);
        Assert.Equal(HavenSurface.Write, route.Surface);
    }
}
