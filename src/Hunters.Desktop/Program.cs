using Avalonia;
using Avalonia.Media;
using Microsoft.Extensions.Hosting;

namespace NewEdenSoft.Hunters.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        using var host = DesktopHost.Create(args);
        host.Start();
        try
        {
            BuildAvaloniaApp(() => new App(host.Services))
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            host.StopAsync().GetAwaiter().GetResult();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(() => new App());

    private static AppBuilder BuildAvaloniaApp(Func<App> createApp)
        => AppBuilder.Configure(createApp)
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .With(new FontManagerOptions
            {
                DefaultFamilyName = "avares://NewEdenSoft.Hunters.Desktop/Assets/Fonts#Saira",
            })
            .LogToTrace();
}
