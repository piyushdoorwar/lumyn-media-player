using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Lumyn.Core.Services;
using Lumyn.App.ViewModels;
using Lumyn.App.Views;

namespace Lumyn.App;

public sealed partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var playback = new PlaybackService();
            var settings = new SettingsService();
            var casting = new ChromecastCastService();
            var vm = new MainViewModel(playback, settings, casting);

            var window = new MainWindow { DataContext = vm };
            desktop.MainWindow = window;

            // Handle command-line file/URI argument: lumyn path/to/file.mp4 or lumyn file:///path/to/file.mp4
            var filePath = TryGetStartupFilePath(desktop.Args);
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                desktop.MainWindow.Opened += async (_, _) =>
                    await window.OpenFileWhenReadyAsync(filePath);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static string? TryGetStartupFilePath(string[]? args)
    {
        if (args is null) return null;

        foreach (var arg in args)
        {
            if (string.IsNullOrWhiteSpace(arg) || arg.StartsWith('-'))
                continue;

            var path = arg;
            if (Uri.TryCreate(arg, UriKind.Absolute, out var uri) && uri.IsFile)
                path = uri.LocalPath;

            if (File.Exists(path))
                return path;
        }

        return null;
    }
}
