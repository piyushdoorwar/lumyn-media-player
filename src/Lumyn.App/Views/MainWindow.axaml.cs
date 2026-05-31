using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lumyn.App.ViewModels;

namespace Lumyn.App.Views;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _positionTimer;
    private readonly DispatcherTimer _hideControlsTimer;
    private readonly DispatcherTimer _glowTimer;
    private readonly DispatcherTimer _glowLerpTimer;
    private readonly SolidColorBrush _glowBrush = new(Colors.Transparent);
    private Color _targetGlowColor = GlowOff;
    private bool _glowSampling;

    // Seek thumbnail preview — keyed by coarse progress bucket (0–1999).
    // Each entry also stores the source JPEG reference so Phase 2 upgrades
    // are detected automatically: if GetNearest returns a different byte[]
    // the cached Bitmap is disposed and the new frame decoded in its place.
    private readonly Dictionary<int, (byte[] Src, Avalonia.Media.Imaging.Bitmap Bmp)> _thumbBitmapCache = new();
    private string? _thumbCacheForFile;

    private bool _isApplyingFullscreenState;
    private WindowState _restoreWindowStateAfterFullscreen = WindowState.Normal;

    // Last known "normal" (non-maximized, non-fullscreen) bounds — what we persist
    // so reopening restores the size/place the user actually chose.
    private double _normalWidth;
    private double _normalHeight;
    private PixelPoint _normalPosition;

    private static readonly Color GlowOff = Color.FromArgb(0, 0, 0, 0);

    public MainWindow()
    {
        InitializeComponent();

        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _positionTimer.Tick += (_, _) => ViewModel?.QueueRefreshState();
        _positionTimer.Start();

        _hideControlsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _hideControlsTimer.Tick += (_, _) => HideControls();

        _glowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _glowTimer.Tick += GlowTimer_Tick;
        _glowTimer.Start();

        _glowLerpTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _glowLerpTimer.Tick += GlowLerpTimer_Tick;
        _glowLerpTimer.Start();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);

        PointerMoved += (_, _) => ShowControls();
        PointerWheelChanged += OnWindowPointerWheelChanged;
        Opened += (_, _) =>
        {
            RestoreWindowGeometry();
            Focus();
            var rb = this.FindControl<Border>("RootBorder");
            if (rb is not null) rb.BorderBrush = _glowBrush;
            // Wire playlist reorder DragDrop after visual tree is ready.
            var pic = this.FindControl<ItemsControl>("PlaylistItemsControl");
            if (pic is not null)
            {
                pic.AddHandler(DragDrop.DragOverEvent, PlaylistDragOver);
                pic.AddHandler(DragDrop.DropEvent, PlaylistDrop);
            }

            var seekSlider = this.FindControl<Controls.SeekBar>("SeekSlider");
            if (seekSlider is not null)
            {
                seekSlider.PointerMoved  += (_, e) => UpdateSeekThumbnail(e);
                seekSlider.PointerExited += (_, _) => HideSeekThumbnailPopup();
            }

            var audioPanel = this.FindControl<Grid>("AudioModePanel");
            if (audioPanel is not null)
                audioPanel.PointerPressed += AudioPanel_OnPointerPressed;
        };
        PositionChanged += (_, _) =>
        {
            if (WindowState == WindowState.Normal)
                _normalPosition = Position;
        };
        Closing += (_, _) =>
        {
            PersistWindowGeometry();
            ViewModel?.SaveResumePosition();
        };
        Closed += (_, _) =>
        {
            _glowTimer.Stop();
            _glowLerpTimer.Stop();
            foreach (var (_, bmp) in _thumbBitmapCache.Values) bmp.Dispose();
            _thumbBitmapCache.Clear();
            ViewModel?.Dispose();
        };
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
            {
                UpdateMaximizeIcon();
                UpdateTopBarVisibility();
            }
            else if (e.Property == BoundsProperty && WindowState == WindowState.Normal)
            {
                if (!double.IsNaN(Width))  _normalWidth  = Width;
                if (!double.IsNaN(Height)) _normalHeight = Height;
            }
        };
        DataContextChanged += (_, _) =>
        {
            UpdateTopBarVisibility();
            if (ViewModel is not null)
                ViewModel.CopyImageToClipboard = CopyImageToClipboardAsync;
        };
    }

    private async Task<bool> CopyImageToClipboardAsync(string imagePath)
    {
        try
        {
            var clipboard = Clipboard;
            if (clipboard is null) return false;

            // The clipboard renders its contents lazily when a consumer pastes,
            // so anything we hand it must stay valid (no `using` dispose) and
            // must not require a fragile encode step on the dispatcher thread.
            if (OperatingSystem.IsWindows())
            {
                // Windows: a Bitmap lands as a native CF_DIB image that Paint,
                // Office, browsers, etc. can paste directly.
                var bitmap = new Avalonia.Media.Imaging.Bitmap(imagePath);
                await clipboard.SetBitmapAsync(bitmap);
            }
            else
            {
                // X11/Wayland: offer the raw PNG bytes under the image/png target.
                // Avoids Avalonia's X11 Bitmap.Save path, which NRE-crashes the
                // app on the event thread when another app requests the image.
                var bytes = await File.ReadAllBytesAsync(imagePath);
                var format = DataFormat.CreateBytesPlatformFormat("image/png");
                await clipboard.SetValueAsync(format, bytes);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    public async Task OpenFileWhenReadyAsync(string filePath)
    {
        await WaitForVideoSurfaceAsync();
        if (ViewModel is null) return;

        await ViewModel.OpenFileAsync(filePath);
        Focus();
        ShowControls();
    }

    // ── Window geometry persistence ──────────────────────────────────────────

    private void RestoreWindowGeometry()
    {
        var geo = ViewModel?.GetWindowGeometry();
        if (geo is null)
        {
            _normalWidth    = Width;
            _normalHeight   = Height;
            _normalPosition = Position;
            return;
        }

        var w = Math.Max(geo.Width, MinWidth);
        var h = Math.Max(geo.Height, MinHeight);
        Width  = w;
        Height = h;
        _normalWidth  = w;
        _normalHeight = h;

        var pos = new PixelPoint(geo.X, geo.Y);
        if (IsPositionOnScreen(pos))
        {
            Position        = pos;
            _normalPosition = pos;
        }
        else
        {
            _normalPosition = Position;
        }

        // Restore Maximized only; never reopen straight into FullScreen.
        if (geo.Maximized)
            WindowState = WindowState.Maximized;
    }

    private void PersistWindowGeometry()
    {
        // Don't persist transient fullscreen bounds — keep the last normal geometry.
        if (WindowState == WindowState.FullScreen) return;

        var maximized = WindowState == WindowState.Maximized;
        var w = _normalWidth  > 0 ? _normalWidth  : Width;
        var h = _normalHeight > 0 ? _normalHeight : Height;
        if (double.IsNaN(w) || double.IsNaN(h)) return;

        ViewModel?.SaveWindowGeometry(w, h, _normalPosition.X, _normalPosition.Y, maximized);
    }

    private bool IsPositionOnScreen(PixelPoint pos)
    {
        var screens = Screens;
        if (screens is null) return true;
        foreach (var screen in screens.All)
            if (screen.Bounds.Contains(pos)) return true;
        return false;
    }

    // ── Controls visibility ──────────────────────────────────────────────────

    private void ShowControls()
    {
        if (ViewModel is null) return;

        if (!ViewModel.ControlsVisible)
        {
            ViewModel.ControlsVisible = true;
            Cursor = Cursor.Default;
            UpdateTopBarVisibility();
        }

        _hideControlsTimer.Stop();
        _hideControlsTimer.Start();
    }

    private void HideControls()
    {
        _hideControlsTimer.Stop();
        if (ViewModel is null) return;
        // In audio-only mode the controls stay visible at all times.
        if (ViewModel.IsAudioOnly) return;
        if (ViewModel.IsPlaying || WindowState == WindowState.FullScreen)
        {
            if (!string.IsNullOrWhiteSpace(ViewModel.CurrentFilePath))
            {
                ViewModel.ControlsVisible = false;
                Cursor = new Cursor(StandardCursorType.None);
            }
        }
        UpdateTopBarVisibility();
    }

    private void DurationText_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        ViewModel?.ToggleDurationDisplay();
        e.Handled = true;
    }

    private void UpdateTopBarVisibility()
    {
        var topBar = this.FindControl<Border>("TopBar");
        if (topBar is null) return;

        topBar.IsVisible = WindowState != WindowState.FullScreen;
    }

    // ── Video click surface ──────────────────────────────────────────────────

    private void AudioPanel_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        ViewModel?.TogglePlayPauseCommand.Execute(null);
        e.Handled = true;
    }

    private void VideoClickLayer_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;

        if (props.IsRightButtonPressed)
        {
            OpenRootContextMenu();
            e.Handled = true;
            return;
        }

        if (!props.IsLeftButtonPressed) return;

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

    private void OpenRootContextMenu()
    {
        var menu       = this.FindControl<ContextMenu>("RootContextMenu");
        var videoPanel = this.FindControl<Panel>("VideoPanel");
        if (menu is null || videoPanel is null) return;

        ShowControls();
        menu.Open(videoPanel);
    }

    // ── Seek slider ──────────────────────────────────────────────────────────

    private void SeekSlider_OnSeekCommitted(object? sender, RoutedEventArgs e)
    {
        if (sender is not Controls.SeekBar seekBar) return;
        ViewModel?.CommitSeek(seekBar.Value);
        ShowControls();
    }

    // ── Top-bar buttons ──────────────────────────────────────────────────────

    private async void OpenButton_OnClick(object? sender, RoutedEventArgs e)
        => await OpenFromPickerAsync();

    private void FullscreenButton_OnClick(object? sender, RoutedEventArgs e)
        => ToggleFullscreen();

    // ── Custom title bar drag + window chrome ────────────────────────────────

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
        => Close();

    private void UpdateMaximizeIcon()
    {
        var icon = this.FindControl<PathIcon>("MaximizeIcon");
        if (icon is null) return;
        var data = this.FindResource(WindowState == WindowState.Maximized
            ? "Icon.WindowRestore"
            : "Icon.WindowMaximize");
        if (data is Avalonia.Media.StreamGeometry sg)
            icon.Data = sg;
    }

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

            case Key.OemPeriod: // . → frame step forward
                ViewModel.StepFrameCommand.Execute(null);
                ShowControls(); e.Handled = true; break;
            case Key.OemComma: // , → frame step back
                ViewModel.StepFrameBackCommand.Execute(null);
                ShowControls(); e.Handled = true; break;

            case Key.PageUp: // page up → previous chapter
                ViewModel.PreviousChapterCommand.Execute(null);
                ShowControls(); e.Handled = true; break;
            case Key.PageDown: // page down → next chapter
                ViewModel.NextChapterCommand.Execute(null);
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
                BeginExitFullscreen();
                e.Handled = true; break;

            // ── Dialogs / file ops ───────────────────────────────────────────
            case Key.O:
                await OpenFromPickerAsync();
                e.Handled = true; break;
            case Key.G when ctrl:
                await OpenJumpToTimeDialogAsync();
                e.Handled = true; break;
            case Key.OemQuestion: // ? → keyboard shortcuts
            case Key.F1:
                await OpenSettingsDialogAsync(SettingsSection.Shortcuts);
                e.Handled = true; break;
            case Key.I when alt:
                ViewModel.TakeScreenshotCommand.Execute(null);
                e.Handled = true; break;
            // ── Track navigation ──────────────────────────────────────────────────
            case Key.N:
                ViewModel.NextTrackCommand.Execute(null);
                e.Handled = true; break;
            case Key.P:
                ViewModel.PreviousTrackCommand.Execute(null);
                e.Handled = true; break;
            case Key.Q:
                ViewModel.TogglePlaylistCommand.Execute(null);
                e.Handled = true; break;
            case Key.B:
                await OpenBookmarksDialogAsync();
                e.Handled = true; break;
        }
    }

    // ── Mouse scroll → volume ────────────────────────────────────────────────

    private void OnWindowPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (ViewModel is null) return;
        if (e.Delta.Y > 0)
            ViewModel.VolumeUpCommand.Execute(null);
        else if (e.Delta.Y < 0)
            ViewModel.VolumeDownCommand.Execute(null);
        ShowControls();
        e.Handled = true;
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

    private async void VideoAdjustments_Click(object? sender, RoutedEventArgs e)
        => await OpenSettingsDialogAsync(SettingsSection.Video);

    private async void KeyboardShortcuts_Click(object? sender, RoutedEventArgs e)
        => await OpenSettingsDialogAsync(SettingsSection.Shortcuts);

    private async void Cast_Click(object? sender, RoutedEventArgs e)
        => await OpenCastDialogAsync();

    private async void OpenFolder_Click(object? sender, RoutedEventArgs e)
        => await OpenFolderAsync();

    private async void AddToQueue_Click(object? sender, RoutedEventArgs e)
        => await AddToQueueAsync();

    private async void LoadSubtitleButton_OnClick(object? sender, RoutedEventArgs e)
        => await OpenSubtitleSettingsDialogAsync();

    private async void SettingsButton_Click(object? sender, RoutedEventArgs e)
        => await OpenSettingsDialogAsync(SettingsSection.WatchModes);

    private async void CastButton_Click(object? sender, RoutedEventArgs e)
        => await OpenCastDialogAsync();

    private void AboutButton_Click(object? sender, RoutedEventArgs e)
        => OpenAboutDialog();

    private void About_Click(object? sender, RoutedEventArgs e)
        => OpenAboutDialog();

    private async void BookmarksButton_Click(object? sender, RoutedEventArgs e)
        => await OpenBookmarksDialogAsync();

    private async void Bookmarks_Click(object? sender, RoutedEventArgs e)
        => await OpenBookmarksDialogAsync();

    private async void RecentItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string filePath } && ViewModel is not null)
            await ViewModel.OpenFileAsync(filePath);
    }

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

    private async Task OpenCastDialogAsync()
    {
        if (ViewModel is null) return;

        var dialog = new CastDialog(ViewModel);
        await dialog.ShowDialog(this);
        Focus();
    }

    // ── File open helpers ────────────────────────────────────────────────────

    private async Task OpenFromPickerAsync()
    {
        var files = await RunWithPlaybackPausedAsync(() => StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open media",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Media files")
                {
                    Patterns = ["*.mp4","*.mkv","*.avi","*.mov","*.webm",
                                "*.mp3","*.flac","*.wav","*.ogg","*.m4a","*.aac","*.opus"]
                },
                FilePickerFileTypes.All
            ]
        }));

        if (ViewModel is null) return;
        var paths = files.Select(f => f.TryGetLocalPath())
                         .Where(p => !string.IsNullOrWhiteSpace(p)).Cast<string>().ToList();
        if (paths.Count == 0) return;

        if (paths.Count == 1)
            await ViewModel.OpenFileAsync(paths[0]);
        else
            await ViewModel.LoadFilesAsync(paths);

        Focus(); ShowControls();
    }

    private async Task OpenFolderAsync()
    {
        if (ViewModel is null) return;
        var folders = await RunWithPlaybackPausedAsync(() => StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Open folder", AllowMultiple = false }));
        var dir = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(dir)) return;

        var mediaExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".mp4",".mkv",".avi",".mov",".webm",".mp3",".flac",".ogg",".opus",".aac",".wav",".m4a",".m4b",".wma" };
        var mediaFiles = Directory.EnumerateFiles(dir)
            .Where(f => mediaExts.Contains(Path.GetExtension(f)))
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (mediaFiles.Count == 0) return;

        await ViewModel.LoadFilesAsync(mediaFiles);
        Focus(); ShowControls();
    }

    private async Task AddToQueueAsync()
    {
        if (ViewModel is null) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add to queue",
            AllowMultiple = true,
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
        var paths = files.Select(f => f.TryGetLocalPath())
                         .Where(p => !string.IsNullOrWhiteSpace(p)).Cast<string>().ToList();
        if (paths.Count > 0)
            await ViewModel.AddFilesAsync(paths);
    }

    private async Task OpenSubtitleFileAsync()
    {
        var files = await RunWithPlaybackPausedAsync(() => StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
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
        }));

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
        ViewModel.RefreshTracksNow();
        var dialog = new SubtitleSettingsDialog(
            ViewModel.CurrentSubtitleSettings,
            ViewModel.CurrentFilePath,
            ViewModel.SubtitleTracks);
        var result = await RunWithPlaybackPausedAsync(() =>
            dialog.ShowDialog<Lumyn.App.Models.SubtitleSettings?>(this));
        if (result is not null)
        {
            await ViewModel.ApplySubtitleSettingsAsync(result);
            Focus();
        }
    }

    private void OpenAboutDialog()
    {
        var dialog = new AboutDialog();
        dialog.ShowDialog(this);
    }

    private async Task OpenBookmarksDialogAsync()
    {
        if (ViewModel is null) return;
        var dialog = new BookmarksDialog(ViewModel);
        await dialog.ShowDialog(this);
    }

    private async Task OpenSettingsDialogAsync(SettingsSection section)
    {
        if (ViewModel is null) return;
        var prev = ViewModel.CurrentVideoAdjustments;
        var dialog = new SettingsDialog(
            prev,
            ViewModel.CurrentAudioClarityMode,
            ViewModel.CurrentUiVisibility,
            adj => ViewModel.ApplyVideoAdjustments(adj),
            section);
        var result = await dialog.ShowDialog<SettingsDialogResult?>(this);
        if (result is null)
        {
            ViewModel.ApplyVideoAdjustments(prev);
        }
        else if (result.WatchMode is { } mode)
        {
            await ViewModel.ApplyWatchModeAsync(mode);
            if (result.AudioClarityMode != ViewModel.CurrentAudioClarityMode)
                ViewModel.ApplyAudioClarityMode(result.AudioClarityMode);
            ViewModel.ApplyUiVisibility(result.UiVisibility);
        }
        else
        {
            ViewModel.ApplyVideoAdjustments(result.VideoAdjustments);
            if (result.AudioClarityMode != ViewModel.CurrentAudioClarityMode)
                ViewModel.ApplyAudioClarityMode(result.AudioClarityMode);
            ViewModel.ApplyUiVisibility(result.UiVisibility);
        }
        Focus();
    }

    private async Task OpenJumpToTimeDialogAsync()
    {
        if (ViewModel is null || !ViewModel.HasMedia) return;
        var dialog = new JumpToTimeDialog();
        var result = await RunWithPlaybackPausedAsync(() => dialog.ShowDialog<TimeSpan?>(this));
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
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var items = e.DataTransfer.TryGetFiles()?.ToArray() ?? [];
        e.Handled = true;
        if (items.Length == 0 || ViewModel is null) return;

        var mediaExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".mp4",".mkv",".avi",".mov",".webm",".mp3",".flac",".ogg",".opus",".aac",".wav",".m4a",".m4b",".wma" };

        var paths = new List<string>();
        foreach (var item in items)
        {
            var local = item.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(local)) continue;
            if (Directory.Exists(local))
                paths.AddRange(Directory.EnumerateFiles(local)
                    .Where(f => mediaExts.Contains(Path.GetExtension(f)))
                    .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase));
            else
                paths.Add(local);
        }

        var subtitlePath = paths.FirstOrDefault(IsSubtitleFile);
        var mediaPaths = paths.Where(p => !IsSubtitleFile(p)).ToList();

        if (mediaPaths.Count > 0)
            await WaitForVideoSurfaceAsync();

        if (mediaPaths.Count == 1)
            await ViewModel.OpenFileAsync(mediaPaths[0]);
        else if (mediaPaths.Count > 1)
            await ViewModel.LoadFilesAsync(mediaPaths);

        if (!string.IsNullOrWhiteSpace(subtitlePath) && ViewModel.HasMedia)
            await ViewModel.LoadSubtitleFileAsync(subtitlePath);

        Focus();
        ShowControls();
    }

    private Task WaitForVideoSurfaceAsync()
    {
        var surface = this.FindControl<Controls.MpvVideoSurface>("VideoSurface");
        if (surface is null || surface.IsReadyForPlaybackOpen)
            return Task.CompletedTask;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };

        void Complete()
        {
            surface.ReadyForPlaybackOpen -= OnReady;
            timeout.Stop();
            tcs.TrySetResult();
        }

        void OnReady(object? sender, EventArgs e) => Complete();

        timeout.Tick += (_, _) => Complete();
        surface.ReadyForPlaybackOpen += OnReady;
        timeout.Start();

        return tcs.Task;
    }

    private static bool IsSubtitleFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".srt" or ".ass" or ".ssa" or ".vtt" or ".sub";
    }

    // ── Playlist drag-to-reorder ──────────────────────────────────────────────

    private const string PlaylistDragFormat = "lumyn/playlist-index";
    private int _playlistDragFromIndex = -1;

    private async void PlaylistDragHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
        if ((sender as Avalonia.Visual)?.DataContext is not PlaylistItem item) return;
        _playlistDragFromIndex = item.Index;
        e.Handled = true;
        await DragDrop.DoDragDropAsync(e, SentinelDataTransfer.Instance, DragDropEffects.Move);
        _playlistDragFromIndex = -1;
    }

    private void PlaylistDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = _playlistDragFromIndex >= 0
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void PlaylistDrop(object? sender, DragEventArgs e)
    {
        if (_playlistDragFromIndex < 0) return;
        if (sender is not ItemsControl ic) return;
        if (ViewModel is null) return;

        var from = _playlistDragFromIndex;
        var to = HitTestPlaylistIndex(ic, e.GetPosition(ic)) ?? from;
        if (to != from)
            ViewModel.MovePlaylistItem(from, to);
        e.Handled = true;
    }

    /// <summary>
    /// Walks the visual tree at <paramref name="point"/> inside <paramref name="ic"/>
    /// to find the <see cref="PlaylistItem.Index"/> of the item under the pointer.
    /// </summary>
    private static int? HitTestPlaylistIndex(ItemsControl ic, Point point)
    {
        var hit = ic.InputHitTest(point) as Avalonia.Visual;
        while (hit is not null && !ReferenceEquals(hit, ic))
        {
            if (hit.DataContext is PlaylistItem item)
                return item.Index;
            hit = hit.GetVisualParent();
        }
        return null;
    }

    // ── Playlist item context menu ────────────────────────────────────────────

    private static PlaylistItem? PlaylistCtxItem(object? sender)
    {
        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: { } t } })
            return t.DataContext as PlaylistItem;
        return null;
    }

    private void PlaylistCtx_Play(object? sender, RoutedEventArgs e)
    {
        var item = PlaylistCtxItem(sender);
        if (item is not null)
            ViewModel?.OpenPlaylistItemCommand.Execute(item.Index);
    }

    private void PlaylistCtx_Remove(object? sender, RoutedEventArgs e)
    {
        var item = PlaylistCtxItem(sender);
        if (item is not null)
            ViewModel?.RemovePlaylistItemCommand.Execute(item.Index);
    }

    private async void PlaylistCtx_AddFile(object? sender, RoutedEventArgs e)
        => await AddToQueueAsync();

    private async void PlaylistCtx_AddFolder(object? sender, RoutedEventArgs e)
        => await OpenFolderAsync();

    // ── Ambient border glow ──────────────────────────────────────────────────

    private async void GlowTimer_Tick(object? sender, EventArgs e)
    {
        if (_glowSampling) return;
        _glowSampling = true;
        try
        {
            var vm = ViewModel;
            if (vm is null || !vm.HasMedia)
            {
                _targetGlowColor = GlowOff;
                return;
            }

            if (vm.IsAudioMode || !vm.IsPlaying)
            {
                _targetGlowColor = GlowOff;
                return;
            }

            var tmpPath = Path.Combine(Path.GetTempPath(), "lumyn_glow.ppm");
            var ok = await Task.Run(() => vm.TakeGlowSnapshot(tmpPath));
            if (!ok) return;

            var color = await Task.Run(() => SamplePpmEdgeColor(tmpPath));
            _targetGlowColor = color;
            try { File.Delete(tmpPath); } catch { }
        }
        finally
        {
            _glowSampling = false;
        }
    }

    private void GlowLerpTimer_Tick(object? sender, EventArgs e)
    {
        var current = _glowBrush.Color;
        var target = _targetGlowColor;
        if (current == target) return;

        static byte Lerp(byte from, byte to)
            => (byte)Math.Round(from + (to - from) * 0.18);

        var next = Color.FromArgb(
            Lerp(current.A, target.A),
            Lerp(current.R, target.R),
            Lerp(current.G, target.G),
            Lerp(current.B, target.B));

        // Snap to target when close enough to avoid infinite micro-steps
        if (Math.Abs(next.A - target.A) <= 1 &&
            Math.Abs(next.R - target.R) <= 1 &&
            Math.Abs(next.G - target.G) <= 1 &&
            Math.Abs(next.B - target.B) <= 1)
            next = target;

        _glowBrush.Color = next;
    }

    private static Color SamplePpmEdgeColor(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                           FileShare.Read, 4096, useAsync: false);
            if (ReadPpmLine(fs) != "P6") return GlowOff;

            // Skip any comment lines
            string line;
            do { line = ReadPpmLine(fs); } while (line.StartsWith('#'));

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2
                || !int.TryParse(parts[0], out var width)  || width  <= 0
                || !int.TryParse(parts[1], out var height) || height <= 0)
                return GlowOff;

            if (!int.TryParse(ReadPpmLine(fs), out var maxVal) || maxVal != 255)
                return GlowOff;

            long dataStart = fs.Position;
            long stride = (long)width * 3;

            var topRow = ReadPpmRow(fs, dataStart, stride, 0,          width);
            var botRow = ReadPpmRow(fs, dataStart, stride, height - 1, width);

            long rSum = 0, gSum = 0, bSum = 0;
            int count = 0;
            const int step = 10;

            foreach (var row in new[] { topRow, botRow })
            {
                for (int x = 0; x < width; x += step)
                {
                    int off = x * 3;
                    if (off + 2 >= row.Length) break;
                    rSum += row[off];
                    gSum += row[off + 1];
                    bSum += row[off + 2];
                    count++;
                }
            }

            if (count == 0) return GlowOff;

            var r = (byte)(rSum / count);
            var g = (byte)(gSum / count);
            var b = (byte)(bSum / count);
            (r, g, b) = BoostSaturation(r, g, b, 1.6f);
            return Color.FromArgb(150, r, g, b);
        }
        catch { return GlowOff; }
    }

    private static byte[] ReadPpmRow(FileStream fs, long dataStart, long stride, int row, int width)
    {
        var buf = new byte[width * 3];
        fs.Seek(dataStart + row * stride, SeekOrigin.Begin);
        int read = 0;
        while (read < buf.Length)
        {
            int n = fs.Read(buf, read, buf.Length - read);
            if (n == 0) break;
            read += n;
        }
        return buf;
    }

    private static string ReadPpmLine(Stream s)
    {
        var sb = new System.Text.StringBuilder();
        int b;
        while ((b = s.ReadByte()) >= 0 && b != '\n')
            if (b != '\r') sb.Append((char)b);
        return sb.ToString().Trim();
    }

    private static (byte r, byte g, byte b) BoostSaturation(byte r, byte g, byte b, float factor)
    {
        float mid = (r + g + b) / 3f;
        return (
            (byte)Math.Clamp((int)(mid + (r - mid) * factor), 0, 255),
            (byte)Math.Clamp((int)(mid + (g - mid) * factor), 0, 255),
            (byte)Math.Clamp((int)(mid + (b - mid) * factor), 0, 255));
    }

    // ── Seek thumbnail preview ───────────────────────────────────────────────

    private void UpdateSeekThumbnail(PointerEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null || !vm.HasMedia || vm.IsAudioMode) return;

        var seekSlider = this.FindControl<Controls.SeekBar>("SeekSlider");
        var popup      = this.FindControl<Border>("SeekThumbnailPopup");
        var thumbImg   = this.FindControl<Avalonia.Controls.Image>("SeekThumbnailImage");
        var timeText   = this.FindControl<TextBlock>("SeekThumbnailTimeText");
        var videoPanel = this.FindControl<Panel>("VideoPanel");

        if (seekSlider is null || popup is null || thumbImg is null
            || timeText is null || videoPanel is null) return;

        var posOnSlider = e.GetPosition(seekSlider);
        var sliderWidth = seekSlider.Bounds.Width;
        if (sliderWidth <= 0) return;

        var progress = Math.Clamp(posOnSlider.X / sliderWidth, 0.0, 1.0);
        var duration = vm.MediaDuration;
        if (duration <= TimeSpan.Zero) return;

        var bmp = GetCachedThumbBitmap(progress, vm);
        if (bmp is null) return;

        // Label always shows the cursor position — that is exactly where the
        // video will seek on click, so label and seek destination always match.
        thumbImg.Source = bmp;
        timeText.Text   = FormatSeekTime(TimeSpan.FromSeconds(progress * duration.TotalSeconds));

        var origin = seekSlider.TranslatePoint(new Point(0, 0), videoPanel);
        if (origin is null) return;

        var rawX       = origin.Value.X + posOnSlider.X - 82;
        var leftMargin = Math.Clamp(rawX, 0, Math.Max(0, videoPanel.Bounds.Width - 164));
        popup.Margin    = new Thickness(leftMargin, 0, 0, 88);
        popup.IsVisible = true;
    }

    private Avalonia.Media.Imaging.Bitmap? GetCachedThumbBitmap(double progress, MainViewModel vm)
    {
        // On file change, dispose and clear every cached Bitmap.
        if (!string.Equals(_thumbCacheForFile, vm.CurrentFilePath, StringComparison.Ordinal))
        {
            foreach (var (_, bmp) in _thumbBitmapCache.Values) bmp.Dispose();
            _thumbBitmapCache.Clear();
            _thumbCacheForFile = vm.CurrentFilePath;
        }

        var bytes = vm.Thumbnails.GetNearest(progress);
        if (bytes is null) return null;

        // 2 000-bucket key gives ~0.05 % progress resolution — plenty for hover.
        var key = (int)(Math.Clamp(progress, 0.0, 1.0) * 1999);

        if (_thumbBitmapCache.TryGetValue(key, out var entry))
        {
            // Phase 2 may have inserted a closer frame for this bucket.
            // If the byte[] reference changed, dispose the stale Bitmap and re-decode.
            if (ReferenceEquals(entry.Src, bytes)) return entry.Bmp;
            entry.Bmp.Dispose();
            _thumbBitmapCache.Remove(key);
        }

        try
        {
            using var ms  = new System.IO.MemoryStream(bytes);
            var bmp = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(ms, 160);
            _thumbBitmapCache[key] = (bytes, bmp);
            return bmp;
        }
        catch { return null; }
    }

    private void HideSeekThumbnailPopup()
    {
        var popup = this.FindControl<Border>("SeekThumbnailPopup");
        if (popup is not null) popup.IsVisible = false;
    }

    private static string FormatSeekTime(TimeSpan t)
        => t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
            : $"{t.Minutes}:{t.Seconds:D2}";

    // ── Fullscreen ───────────────────────────────────────────────────────────

    private void ToggleFullscreen()
    {
        if (_isApplyingFullscreenState) return;

        if (WindowState == WindowState.FullScreen)
            BeginExitFullscreen();
        else
            BeginEnterFullscreen();
    }

    private void BeginEnterFullscreen()
    {
        _isApplyingFullscreenState = true;
        _restoreWindowStateAfterFullscreen = WindowState == WindowState.Maximized
            ? WindowState.Maximized
            : WindowState.Normal;

        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            Dispatcher.UIThread.Post(() =>
            {
                WindowState = WindowState.FullScreen;
                CompleteFullscreenTransition();
            }, DispatcherPriority.Background);
            return;
        }

        WindowState = WindowState.FullScreen;
        CompleteFullscreenTransition();
    }

    private void BeginExitFullscreen()
    {
        _isApplyingFullscreenState = true;
        WindowState = _restoreWindowStateAfterFullscreen == WindowState.Maximized
            ? WindowState.Maximized
            : WindowState.Normal;
        CompleteFullscreenTransition();
    }

    private void CompleteFullscreenTransition()
    {
        UpdateFullscreenIcon();
        ShowControls();

        Dispatcher.UIThread.Post(() => _isApplyingFullscreenState = false, DispatcherPriority.Background);
    }

    private void UpdateFullscreenIcon()
    {
        var icon = this.FindControl<PathIcon>("FullscreenIcon");
        var iconCtrl = this.FindControl<PathIcon>("FullscreenCtrlIcon");
        var data = this.FindResource(
            WindowState == WindowState.FullScreen ? "Icon.FullscreenExit" : "Icon.Fullscreen");
        if (data is Avalonia.Media.StreamGeometry sg)
        {
            if (icon is not null) icon.Data = sg;
            if (iconCtrl is not null) iconCtrl.Data = sg;
        }
    }

    private async Task<T> RunWithPlaybackPausedAsync<T>(Func<Task<T>> action)
    {
        if (ViewModel is null)
            return await action();

        var resume = ViewModel.IsPlaying;
        if (resume)
            ViewModel.PausePlayback();

        try
        {
            return await action();
        }
        finally
        {
            if (resume && ViewModel.HasMedia)
                ViewModel.ResumePlayback();

            Focus();
            ShowControls();
        }
    }
}

// Minimal fluent helper so we can call .Also() for inline event subscription
internal static class ObjectExtensions
{
    internal static T Also<T>(this T obj, Action<T> action) { action(obj); return obj; }
}

/// <summary>
/// Minimal <see cref="IDataTransfer"/> sentinel used to initiate playlist drag-drop
/// without carrying any payload (the dragged index is stored in a field).
/// </summary>
file sealed class SentinelDataTransfer : Avalonia.Input.IDataTransfer
{
    public static readonly SentinelDataTransfer Instance = new();
    public IReadOnlyList<Avalonia.Input.DataFormat> Formats => [];
    public IReadOnlyList<Avalonia.Input.IDataTransferItem> Items => [];
    public void Dispose() { }
}
