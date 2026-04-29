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

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(playback, settings)
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
