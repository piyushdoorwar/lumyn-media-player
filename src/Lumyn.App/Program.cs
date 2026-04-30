using Avalonia;

namespace Lumyn.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Suppress harmless ICELib "SESSION_MANAGER not defined" message on X11
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SESSION_MANAGER")))
            Environment.SetEnvironmentVariable("SESSION_MANAGER", "");

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
