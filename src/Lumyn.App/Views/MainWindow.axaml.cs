using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Lumyn.App.ViewModels;

namespace Lumyn.App.Views;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _positionTimer;
    private readonly DispatcherTimer _hideControlsTimer;

    public MainWindow()
    {
        InitializeComponent();

        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _positionTimer.Tick += (_, _) => ViewModel?.RefreshState();
        _positionTimer.Start();

        _hideControlsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _hideControlsTimer.Tick += (_, _) =>
        {
            _hideControlsTimer.Stop();
            if (ViewModel is { IsPlaying: true } || WindowState == WindowState.FullScreen)
            {
                if (!string.IsNullOrWhiteSpace(ViewModel?.CurrentFilePath))
                {
                    ViewModel!.ControlsVisible = false;
                    Cursor = new Cursor(StandardCursorType.None);
                }
            }
        };

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        PointerMoved += (_, _) => ShowControlsTemporarily();
        Closing += (_, _) => ViewModel?.SaveResumePosition();
        Closed += (_, _) => ViewModel?.Dispose();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private async void OpenButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await OpenFromPickerAsync();
    }

    private void FullscreenButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ToggleFullscreen();
    }

    private void SeekSlider_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.BeginSeek();
    }

    private void SeekSlider_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        ViewModel?.EndSeek();
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (ViewModel is null)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Space:
                ViewModel.TogglePlayPauseCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Left:
                ViewModel.SeekBackwardCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Right:
                ViewModel.SeekForwardCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Up:
                ViewModel.VolumeUpCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Down:
                ViewModel.VolumeDownCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.F:
                ToggleFullscreen();
                e.Handled = true;
                break;
            case Key.M:
                ViewModel.ToggleMuteCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.O:
                await OpenFromPickerAsync();
                e.Handled = true;
                break;
            case Key.Escape when WindowState == WindowState.FullScreen:
                WindowState = WindowState.Normal;
                ShowControlsTemporarily();
                e.Handled = true;
                break;
        }
    }

    private async Task OpenFromPickerAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open media",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Media files")
                {
                    Patterns = ["*.mp4", "*.mkv", "*.avi", "*.mov", "*.webm", "*.mp3", "*.flac", "*.wav", "*.ogg"]
                },
                FilePickerFileTypes.All
            ]
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path) && ViewModel is not null)
        {
            await ViewModel.OpenFileAsync(path);
            ShowControlsTemporarily();
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        var path = files?.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path) && ViewModel is not null)
        {
            await ViewModel.OpenFileAsync(path);
            ShowControlsTemporarily();
        }
    }

    private void ToggleFullscreen()
    {
        WindowState = WindowState == WindowState.FullScreen
            ? WindowState.Normal
            : WindowState.FullScreen;
        ShowControlsTemporarily();
    }

    private void ShowControlsTemporarily()
    {
        if (ViewModel is null)
        {
            return;
        }

        ViewModel.ControlsVisible = true;
        Cursor = new Cursor(StandardCursorType.Arrow);
        _hideControlsTimer.Stop();
        _hideControlsTimer.Start();
    }
}
