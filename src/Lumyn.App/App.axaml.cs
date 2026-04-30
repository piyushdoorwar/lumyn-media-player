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
            var vm = new MainViewModel(playback, settings);

            desktop.MainWindow = new MainWindow { DataContext = vm };

            // Handle command-line file argument: lumyn path/to/file.mp4
            var args = desktop.Args;
            if (args?.Length > 0)
            {
                var filePath = args[0];
                if (File.Exists(filePath))
                {
                    desktop.MainWindow.Opened += async (_, _) =>
                        await vm.OpenFileAsync(filePath);
                }
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
