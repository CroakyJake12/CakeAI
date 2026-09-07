using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

[assembly: AvaloniaTestApplication(typeof(HavenOS.Apps.Present.Tests.TestAppBuilder))]

namespace HavenOS.Apps.Present.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<PresentApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
