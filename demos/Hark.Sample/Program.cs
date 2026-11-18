using Avalonia;
using Hark;
using Hark.Avalonia;

namespace Hark.Sample;

internal static class Program
{
    private static readonly AppIdentity Identity = new("dev.brightshore.HarkSample", "Hark Sample");

    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseNotifications(Identity)
            .UseBadge(Identity)
            .LogToTrace();
}
