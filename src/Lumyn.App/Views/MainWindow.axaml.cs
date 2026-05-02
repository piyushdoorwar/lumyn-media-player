using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lumyn.App.ViewModels;
using Lumyn.Core.Services;

namespace Lumyn.App.Views;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _positionTimer;
    private readonly DispatcherTimer _hideControlsTimer;
    private long _lastControlsPulseMs;
    private bool _isApplyingFullscreenState;

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
        PointerWheelChanged += OnWindowPointerWheelChanged;
        Opened += (_, _) =>
        {
            Focus();
            // Wire playlist reorder DragDrop after visual tree is ready.
            var pic = this.FindControl<ItemsControl>("PlaylistItemsControl");
            if (pic is not null)
            {
                pic.AddHandler(DragDrop.DragOverEvent, PlaylistDragOver);
                pic.AddHandler(DragDrop.DropEvent, PlaylistDrop);
            }
        };
        Closing += (_, _) => ViewModel?.SaveResumePosition();
        Closed += (_, _) => ViewModel?.Dispose();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
            {
                UpdateMaximizeIcon();
                UpdateTopBarVisibility();
            }
        };
        DataContextChanged += (_, _) => UpdateTopBarVisibility();
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
        UpdateTopBarVisibility();

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

    private void UpdateTopBarVisibility()
    {
        var topBar = this.FindControl<Border>("TopBar");
        if (topBar is null) return;

        topBar.IsVisible = ViewModel?.ControlsVisible == true && WindowState != WindowState.FullScreen;
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
        => ToggleFullscreen();

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
                WindowState = WindowState.Normal;
                ShowControls(); e.Handled = true; break;

            // ── Dialogs / file ops ───────────────────────────────────────────
            case Key.O:
                await OpenFromPickerAsync();
                e.Handled = true; break;
            case Key.G when ctrl:
                await OpenJumpToTimeDialogAsync();
                e.Handled = true; break;
            case Key.OemQuestion: // ? → keyboard shortcuts
            case Key.F1:
                OpenKeyboardShortcutsDialog();
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

        var castItem = cm.Items.OfType<MenuItem>().FirstOrDefault(m => m.Name == "CastMenuItem");
        if (castItem is not null)
            _ = PopulateCastMenuItemAsync(castItem);
    }

    private async void JumpToTime_Click(object? sender, RoutedEventArgs e)
        => await OpenJumpToTimeDialogAsync();

    private async void LoadSubtitle_Click(object? sender, RoutedEventArgs e)
        => await OpenSubtitleFileAsync();

    private async void VideoAdjustments_Click(object? sender, RoutedEventArgs e)
        => await OpenVideoAdjustmentsDialogAsync();

    private void KeyboardShortcuts_Click(object? sender, RoutedEventArgs e)
        => OpenKeyboardShortcutsDialog();

    private async void OpenFolder_Click(object? sender, RoutedEventArgs e)
        => await OpenFolderAsync();

    private async void AddToQueue_Click(object? sender, RoutedEventArgs e)
        => await AddToQueueAsync();

    private async void LoadSubtitleButton_OnClick(object? sender, RoutedEventArgs e)
        => await OpenSubtitleSettingsDialogAsync();

    private async void VideoAdjustmentsButton_OnClick(object? sender, RoutedEventArgs e)
        => await OpenVideoAdjustmentsDialogAsync();

    private void KeyboardShortcutsButton_OnClick(object? sender, RoutedEventArgs e)
        => OpenKeyboardShortcutsDialog();

    private async void CastButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || ViewModel is null) return;

        var menu = await BuildCastMenuAsync();
        button.ContextMenu = menu;
        menu.Open(button);
    }

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

    private async Task<ContextMenu> BuildCastMenuAsync()
    {
        var menu = new ContextMenu();
        var root = new MenuItem { Header = "Cast To" };
        menu.Items.Add(root);
        await PopulateCastMenuItemAsync(root);
        return menu;
    }

    private async Task PopulateCastMenuItemAsync(MenuItem castItem)
    {
        if (ViewModel is null) return;

        castItem.Items.Clear();
        castItem.Items.Add(new MenuItem { Header = "Searching...", IsEnabled = false });
        castItem.IsEnabled = ViewModel.HasMedia || ViewModel.IsCasting;

        if (ViewModel.IsCasting)
        {
            var stop = new MenuItem { Header = "Stop Casting" };
            stop.Click += (_, _) => ViewModel.StopCastingCommand.Execute(null);
            castItem.Items.Clear();
            castItem.Items.Add(stop);
            castItem.Items.Add(new Separator());
        }

        if (!ViewModel.HasMedia)
        {
            castItem.Items.Clear();
            castItem.Items.Add(new MenuItem { Header = "Open media before casting", IsEnabled = false });
            return;
        }

        await ViewModel.RefreshCastDevicesAsync();

        if (!ViewModel.IsCasting)
            castItem.Items.Clear();

        if (ViewModel.CastDevices.Count == 0)
        {
            castItem.Items.Add(new MenuItem { Header = "No devices found", IsEnabled = false });
            return;
        }

        foreach (var device in ViewModel.CastDevices)
        {
            var target = device;
            var item = new MenuItem { Header = target.Name };
            item.Click += async (_, _) => await ViewModel.CastToDeviceAsync(target);
            castItem.Items.Add(item);
        }
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

    private void OpenKeyboardShortcutsDialog()
    {
        var dialog = new KeyboardShortcutsDialog();
        dialog.ShowDialog(this);
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

    private async Task OpenVideoAdjustmentsDialogAsync()
    {
        if (ViewModel is null) return;
        var prev = ViewModel.CurrentVideoAdjustments;
        var dialog = new VideoAdjustmentsDialog(
            prev,
            adj => ViewModel.ApplyVideoAdjustments(adj));
        var result = await dialog.ShowDialog<Lumyn.App.Models.VideoAdjustments?>(this);
        // Apply final result; revert to previous state on cancel.
        ViewModel.ApplyVideoAdjustments(result ?? prev);
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

    // ── Fullscreen ───────────────────────────────────────────────────────────

    private void ToggleFullscreen()
    {
        if (_isApplyingFullscreenState) return;
        _isApplyingFullscreenState = true;

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

        Dispatcher.UIThread.Post(() => _isApplyingFullscreenState = false, DispatcherPriority.Background);
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
