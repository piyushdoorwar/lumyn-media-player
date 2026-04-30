using System.ComponentModel;
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
    private long _lastControlsPulseMs;

    public MainWindow()
    {
        InitializeComponent();

        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _positionTimer.Tick += (_, _) => ViewModel?.QueueRefreshState();
        _positionTimer.Start();

        _hideControlsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _hideControlsTimer.Tick += (_, _) => HideControls();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);

        PointerMoved += (_, _) => ShowControls();
        Opened += (_, _) => Focus();
        Closing += (_, _) => ViewModel?.SaveResumePosition();
        Closed += (_, _) => ViewModel?.Dispose();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    // ── Controls visibility ──────────────────────────────────────────────────

    private void ShowControls()
    {
        if (ViewModel is null) return;

        var now = Environment.TickCount64;
        if (ViewModel.ControlsVisible && now - _lastControlsPulseMs < 250)
            return;

        _lastControlsPulseMs = now;
        if (!ViewModel.ControlsVisible)
        {
            ViewModel.ControlsVisible = true;
            Cursor = Cursor.Default;
        }

        if (!_hideControlsTimer.IsEnabled || ViewModel.IsPlaying || WindowState == WindowState.FullScreen)
        {
            _hideControlsTimer.Stop();
            _hideControlsTimer.Start();
        }
    }

    private void HideControls()
    {
        _hideControlsTimer.Stop();
        if (ViewModel is null) return;
        if (ViewModel.IsPlaying || WindowState == WindowState.FullScreen)
        {
            if (!string.IsNullOrWhiteSpace(ViewModel.CurrentFilePath))
            {
                ViewModel.ControlsVisible = false;
                Cursor = new Cursor(StandardCursorType.None);
            }
        }
    }

    // ── Video click surface ──────────────────────────────────────────────────

    private void VideoClickLayer_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed) return; // right-click → context menu handles it

        e.Handled = true;
        if (e.ClickCount >= 2)
        {
            ToggleFullscreen();
        }
        else
        {
            if (ViewModel?.ControlsVisible == false)
                ShowControls();
            else
                ViewModel?.TogglePlayPauseCommand.Execute(null);
        }
    }

    // ── Seek slider ──────────────────────────────────────────────────────────

    private void SeekSlider_OnPointerPressed(object? sender, PointerPressedEventArgs e)
        => ViewModel?.BeginSeek();

    // PointerReleased is not fired reliably on Slider (Avalonia routing swallows it).
    // PointerCaptureLost fires when the user releases the thumb after a drag, or
    // clicks anywhere else — both are valid "seek done" signals.
    private void SeekSlider_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        => ViewModel?.EndSeek();

    // ── Top-bar buttons ──────────────────────────────────────────────────────

    private async void OpenButton_OnClick(object? sender, RoutedEventArgs e)
        => await OpenFromPickerAsync();

    private void FullscreenButton_OnClick(object? sender, RoutedEventArgs e)
        => ToggleFullscreen();

    // ── Keyboard shortcuts ───────────────────────────────────────────────────

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is null) return;
        var ctrl  = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var alt   = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        switch (e.Key)
        {
            // ── Playback ────────────────────────────────────────────────────
            case Key.Space:
                ViewModel.TogglePlayPauseCommand.Execute(null);
                ShowControls();
                e.Handled = true; break;

            case Key.Left when ctrl:
                ViewModel.SeekBackward30Command.Execute(null);
                ShowControls(); e.Handled = true; break;
            case Key.Left:
                ViewModel.SeekBackwardCommand.Execute(null);
                ShowControls(); e.Handled = true; break;

            case Key.Right when ctrl:
                ViewModel.SeekForward30Command.Execute(null);
                ShowControls(); e.Handled = true; break;
            case Key.Right:
                ViewModel.SeekForwardCommand.Execute(null);
                ShowControls(); e.Handled = true; break;

            case Key.OemPeriod: // . → frame step
                ViewModel.StepFrameCommand.Execute(null);
                ShowControls(); e.Handled = true; break;

            // ── Volume ──────────────────────────────────────────────────────
            case Key.Up:
                ViewModel.VolumeUpCommand.Execute(null);
                ShowControls(); e.Handled = true; break;
            case Key.Down:
                ViewModel.VolumeDownCommand.Execute(null);
                ShowControls(); e.Handled = true; break;
            case Key.M:
                ViewModel.ToggleMuteCommand.Execute(null);
                ShowControls(); e.Handled = true; break;

            // ── Speed ───────────────────────────────────────────────────────
            case Key.OemOpenBrackets: // [
                ViewModel.SpeedDownCommand.Execute(null);
                ShowControls(); e.Handled = true; break;
            case Key.OemCloseBrackets: // ]
                ViewModel.SpeedUpCommand.Execute(null);
                ShowControls(); e.Handled = true; break;
            case Key.OemBackslash: // \ → reset speed
                ViewModel.ResetSpeedCommand.Execute(null);
                ShowControls(); e.Handled = true; break;

            // ── Loop ────────────────────────────────────────────────────────
            case Key.L:
                ViewModel.ToggleLoopCommand.Execute(null);
                e.Handled = true; break;

            // ── Tracks ──────────────────────────────────────────────────────
            case Key.A:
                ViewModel.CycleAudioTrackCommand.Execute(null);
                e.Handled = true; break;
            case Key.V:
                ViewModel.CycleSubtitleTrackCommand.Execute(null);
                e.Handled = true; break;
            case Key.S when !ctrl:
                await OpenSubtitleFileAsync();
                e.Handled = true; break;

            // ── Window ──────────────────────────────────────────────────────
            case Key.F:
                ToggleFullscreen();
                e.Handled = true; break;
            case Key.T:
                ViewModel.ToggleAlwaysOnTopCommand.Execute(null);
                e.Handled = true; break;
            case Key.Escape when WindowState == WindowState.FullScreen:
                WindowState = WindowState.Normal;
                ShowControls(); e.Handled = true; break;

            // ── Dialogs / file ops ───────────────────────────────────────────
            case Key.O:
                await OpenFromPickerAsync();
                e.Handled = true; break;
            case Key.G when ctrl:
                await OpenJumpToTimeDialogAsync();
                e.Handled = true; break;
            case Key.I when alt:
                ViewModel.TakeScreenshotCommand.Execute(null);
                e.Handled = true; break;
        }
    }

    // ── Context menu ─────────────────────────────────────────────────────────

    private void RootContextMenu_Opening(object? sender, CancelEventArgs e)
    {
        if (ViewModel is null || sender is not ContextMenu cm) return;

        // Recent files sub-menu
        var recentMenuItem = cm.Items.OfType<MenuItem>().FirstOrDefault(m => m.Name == "RecentFilesMenuItem");
        if (recentMenuItem is not null)
        {
            recentMenuItem.Items.Clear();
            var recent = ViewModel.RecentFiles;
            recentMenuItem.IsEnabled = recent.Count > 0;
            foreach (var path in recent)
            {
                var p = path;
                var mi = new MenuItem { Header = Path.GetFileName(p) };
                Avalonia.Controls.ToolTip.SetTip(mi, p);
                mi.Click += async (_, _) =>
                {
                    await ViewModel.OpenFileAsync(p);
                    Focus();
                    ShowControls();
                };
                recentMenuItem.Items.Add(mi);
            }
        }

        // Audio tracks sub-menu
        var audioMenuItem = cm.Items.OfType<MenuItem>().FirstOrDefault(m => m.Name == "AudioTrackMenuItem");
        if (audioMenuItem is not null)
        {
            audioMenuItem.Items.Clear();
            var tracks = ViewModel.AudioTracks;
            audioMenuItem.IsEnabled = tracks.Length > 0;
            foreach (var track in tracks)
            {
                var t = track;
                audioMenuItem.Items.Add(new MenuItem { Header = t.IsSelected ? $"✓ {t.Name}" : t.Name }
                    .Also(mi => mi.Click += (_, _) => ViewModel.SetAudioTrackCommand.Execute(t.Id)));
            }
        }

        // Subtitle tracks sub-menu
        var subMenuItem = cm.Items.OfType<MenuItem>().FirstOrDefault(m => m.Name == "SubtitleTrackMenuItem");
        if (subMenuItem is not null)
        {
            subMenuItem.Items.Clear();
            var tracks = ViewModel.SubtitleTracks;
            subMenuItem.IsEnabled = tracks.Length > 0;
            foreach (var track in tracks)
            {
                var t = track;
                subMenuItem.Items.Add(new MenuItem { Header = t.IsSelected ? $"✓ {t.Name}" : t.Name }
                    .Also(mi => mi.Click += (_, _) => ViewModel.SetSubtitleTrackCommand.Execute(t.Id)));
            }
        }

        // Always-on-top checkmark
        var aotItem = cm.Items.OfType<MenuItem>().FirstOrDefault(m => m.Name == "AlwaysOnTopMenuItem");
        if (aotItem is not null)
            aotItem.Header = ViewModel.IsAlwaysOnTop ? "✓ Always on Top" : "Always on Top";
    }

    private async void JumpToTime_Click(object? sender, RoutedEventArgs e)
        => await OpenJumpToTimeDialogAsync();

    private async void LoadSubtitle_Click(object? sender, RoutedEventArgs e)
        => await OpenSubtitleFileAsync();

    private async void LoadSubtitleButton_OnClick(object? sender, RoutedEventArgs e)
        => await OpenSubtitleSettingsDialogAsync();

    private void Speed_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi &&
            mi.Tag is string tag &&
            float.TryParse(tag, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var rate))
            ViewModel?.SetSpeedCommand.Execute(rate);
    }

    private void AlwaysOnTop_Click(object? sender, RoutedEventArgs e)
        => ViewModel?.ToggleAlwaysOnTopCommand.Execute(null);

    // ── File open helpers ────────────────────────────────────────────────────

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
                    Patterns = ["*.mp4","*.mkv","*.avi","*.mov","*.webm",
                                "*.mp3","*.flac","*.wav","*.ogg","*.m4a","*.aac","*.opus"]
                },
                FilePickerFileTypes.All
            ]
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path) && ViewModel is not null)
        {
            await ViewModel.OpenFileAsync(path);
            Focus();
            ShowControls();
        }
    }

    private async Task OpenSubtitleFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load subtitle",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Subtitle files")
                {
                    Patterns = ["*.srt","*.ass","*.ssa","*.vtt","*.sub"]
                },
                FilePickerFileTypes.All
            ]
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path) && ViewModel is not null)
        {
            await ViewModel.LoadSubtitleFileAsync(path);
            Focus();
        }
    }

    private async Task OpenSubtitleSettingsDialogAsync()
    {
        if (ViewModel is null) return;
        var dialog = new SubtitleSettingsDialog(ViewModel.CurrentSubtitleSettings, ViewModel.CurrentFilePath);
        var result = await dialog.ShowDialog<Lumyn.App.Models.SubtitleSettings?>(this);
        if (result is not null)
        {
            await ViewModel.ApplySubtitleSettingsAsync(result);
            Focus();
        }
    }

    private async Task OpenJumpToTimeDialogAsync()
    {
        if (ViewModel is null || !ViewModel.HasMedia) return;
        var dialog = new JumpToTimeDialog();
        var result = await dialog.ShowDialog<TimeSpan?>(this);
        if (result.HasValue)
        {
            ViewModel.JumpTo(result.Value);
            Focus();
        }
    }

    // ── Drag-and-drop ────────────────────────────────────────────────────────

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
            // Detect subtitle by extension; load instead of open
            if (IsSubtitleFile(path) && ViewModel.HasMedia)
                await ViewModel.LoadSubtitleFileAsync(path);
            else
                await ViewModel.OpenFileAsync(path);
            Focus();
            ShowControls();
        }
    }

    private static bool IsSubtitleFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".srt" or ".ass" or ".ssa" or ".vtt" or ".sub";
    }

    // ── Fullscreen ───────────────────────────────────────────────────────────

    private void ToggleFullscreen()
    {
        WindowState = WindowState == WindowState.FullScreen
            ? WindowState.Normal
            : WindowState.FullScreen;

        var icon = this.FindControl<PathIcon>("FullscreenIcon");
        var iconCtrl = this.FindControl<PathIcon>("FullscreenCtrlIcon");
        var data = this.FindResource(
            WindowState == WindowState.FullScreen ? "Icon.FullscreenExit" : "Icon.Fullscreen");
        if (data is Avalonia.Media.StreamGeometry sg)
        {
            if (icon is not null) icon.Data = sg;
            if (iconCtrl is not null) iconCtrl.Data = sg;
        }
        ShowControls();
    }
}

// Minimal fluent helper so we can call .Also() for inline event subscription
internal static class ObjectExtensions
{
    internal static T Also<T>(this T obj, Action<T> action) { action(obj); return obj; }
}
