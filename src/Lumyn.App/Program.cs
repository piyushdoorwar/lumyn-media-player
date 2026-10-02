using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;

namespace Lumyn.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SESSION_MANAGER")))
            Environment.SetEnvironmentVariable("SESSION_MANAGER", "");

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // DM Sans ships in Assets/Fonts (SIL OFL 1.1). Non-Latin glyphs fall
            // back to system fonts, so file names in any script still render.
            .ConfigureFonts(fonts => fonts.AddFontCollection(
                new EmbeddedFontCollection(new Uri("fonts:Lumyn"), new Uri("avares://Lumyn/Assets/Fonts"))))
            .With(new FontManagerOptions { DefaultFamilyName = "fonts:Lumyn#DM Sans" })
            .LogToTrace()
            // Disable IBus IME — Ubuntu 26.04 IBus dropped several methods that
            // Avalonia still calls, causing cascading DBus errors in every dialog.
            .With(new X11PlatformOptions { EnableIme = false });
    }
}
