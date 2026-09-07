using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace HavenOS.Apps.Present;

public sealed class PresentApplication : Application
{
    private PresentAppHost? _host;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _host = PresentAppHost.CreateDefault();

            var window = new Window
            {
                Title = "Present",
                Width = 1440,
                Height = 900,
                MinWidth = 1024,
                MinHeight = 640,
                Content = _host.Page
            };

            window.Closed += (_, _) =>
            {
                _host?.Dispose();
                _host = null;
            };

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
