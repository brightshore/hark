using Avalonia;
using Hark;
using Hark.Avalonia;

namespace Hark.Sample;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseNotifications(new AppIdentity("dev.brightshore.HarkSample", "Hark Sample"))
            .LogToTrace();
}
