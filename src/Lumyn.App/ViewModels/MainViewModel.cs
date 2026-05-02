using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Lumyn.App.Models;
using Lumyn.Core.Models;
using Lumyn.Core.Services;

namespace Lumyn.App.ViewModels;

public sealed record TrackInfo(int Id, string Name, bool IsSelected = false);

/// <summary>One entry in the playlist / queue shown in the sidebar.</summary>
public sealed record PlaylistItem(int Index, string FilePath, bool IsCurrent)
{
    public string DisplayName => Path.GetFileName(FilePath);
}

/// <summary>A recently-played file shown on the start screen.</summary>
public sealed record RecentFileItem(
    string FilePath,
    double ProgressPct,
    TimeSpan ResumePosition)
{
    public string DisplayName   => Path.GetFileNameWithoutExtension(FilePath);
    public string Directory     => Path.GetDirectoryName(FilePath) ?? "";
    public bool   HasResume     => ProgressPct >= 0;
    public string ResumeLabel   =>
        ResumePosition.TotalHours >= 1
            ? $"Resume from {(int)ResumePosition.TotalHours}:{ResumePosition.Minutes:D2}:{ResumePosition.Seconds:D2}"
            : $"Resume from {ResumePosition.Minutes}:{ResumePosition.Seconds:D2}";
}

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly string[] SubtitleExtensions = [".srt", ".ass", ".ssa", ".vtt", ".sub"];

    private static readonly float[] SpeedSteps = [0.25f, 0.5f, 0.75f, 1.0f, 1.25f, 1.5f, 2.0f, 3.0f, 4.0f];

    private static readonly string[] AudioExtensions =
    [
        ".mp3", ".flac", ".ogg", ".opus", ".aac", ".wav", ".m4a", ".m4b", ".wma",
        ".aiff", ".aif", ".alac", ".ape", ".mka", ".mp2", ".ac3", ".dts",
        ".amr", ".wv", ".tta", ".tak", ".dsf", ".dff"
    ];

    private static readonly string[] CoverArtNames =
        ["cover", "folder", "album", "artwork", "front", "art"];

    private readonly PlaybackService _playback;
    private readonly SettingsService _settings;
    private readonly DlnaCastService _casting;
    private readonly DispatcherTimer _osdTimer;
    private int _stateRefreshQueued;
    private int _castStateRefreshQueued;
    private long _lastTrackRevision = -1;
    private long _lastCastStateRefreshMs;

    private string _title = "Lumyn";
    private string _timeText = "00:00 / 00:00";
    private string? _errorMessage;
    private double _seekValue;
    private int _volume = 80;
    private bool _isPlaying;
    private bool _isMuted;
    private bool _controlsVisible = true;
    private bool _isSeeking;
    private float _speed = 1.0f;
    private bool _isLooping;
    private bool _isAlwaysOnTop;
    private bool _isCasting;
    private bool _isCastPlaying;
    private TimeSpan _castPosition;
    private TimeSpan _castDuration;
    private bool _isDiscoveringCastDevices;
    private string? _castTargetName;
    private string? _castStatusText;
    private string? _osdMessage;
    private TrackInfo[] _audioTracks = [];
    private TrackInfo[] _subtitleTracks = [];
    private string? _currentSubtitleText;
    private int _seekStep = 5;   // seconds; 5 | 10 | 30
    private IReadOnlyList<double> _chapterPositions = [];

    // ── Subtitle overlay (Avalonia-rendered to avoid duplicate native subtitles) ───
    private List<Lumyn.Core.Services.SubtitleLine> _subtitleLines = [];
    private bool _useSubtitleOverlay;

    // ── Subtitle appearance ───────────────────────────────────────────────────
    private double _subtitleFontSizeValue = 22;
    private FontFamily _subtitleFontFamily = FontFamily.Default;
    private IBrush _subtitleForeground = Brushes.White;
    // ── Video adjustments ───────────────────────────────────────────────
    private Lumyn.App.Models.VideoAdjustments _videoAdjustments = Lumyn.App.Models.VideoAdjustments.Default;

    // ── Audio mode ──────────────────────────────────────────────────────
    private bool _isAudioOnly;
    private string? _trackTitle;
    private string? _trackArtist;
    private string? _trackAlbum;
    private Avalonia.Media.Imaging.Bitmap? _coverArtBitmap;

    // ── Playlist / queue ───────────────────────────────────────────────
    private List<string> _playlist = [];
    private int _playlistIndex = -1;
    private bool _isPlaylistVisible;

    public MainViewModel(PlaybackService playback, SettingsService settings, DlnaCastService? casting = null)
    {
        _playback = playback;
        _settings = settings;
        _casting = casting ?? new DlnaCastService();

        // Restore persisted session preferences
        _volume   = _settings.LastVolume;
        _speed    = _settings.LastSpeed;
        _seekStep = _settings.SeekStep;
        _playback.SetVolume(_volume);
        if (Math.Abs(_speed - 1.0f) > 0.001f)
            _playback.SetSpeed(_speed);

        _osdTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _osdTimer.Tick += (_, _) =>
        {
            _osdTimer.Stop();
            OsdMessage = null;
        };

        _playback.StateChanged += (_, _) => QueueRefreshState();

        TogglePlayPauseCommand = new RelayCommand(_ => { _ = TogglePlayPauseAsync(); });
        StopCommand            = new RelayCommand(_ => { _ = StopAsync(); });
        ToggleMuteCommand      = new RelayCommand(_ => ToggleMute());
        SeekBackwardCommand    = new RelayCommand(_ => SeekRelative(-_seekStep));
        SeekForwardCommand     = new RelayCommand(_ => SeekRelative(_seekStep));
        SeekBackward30Command  = new RelayCommand(_ => SeekRelative(-30));
        SeekForward30Command   = new RelayCommand(_ => SeekRelative(30));
        VolumeUpCommand        = new RelayCommand(_ => { Volume += 5; ShowOsd($"Volume: {Volume}%"); });
        VolumeDownCommand      = new RelayCommand(_ => { Volume -= 5; ShowOsd($"Volume: {Volume}%"); });
        SpeedUpCommand         = new RelayCommand(_ => AdjustSpeed(+1));
        SpeedDownCommand       = new RelayCommand(_ => AdjustSpeed(-1));
        ResetSpeedCommand      = new RelayCommand(_ => SetSpeed(1.0f));
        ToggleLoopCommand      = new RelayCommand(_ => ToggleLoop());
        StepFrameCommand       = new RelayCommand(_ => _playback.StepFrame());
        StepFrameBackCommand   = new RelayCommand(_ => _playback.StepFrameBack());
        TakeScreenshotCommand  = new RelayCommand(_ => TakeScreenshot());
        ToggleAlwaysOnTopCommand = new RelayCommand(_ => IsAlwaysOnTop = !IsAlwaysOnTop);
        SetAudioTrackCommand   = new RelayCommand(p => SetAudioTrack(p));
        SetSubtitleTrackCommand = new RelayCommand(p => SetSubtitleTrack(p));
        CycleAudioTrackCommand = new RelayCommand(_ => CycleAudioTrack());
        CycleSubtitleTrackCommand = new RelayCommand(_ => CycleSubtitleTrack());
        SetSpeedCommand        = new RelayCommand(p => ParseAndSetSpeed(p));
        PreviousTrackCommand   = new RelayCommand(_ => { _ = NavigateTrackAsync(-1); });
        NextTrackCommand       = new RelayCommand(_ => { _ = NavigateTrackAsync(+1); });
        TogglePlaylistCommand  = new RelayCommand(_ => IsPlaylistVisible = !IsPlaylistVisible);
        OpenPlaylistItemCommand   = new RelayCommand(p => { if (p is int i) _ = PlayFromIndexAsync(i); });
        RemovePlaylistItemCommand = new RelayCommand(p => { if (p is int i) RemoveFromPlaylist(i); });
        ClearPlaylistCommand   = new RelayCommand(_ => ClearPlaylist());
        RemoveRecentFileCommand = new RelayCommand(p => { if (p is string path) RemoveRecentFile(path); });
        PreviousChapterCommand = new RelayCommand(_ => _playback.SeekToChapter(-1));
        NextChapterCommand     = new RelayCommand(_ => _playback.SeekToChapter(+1));
        CycleSeekStepCommand   = new RelayCommand(_ => CycleSeekStep());
        StopCastingCommand     = new RelayCommand(_ => { _ = StopCastingAsync(); });
        ToggleCastPlaybackCommand = new RelayCommand(_ => { _ = ToggleCastPlaybackAsync(); });
        CastVolumeDownCommand  = new RelayCommand(_ => { _ = ChangeCastVolumeAsync(-5); });
        CastVolumeUpCommand    = new RelayCommand(_ => { _ = ChangeCastVolumeAsync(+5); });

        _playback.EndReached += (_, _) => Dispatcher.UIThread.InvokeAsync(() =>
        {
            _settings.ClearResumePosition(CurrentFilePath);
            RefreshState();
            // Auto-advance to next playlist item when not looping.
            if (!_playback.IsLooping && HasNextTrack)
                _ = NavigateTrackAsync(+1);
        });
        _playback.ErrorOccurred += (_, message) =>
            Dispatcher.UIThread.InvokeAsync(() => ErrorMessage = message);

        ErrorMessage = _playback.InitializationError;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // ── Read-only state ──────────────────────────────────────────────────────

    public PlaybackService Playback => _playback;
    public string? CurrentFilePath => _playback.CurrentFilePath;
    public IReadOnlyList<string> RecentFiles => _settings.RecentFiles;
    public ObservableCollection<DlnaCastDevice> CastDevices { get; } = [];

    public bool HasRecentFiles => _settings.RecentFiles.Count > 0;

    public IReadOnlyList<RecentFileItem> RecentFileItems =>
        _settings.RecentFiles
            .Where(File.Exists)
            .Select(f =>
            {
                var (pos, pct) = _settings.GetResumeInfo(f);
                return new RecentFileItem(f, pct, pos);
            })
            .ToList();

    // ── Bookmarks ────────────────────────────────────────────────────────────

    public IReadOnlyList<BookmarkEntry> GetBookmarksForCurrentFile()
        => string.IsNullOrWhiteSpace(CurrentFilePath)
            ? []
            : _settings.GetBookmarks(CurrentFilePath);

    public void AddBookmarkAtCurrentPosition(string label)
    {
        if (string.IsNullOrWhiteSpace(CurrentFilePath)) return;
        var pos = _playback.Position;
        if (pos == TimeSpan.Zero && !HasMedia) return;
        var lbl = string.IsNullOrWhiteSpace(label)
            ? (pos.TotalHours >= 1
                ? $"{(int)pos.TotalHours}:{pos.Minutes:D2}:{pos.Seconds:D2}"
                : $"{pos.Minutes}:{pos.Seconds:D2}")
            : label.Trim();
        _settings.AddBookmark(CurrentFilePath, pos, lbl);
        ShowOsd($"Bookmark added: {lbl}");
    }

    public void RemoveBookmark(int index)
    {
        if (string.IsNullOrWhiteSpace(CurrentFilePath)) return;
        _settings.RemoveBookmark(CurrentFilePath, index);
    }

    public void RenameBookmark(int index, string newLabel)
    {
        if (string.IsNullOrWhiteSpace(CurrentFilePath)) return;
        _settings.RenameBookmark(CurrentFilePath, index, newLabel);
    }

    public void RemoveRecentFile(string filePath)
    {
        _settings.RemoveRecentFile(filePath);
        OnPropertyChanged(nameof(RecentFiles));
        OnPropertyChanged(nameof(RecentFileItems));
        OnPropertyChanged(nameof(HasRecentFiles));
    }

    public void JumpToBookmark(TimeSpan position)
    {
        _playback.Seek(position);
        ShowOsd($"Jumped to {(position.TotalHours >= 1 ? $"{(int)position.TotalHours}:{position.Minutes:D2}:{position.Seconds:D2}" : $"{position.Minutes}:{position.Seconds:D2}")}");
    }

    // ── Bindable properties ──────────────────────────────────────────────────

    public string Title
    {
        get => _title;
        private set => SetField(ref _title, value);
    }

    public string TimeText
    {
        get => _timeText;
        private set => SetField(ref _timeText, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasMedia => !string.IsNullOrWhiteSpace(CurrentFilePath);

    /// <summary>Current subtitle text to display as an overlay (null = hidden).</summary>
    public string? CurrentSubtitleText
    {
        get => _currentSubtitleText;
        private set => SetField(ref _currentSubtitleText, value);
    }

    /// <summary>Font size (in pts) for the subtitle overlay, derived from settings.</summary>
    public double SubtitleFontSizeValue
    {
        get => _subtitleFontSizeValue;
        private set => SetField(ref _subtitleFontSizeValue, value);
    }

    /// <summary>Font family for the subtitle overlay, derived from settings.</summary>
    public FontFamily SubtitleFontFamily
    {
        get => _subtitleFontFamily;
        private set => SetField(ref _subtitleFontFamily, value);
    }

    /// <summary>Foreground brush for the subtitle overlay, derived from settings.</summary>
    public IBrush SubtitleForeground
    {
        get => _subtitleForeground;
        private set => SetField(ref _subtitleForeground, value);
    }

    // ── Video adjustments ───────────────────────────────────────────────

    public Lumyn.App.Models.VideoAdjustments CurrentVideoAdjustments
    {
        get => _videoAdjustments;
        private set
        {
            if (SetField(ref _videoAdjustments, value))
                OnPropertyChanged(nameof(HasVideoAdjustments));
        }
    }

    public bool HasVideoAdjustments => !_videoAdjustments.IsDefault;

    // ── Audio mode properties ────────────────────────────────────────────

    public bool IsAudioOnly
    {
        get => _isAudioOnly;
        private set
        {
            if (SetField(ref _isAudioOnly, value))
                OnPropertyChanged(nameof(IsAudioMode));
        }
    }

    public bool IsAudioMode => _isAudioOnly && HasMedia;

    public string? TrackTitle
    {
        get => _trackTitle;
        private set => SetField(ref _trackTitle, value);
    }

    public string? TrackArtist
    {
        get => _trackArtist;
        private set
        {
            if (SetField(ref _trackArtist, value))
                OnPropertyChanged(nameof(TrackArtistAlbum));
        }
    }

    public string? TrackAlbum
    {
        get => _trackAlbum;
        private set
        {
            if (SetField(ref _trackAlbum, value))
                OnPropertyChanged(nameof(TrackArtistAlbum));
        }
    }

    public string? TrackArtistAlbum
    {
        get
        {
            var hasArtist = !string.IsNullOrWhiteSpace(_trackArtist);
            var hasAlbum  = !string.IsNullOrWhiteSpace(_trackAlbum);
            if (hasArtist && hasAlbum) return $"{_trackArtist} · {_trackAlbum}";
            if (hasArtist) return _trackArtist;
            if (hasAlbum)  return _trackAlbum;
            return null;
        }
    }

    public Avalonia.Media.Imaging.Bitmap? CoverArtBitmap
    {
        get => _coverArtBitmap;
        private set
        {
            var old = _coverArtBitmap;
            if (SetField(ref _coverArtBitmap, value))
                OnPropertyChanged(nameof(HasCoverArt));
            old?.Dispose();
        }
    }

    public bool HasCoverArt => _coverArtBitmap is not null;

    // ── Playlist / queue properties ─────────────────────────────────────────

    public bool IsPlaylistVisible
    {
        get => _isPlaylistVisible;
        set => SetField(ref _isPlaylistVisible, value);
    }

    public IReadOnlyList<PlaylistItem> PlaylistItems { get; private set; } = [];

    public int PlaylistCount => _playlist.Count;

    /// <summary>Shown in the audio panel: "3 / 12"</summary>
    public string PlaylistPositionLabel => _playlist.Count > 1
        ? $"{_playlistIndex + 1} / {_playlist.Count}"
        : string.Empty;

    /// <summary>Alias kept for AXAML bindings on the audio panel.</summary>
    public string FolderTrackLabel => PlaylistPositionLabel;

    public bool HasPlaylist => _playlist.Count > 1;

    /// <summary>Alias kept for AXAML bindings on the audio panel.</summary>
    public bool HasFolderTracks => HasPlaylist;

    public bool HasPreviousTrack => _playlist.Count > 1 && _playlistIndex > 0;
    public bool HasNextTrack     => _playlist.Count > 1 && _playlistIndex < _playlist.Count - 1;

    public double SeekValue
    {
        get => _seekValue;
        set => SetField(ref _seekValue, Math.Clamp(value, 0, 1000));
    }

    public int Volume
    {
        get => _volume;
        set
        {
            var next = Math.Clamp(value, 0, 150);
            if (SetField(ref _volume, next))
            {
                _playback.SetVolume(next);
                if (IsCasting)
                    _ = _casting.SetVolumeAsync(Math.Clamp(next, 0, 100));
                _settings.SaveSessionPreferences(next, _speed, _seekStep);
            }
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set => SetField(ref _isPlaying, value);
    }

    public bool IsMuted
    {
        get => _isMuted;
        private set => SetField(ref _isMuted, value);
    }

    public bool ControlsVisible
    {
        get => _controlsVisible;
        set
        {
            if (SetField(ref _controlsVisible, value))
                OnPropertyChanged(nameof(ControlsOpacity));
        }
    }

    public double ControlsOpacity => _controlsVisible ? 1.0 : 0.0;

    public float Speed
    {
        get => _speed;
        private set
        {
            if (SetField(ref _speed, value))
            {
                OnPropertyChanged(nameof(SpeedLabel));
                OnPropertyChanged(nameof(IsNormalSpeed));
            }
        }
    }

    public string SpeedLabel => $"{_speed:0.##}×";

    public bool IsNormalSpeed => Math.Abs(_speed - 1.0f) < 0.001f;

    public int SeekStep
    {
        get => _seekStep;
        private set
        {
            if (SetField(ref _seekStep, value))
                OnPropertyChanged(nameof(SeekStepLabel));
        }
    }

    public string SeekStepLabel => $"{_seekStep}s";

    public IReadOnlyList<double> ChapterPositions
    {
        get => _chapterPositions;
        private set => SetField(ref _chapterPositions, value);
    }

    public bool IsLooping
    {
        get => _isLooping;
        private set => SetField(ref _isLooping, value);
    }

    public bool IsAlwaysOnTop
    {
        get => _isAlwaysOnTop;
        set => SetField(ref _isAlwaysOnTop, value);
    }

    public bool IsCasting
    {
        get => _isCasting;
        private set
        {
            if (SetField(ref _isCasting, value))
                OnPropertyChanged(nameof(CastStatusText));
        }
    }

    public bool IsCastPlaying
    {
        get => _isCastPlaying;
        private set => SetField(ref _isCastPlaying, value);
    }

    public bool IsDiscoveringCastDevices
    {
        get => _isDiscoveringCastDevices;
        private set => SetField(ref _isDiscoveringCastDevices, value);
    }

    public string? CastTargetName
    {
        get => _castTargetName;
        private set
        {
            if (SetField(ref _castTargetName, value))
                OnPropertyChanged(nameof(CastStatusText));
        }
    }

    public string? CastStatusText
    {
        get => _castStatusText ?? (IsCasting && !string.IsNullOrWhiteSpace(CastTargetName)
            ? $"Casting to {CastTargetName}"
            : null);
        private set => SetField(ref _castStatusText, value);
    }

    public string? OsdMessage
    {
        get => _osdMessage;
        private set
        {
            if (SetField(ref _osdMessage, value))
                OnPropertyChanged(nameof(HasOsd));
        }
    }

    public bool HasOsd => !string.IsNullOrEmpty(_osdMessage);

    public TrackInfo[] AudioTracks
    {
        get => _audioTracks;
        private set => SetField(ref _audioTracks, value);
    }

    public TrackInfo[] SubtitleTracks
    {
        get => _subtitleTracks;
        private set => SetField(ref _subtitleTracks, value);
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    public ICommand TogglePlayPauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ToggleMuteCommand { get; }
    public ICommand SeekBackwardCommand { get; }
    public ICommand SeekForwardCommand { get; }
    public ICommand SeekBackward30Command { get; }
    public ICommand SeekForward30Command { get; }
    public ICommand VolumeUpCommand { get; }
    public ICommand VolumeDownCommand { get; }
    public ICommand SpeedUpCommand { get; }
    public ICommand SpeedDownCommand { get; }
    public ICommand ResetSpeedCommand { get; }
    public ICommand ToggleLoopCommand { get; }
    public ICommand StepFrameCommand { get; }
    public ICommand StepFrameBackCommand { get; }
    public ICommand TakeScreenshotCommand { get; }
    public ICommand ToggleAlwaysOnTopCommand { get; }
    public ICommand SetAudioTrackCommand { get; }
    public ICommand SetSubtitleTrackCommand { get; }
    public ICommand CycleAudioTrackCommand { get; }
    public ICommand CycleSubtitleTrackCommand { get; }
    public ICommand SetSpeedCommand { get; }    public ICommand PreviousTrackCommand { get; }
    public ICommand NextTrackCommand { get; }
    public ICommand TogglePlaylistCommand { get; }
    public ICommand OpenPlaylistItemCommand { get; }
    public ICommand RemovePlaylistItemCommand { get; }
    public ICommand ClearPlaylistCommand { get; }
    public ICommand RemoveRecentFileCommand { get; }
    public ICommand PreviousChapterCommand { get; }
    public ICommand NextChapterCommand { get; }
    public ICommand CycleSeekStepCommand { get; }
    public ICommand StopCastingCommand { get; }
    public ICommand ToggleCastPlaybackCommand { get; }
    public ICommand CastVolumeDownCommand { get; }
    public ICommand CastVolumeUpCommand { get; }
    // ── Public methods ───────────────────────────────────────────────────────

    public void PausePlayback() => _playback.Pause();

    public void ResumePlayback() => _playback.Play();

    public async Task RefreshCastDevicesAsync()
    {
        IsDiscoveringCastDevices = true;
        CastStatusText = "Looking for cast devices...";
        try
        {
            var devices = await _casting.DiscoverAsync(TimeSpan.FromSeconds(3));
            CastDevices.Clear();
            foreach (var device in devices)
                CastDevices.Add(device);

            CastStatusText = devices.Count == 0
                ? "No cast devices found"
                : null;
        }
        catch (Exception ex)
        {
            CastStatusText = $"Cast discovery failed: {ex.Message}";
        }
        finally
        {
            IsDiscoveringCastDevices = false;
            if (!IsCasting && CastDevices.Count > 0)
                CastStatusText = null;
        }
    }

    public async Task CastToDeviceAsync(DlnaCastDevice device)
    {
        if (string.IsNullOrWhiteSpace(CurrentFilePath) || !File.Exists(CurrentFilePath))
        {
            ShowOsd("Open a media file before casting");
            return;
        }

        try
        {
            CastStatusText = $"Connecting to {device.Name}...";
            var subtitlePath = CurrentSubtitleSettings.FilePath;
            await _casting.CastAsync(device, CurrentFilePath, subtitlePath, _playback.Position, Math.Clamp(Volume, 0, 100));
            _playback.Pause();
            IsCasting = true;
            IsCastPlaying = true;
            _castPosition = _playback.Position;
            _castDuration = _playback.Duration;
            CastTargetName = device.Name;
            CastStatusText = null;
            ShowOsd($"Casting to {device.Name}");
        }
        catch (Exception ex)
        {
            IsCasting = false;
            IsCastPlaying = false;
            _castPosition = TimeSpan.Zero;
            _castDuration = TimeSpan.Zero;
            CastTargetName = null;
            CastStatusText = $"Cast failed: {ex.Message}";
            ShowOsd(CastStatusText);
        }
    }

    public async Task StopCastingAsync()
    {
        try
        {
            await _casting.StopAsync();
        }
        catch
        {
            // Some renderers close the SOAP session before acknowledging stop.
        }
        finally
        {
            _casting.StopServer();
            IsCasting = false;
            IsCastPlaying = false;
            _castPosition = TimeSpan.Zero;
            _castDuration = TimeSpan.Zero;
            CastTargetName = null;
            CastStatusText = null;
            ShowOsd("Casting stopped");
        }
    }

    private async Task ToggleCastPlaybackAsync()
    {
        if (!IsCasting) return;
        try
        {
            if (IsCastPlaying)
            {
                await _casting.PauseAsync();
                IsCastPlaying = false;
                IsPlaying = false;
            }
            else
            {
                await _casting.PlayAsync();
                IsCastPlaying = true;
                IsPlaying = true;
            }
        }
        catch (Exception ex)
        {
            CastStatusText = $"Cast control failed: {ex.Message}";
        }
    }

    private async Task TogglePlayPauseAsync()
    {
        if (IsCasting)
        {
            await ToggleCastPlaybackAsync();
            return;
        }

        _playback.TogglePlayPause();
    }

    private async Task StopAsync()
    {
        if (IsCasting)
        {
            await StopCastingAsync();
            return;
        }

        Stop();
    }

    private async Task ChangeCastVolumeAsync(int delta)
    {
        if (!IsCasting) return;
        Volume = Math.Clamp(Volume + delta, 0, 100);
        try
        {
            await _casting.SetVolumeAsync(Volume);
        }
        catch (Exception ex)
        {
            CastStatusText = $"Cast volume failed: {ex.Message}";
        }
    }

    public async Task OpenFileAsync(string filePath)
    {
        // Single-file open: build playlist (folder-scanned for audio, single item for video).
        var ext = Path.GetExtension(filePath);
        var audioOnly = AudioExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        IsAudioOnly = audioOnly;
        OnPropertyChanged(nameof(IsAudioMode));

        if (audioOnly)
            SetPlaylistFromFolder(filePath);
        else
            SetPlaylist([filePath], 0);

        CoverArtBitmap = audioOnly ? FindCoverArt(filePath) : null;
        TrackTitle = null; TrackArtist = null; TrackAlbum = null;

        NotifyPlaylistState();

        await OpenPlaylistIndexInternalAsync(_playlistIndex);
    }

    /// <summary>Replaces the playlist with the given files and starts playing the first one.</summary>
    public async Task LoadFilesAsync(IReadOnlyList<string> paths)
    {
        var mediaPaths = paths.Where(p => !IsSubtitleFile(p))
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToList();
        var subtitlePath = paths.FirstOrDefault(IsSubtitleFile);

        if (mediaPaths.Count == 0) return;

        var firstExt = Path.GetExtension(mediaPaths[0]);
        var audioOnly = AudioExtensions.Contains(firstExt, StringComparer.OrdinalIgnoreCase);
        IsAudioOnly = audioOnly;
        OnPropertyChanged(nameof(IsAudioMode));

        SetPlaylist(mediaPaths, 0);
        CoverArtBitmap = audioOnly ? FindCoverArt(mediaPaths[0]) : null;
        TrackTitle = null; TrackArtist = null; TrackAlbum = null;
        NotifyPlaylistState();

        await OpenPlaylistIndexInternalAsync(0);

        if (!string.IsNullOrWhiteSpace(subtitlePath) && HasMedia)
            await LoadSubtitleFileAsync(subtitlePath);
    }

    /// <summary>Appends files to the existing playlist without interrupting playback. Starts if nothing is playing.</summary>
    public async Task AddFilesAsync(IReadOnlyList<string> paths)
    {
        var mediaPaths = paths
            .Where(p => !IsSubtitleFile(p))
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (mediaPaths.Count == 0) return;

        var firstNewIndex = _playlist.Count;
        _playlist.AddRange(mediaPaths);
        RebuildPlaylistItems();
        NotifyPlaylistState();

        if (!HasMedia)
        {
            _playlistIndex = firstNewIndex;
            await OpenPlaylistIndexInternalAsync(_playlistIndex);
        }
    }

    public async Task LoadSubtitleFileAsync(string path)
    {
        // Parse off the UI thread so large SRT files don't freeze the window.
        var lines = await Task.Run(() => Lumyn.Core.Services.SubtitleParser.Parse(path));
        _subtitleLines = lines;

        // Add the subtitle to mpv so it is available in the track menu. If our
        // parser can render it, keep mpv subtitles off to avoid duplicate text.
        _playback.LoadSubtitleFile(path);
        _useSubtitleOverlay = lines.Count > 0;
        if (_useSubtitleOverlay)
            _playback.SetSubtitleTrack(-1);

        // Keep appearance settings but update the file path so the dialog reopens correctly.
        CurrentSubtitleSettings = CurrentSubtitleSettings with { FilePath = path, EmbeddedTrackId = null };
        if (!string.IsNullOrWhiteSpace(_playback.CurrentFilePath))
            _settings.SaveSubtitleSettings(_playback.CurrentFilePath, SubtitleEntryFromSettings(CurrentSubtitleSettings));

        ShowOsd($"Subtitle: {Path.GetFileName(path)}");
    }

    /// <summary>
    /// Persists the last subtitle settings so the dialog re-opens with the previous values.
    /// </summary>
    public SubtitleSettings CurrentSubtitleSettings { get; private set; } =
        new(null, SubtitleFontSize.Medium, SubtitleFont.SansSerif, SubtitleColor.White, 0);

    /// <summary>
    /// Applies subtitle settings from the dialog: loads a file if selected and
    /// adjusts the sync delay immediately via mpv's native API.
    /// </summary>
    public Task ApplySubtitleSettingsAsync(SubtitleSettings s) => ApplySubtitleSettingsAsync(s, saveToCache: true);

    private async Task ApplySubtitleSettingsAsync(SubtitleSettings s, bool saveToCache)
    {
        CurrentSubtitleSettings = s;
        ApplySubtitleAppearance(s);

        if (s.EmbeddedTrackId is not null)
        {
            _subtitleLines = [];
            _useSubtitleOverlay = false;
            CurrentSubtitleText = null;
            _playback.SetSubtitleTrack(s.EmbeddedTrackId.Value);
            _playback.SubtitleDelayMs = s.DelayMs;
            RefreshTracks();

            var name = _subtitleTracks.FirstOrDefault(t => t.Id == s.EmbeddedTrackId.Value)?.Name
                ?? $"Subtitle {s.EmbeddedTrackId.Value}";
            ShowOsd($"Subtitle: {name}");
            if (s.DelayMs != 0)
                ShowOsd($"Subtitle delay: {s.DelayMs:+0;-0} ms");
        }
        else if (s.FilePath is null)
        {
            // Disable subtitles: clear parsed lines + reset delay
            _subtitleLines = [];
            _useSubtitleOverlay = false;
            CurrentSubtitleText = null;
            _playback.SetSubtitleTrack(-1);
            _playback.SubtitleDelayMs = 0;
            ShowOsd("Subtitles disabled");
        }
        else
        {
            await LoadSubtitleFileAsync(s.FilePath);
            _playback.SubtitleDelayMs = s.DelayMs;
            if (s.DelayMs != 0)
                ShowOsd($"Subtitle delay: {s.DelayMs:+0;-0} ms");
        }

        if (saveToCache && !string.IsNullOrWhiteSpace(_playback.CurrentFilePath))
        {
            if (s.FilePath is null && s.EmbeddedTrackId is null)
                _settings.ClearSubtitleSettings(_playback.CurrentFilePath);
            else
                _settings.SaveSubtitleSettings(_playback.CurrentFilePath, SubtitleEntryFromSettings(s));
        }
    }

    // ── Subtitle settings ↔ SettingsEntry conversion ──────────────────────

    private void ApplySubtitleAppearance(SubtitleSettings s)
    {
        SubtitleFontSizeValue = s.FontSize switch
        {
            SubtitleFontSize.Small  => 17,
            SubtitleFontSize.Large  => 30,
            _                       => 22
        };

        SubtitleFontFamily = s.Font switch
        {
            SubtitleFont.Serif     => new FontFamily("Liberation Serif,DejaVu Serif,Times New Roman,serif"),
            SubtitleFont.Monospace => new FontFamily("Courier New,Liberation Mono,DejaVu Sans Mono,monospace"),
            SubtitleFont.Arial     => new FontFamily("Arial,Liberation Sans,sans-serif"),
            _                      => FontFamily.Default
        };

        SubtitleForeground = s.Color switch
        {
            SubtitleColor.Yellow => new SolidColorBrush(Color.Parse("#FFE600")),
            SubtitleColor.Grey   => new SolidColorBrush(Color.Parse("#BBBBBB")),
            SubtitleColor.Black  => new SolidColorBrush(Color.Parse("#111111")),
            _                    => Brushes.White
        };
    }

    private static SubtitleSettings SubtitleSettingsFromEntry(SubtitleEntry e) => new(
        e.FilePath,
        Enum.TryParse<SubtitleFontSize>(e.FontSize, out var sz) ? sz : SubtitleFontSize.Medium,
        Enum.TryParse<SubtitleFont>(e.Font,     out var fn) ? fn : SubtitleFont.SansSerif,
        Enum.TryParse<SubtitleColor>(e.Color,   out var cl) ? cl : SubtitleColor.White,
        e.DelayMs,
        e.EmbeddedTrackId);

    private static SubtitleEntry SubtitleEntryFromSettings(SubtitleSettings s) => new()
    {
        FilePath = s.FilePath,
        FontSize = s.FontSize.ToString(),
        Font     = s.Font.ToString(),
        Color    = s.Color.ToString(),
        DelayMs  = s.DelayMs,
        EmbeddedTrackId = s.EmbeddedTrackId
    };

    public void JumpTo(TimeSpan position)
    {
        if (IsCasting)
        {
            _ = SeekCastAsync(position);
            return;
        }

        _playback.Seek(position);
    }

    public void ApplyVideoAdjustments(Lumyn.App.Models.VideoAdjustments adj)
    {
        _playback.SetBrightness(adj.Brightness);
        _playback.SetContrast(adj.Contrast);
        _playback.SetSaturation(adj.Saturation);
        _playback.SetVideoRotation(adj.Rotation);
        _playback.SetVideoZoom(adj.Zoom);
        _playback.SetVideoAspect(Lumyn.App.Models.VideoAdjustments.AspectToMpv(adj.Aspect));
        CurrentVideoAdjustments = adj;
        ShowOsd(adj.IsDefault ? "Video adjustments reset" : "Video adjustments applied");
    }

    public void EndSeek()
    {
        _isSeeking = false;
        var duration = IsCasting ? _castDuration : _playback.Duration;
        if (duration > TimeSpan.Zero)
        {
            var target = TimeSpan.FromMilliseconds(duration.TotalMilliseconds * (_seekValue / 1000.0));
            if (IsCasting)
                _ = SeekCastAsync(target);
            else
                _playback.Seek(target);
        }
    }

    public void PreviewSeek(double value)
    {
        _isSeeking = true;
        SeekValue = value;
    }

    public void CommitSeek(double value)
    {
        PreviewSeek(value);
        EndSeek();
    }

    public void RefreshState()
    {
        Interlocked.Exchange(ref _stateRefreshQueued, 0);

        var state = _playback.Snapshot();
        if (IsCasting)
            QueueCastStateRefresh();

        IsPlaying = IsCasting ? IsCastPlaying : state.IsPlaying;
        IsMuted = state.IsMuted;
        Speed = state.Speed;
        IsLooping = state.IsLooping;

        if (_volume != state.Volume)
        {
            _volume = state.Volume;
            OnPropertyChanged(nameof(Volume));
        }

        var displayPosition = IsCasting ? _castPosition : state.Position;
        var displayDuration = IsCasting && _castDuration > TimeSpan.Zero ? _castDuration : state.Duration;

        if (!_isSeeking && displayDuration > TimeSpan.Zero)
        {
            _seekValue = Math.Clamp(
                displayPosition.TotalMilliseconds / displayDuration.TotalMilliseconds * 1000.0,
                0, 1000);
            OnPropertyChanged(nameof(SeekValue));
        }

        TimeText = $"{FormatTime(displayPosition)} / {FormatTime(displayDuration)}";
        OnPropertyChanged(nameof(CurrentFilePath));
        OnPropertyChanged(nameof(HasMedia));
        OnPropertyChanged(nameof(IsAudioMode));
        OnPropertyChanged(nameof(RecentFiles));
        NotifyPlaylistState();
        RefreshTracksIfNeeded();

        // Update Avalonia subtitle overlay text.
        if (_useSubtitleOverlay && _subtitleLines.Count > 0 && state.IsPlaying)
        {
            var pos = state.Position;
            var hit = _subtitleLines.FirstOrDefault(l => pos >= l.Start && pos < l.End);
            CurrentSubtitleText = hit?.Text;
        }
        else
        {
            CurrentSubtitleText = null;
        }
    }

    public void QueueRefreshState()
    {
        if (Interlocked.Exchange(ref _stateRefreshQueued, 1) == 1)
            return;

        Dispatcher.UIThread.Post(RefreshState, DispatcherPriority.Background);
    }

    private void QueueCastStateRefresh()
    {
        var now = Environment.TickCount64;
        if (now - _lastCastStateRefreshMs < 900)
            return;
        _lastCastStateRefreshMs = now;

        if (Interlocked.Exchange(ref _castStateRefreshQueued, 1) == 1)
            return;

        _ = RefreshCastStateAsync();
    }

    private async Task RefreshCastStateAsync()
    {
        try
        {
            var positionTask = _casting.GetPositionInfoAsync();
            var playingTask = _casting.GetIsPlayingAsync();
            var position = await positionTask;
            var playing = await playingTask;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (position is not null)
                {
                    _castPosition = position.Position;
                    if (position.Duration > TimeSpan.Zero)
                        _castDuration = position.Duration;
                }

                if (playing is not null)
                    IsCastPlaying = playing.Value;

                IsPlaying = IsCastPlaying;
                if (!_isSeeking && _castDuration > TimeSpan.Zero)
                {
                    _seekValue = Math.Clamp(
                        _castPosition.TotalMilliseconds / _castDuration.TotalMilliseconds * 1000.0,
                        0, 1000);
                    OnPropertyChanged(nameof(SeekValue));
                }
                TimeText = $"{FormatTime(_castPosition)} / {FormatTime(_castDuration)}";
            });
        }
        catch
        {
            // Polling can fail while a renderer is buffering or switching state.
        }
        finally
        {
            Interlocked.Exchange(ref _castStateRefreshQueued, 0);
        }
    }

    public void SaveResumePosition()
    {
        _settings.SaveResumePosition(CurrentFilePath, _playback.Position, _playback.Duration);
    }

    public void ShowOsd(string message)
    {
        OsdMessage = message;
        _osdTimer.Stop();
        _osdTimer.Start();
    }

    public void Dispose()
    {
        SaveResumePosition();
        _osdTimer.Stop();
        _coverArtBitmap?.Dispose();
        _casting.Dispose();
        _playback.Dispose();
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private void Stop()
    {
        SaveResumePosition();
        _playback.Stop();
        Title         = "Lumyn";
        IsAudioOnly   = false;
        TrackTitle    = null;
        TrackArtist   = null;
        TrackAlbum    = null;
        CoverArtBitmap = null;
        ChapterPositions = [];
        // Leave the playlist intact so the user can resume or navigate.
        NotifyPlaylistState();
        RefreshState();
    }

    private void ToggleMute()
    {
        var willBeMuted = !_playback.IsMuted;
        _playback.ToggleMute();
        ShowOsd(willBeMuted ? "Muted" : $"Volume: {_playback.Volume}%");
    }

    private void SeekRelative(int seconds)
    {
        if (IsCasting)
        {
            var target = _castPosition + TimeSpan.FromSeconds(seconds);
            _ = SeekCastAsync(target < TimeSpan.Zero ? TimeSpan.Zero : target);
            ShowOsd(seconds < 0 ? $"Rewind {Math.Abs(seconds)}s" : $"Forward {seconds}s");
            return;
        }

        _playback.SeekRelative(TimeSpan.FromSeconds(seconds));
        ShowOsd(seconds < 0 ? $"Rewind {Math.Abs(seconds)}s" : $"Forward {seconds}s");
    }

    private async Task SeekCastAsync(TimeSpan position)
    {
        try
        {
            if (_castDuration > TimeSpan.Zero && position > _castDuration)
                position = _castDuration;

            await _casting.SeekAsync(position);
            _castPosition = position;
            TimeText = $"{FormatTime(_castPosition)} / {FormatTime(_castDuration)}";
            if (_castDuration > TimeSpan.Zero)
            {
                _seekValue = Math.Clamp(
                    _castPosition.TotalMilliseconds / _castDuration.TotalMilliseconds * 1000.0,
                    0, 1000);
                OnPropertyChanged(nameof(SeekValue));
            }
        }
        catch (Exception ex)
        {
            CastStatusText = $"Cast seek failed: {ex.Message}";
            ShowOsd(CastStatusText);
        }
    }

    private void SetSpeed(float rate)
    {
        _playback.SetSpeed(rate);
        Speed = rate;
        _settings.SaveSessionPreferences(_volume, rate, _seekStep);
        ShowOsd($"Speed: {new MainViewModel.SpeedDisplay(rate)}");
    }

    private void CycleSeekStep()
    {
        SeekStep = _seekStep switch { 5 => 10, 10 => 30, _ => 5 };
        _settings.SaveSessionPreferences(_volume, _speed, _seekStep);
        ShowOsd($"Seek step: {_seekStep}s");
    }

    private void AdjustSpeed(int direction)
    {
        var idx = Array.FindIndex(SpeedSteps, s => Math.Abs(s - _speed) < 0.001f);
        if (idx < 0) idx = Array.IndexOf(SpeedSteps, 1.0f);
        var next = SpeedSteps[Math.Clamp(idx + direction, 0, SpeedSteps.Length - 1)];
        SetSpeed(next);
    }

    private void ParseAndSetSpeed(object? parameter)
    {
        if (parameter is float f) { SetSpeed(f); return; }
        if (parameter is string s &&
            float.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
            SetSpeed(v);
    }

    private static bool IsSubtitleFile(string path) =>
        SubtitleExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static string? FindMatchingSubtitleFile(string mediaPath)    {
        var directory = Path.GetDirectoryName(mediaPath);
        var name = Path.GetFileNameWithoutExtension(mediaPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(name))
            return null;

        foreach (var extension in SubtitleExtensions)
        {
            var exact = Path.Combine(directory, name + extension);
            if (File.Exists(exact))
                return exact;
        }

        try
        {
            return Directory.EnumerateFiles(directory)
                .Where(path =>
                    SubtitleExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) &&
                    Path.GetFileNameWithoutExtension(path).StartsWith(name + ".", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path.Length)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private void ToggleLoop()
    {
        _playback.ToggleLoop();
        ShowOsd(_playback.IsLooping ? "Loop: On" : "Loop: Off");
    }

    private void TakeScreenshot()
    {
        if (!HasMedia) return;
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        Directory.CreateDirectory(dir);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var path = Path.Combine(dir, $"Lumyn_{timestamp}.png");
        if (_playback.TakeSnapshot(path))
            ShowOsd($"Screenshot saved");
        else
            ShowOsd("Screenshot failed");
    }

    private void SetAudioTrack(object? parameter)
    {
        if (parameter is not int id) return;
        _playback.SetAudioTrack(id);
        var name = _audioTracks.FirstOrDefault(t => t.Id == id)?.Name ?? id.ToString();
        ShowOsd($"Audio: {name}");
    }

    private void SetSubtitleTrack(object? parameter)
    {
        if (parameter is not int id) return;
        _playback.SetSubtitleTrack(id);
        if (id < 0 || _useSubtitleOverlay)
        {
            _useSubtitleOverlay = false;
            _subtitleLines = [];
            CurrentSubtitleText = null;
        }
        CurrentSubtitleSettings = CurrentSubtitleSettings with
        {
            FilePath = null,
            EmbeddedTrackId = id < 0 ? null : id
        };
        var name = _subtitleTracks.FirstOrDefault(t => t.Id == id)?.Name ?? (id < 0 ? "Off" : id.ToString());
        ShowOsd($"Subtitle: {name}");
    }

    private void CycleAudioTrack()
    {
        _playback.CycleAudioTrack();
        RefreshTracks();
        var id = _playback.CurrentAudioTrack;
        var name = _audioTracks.FirstOrDefault(t => t.Id == id)?.Name ?? id.ToString();
        ShowOsd($"Audio: {name}");
    }

    private void CycleSubtitleTrack()
    {
        _playback.CycleSubtitleTrack();
        RefreshTracks();
        var id = _playback.CurrentSubtitleTrack;
        if (id < 0 || _useSubtitleOverlay)
        {
            _useSubtitleOverlay = false;
            _subtitleLines = [];
            CurrentSubtitleText = null;
        }
        CurrentSubtitleSettings = CurrentSubtitleSettings with
        {
            FilePath = null,
            EmbeddedTrackId = id < 0 ? null : id
        };
        var name = _subtitleTracks.FirstOrDefault(t => t.Id == id)?.Name ?? (id < 0 ? "Off" : id.ToString());
        ShowOsd($"Subtitle: {name}");
    }

    public void RefreshTracksNow() => RefreshTracks();

    private void RefreshTracks()
    {
        AudioTracks = [.. _playback.GetAudioTracks()
            .Select(t => new TrackInfo(t.Id, string.IsNullOrWhiteSpace(t.Name) ? $"Track {t.Id}" : t.Name, t.IsSelected))];

        SubtitleTracks = [.. _playback.GetSubtitleTracks()
            .Select(t => new TrackInfo(t.Id, string.IsNullOrWhiteSpace(t.Name) ? (t.Id < 0 ? "Off" : $"Track {t.Id}") : t.Name, t.IsSelected))];

        ChapterPositions = _playback.GetChapterPositions();
    }

    private void RefreshTracksIfNeeded()
    {
        var revision = _playback.TrackRevision;
        if (revision == _lastTrackRevision)
            return;

        _lastTrackRevision = revision;
        RefreshTracks();
    }

    private static string FormatTime(TimeSpan value)
    {
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;
        return value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss")
            : value.ToString(@"mm\:ss");
    }

    // ── Audio mode helpers ────────────────────────────────────────────────────

    private void SetPlaylistFromFolder(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(dir))
        {
            SetPlaylist([filePath], 0);
            return;
        }

        try
        {
            var files = Directory.EnumerateFiles(dir)
                .Where(f => AudioExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0) files = [filePath];
            var idx = files.FindIndex(f => string.Equals(f, filePath, StringComparison.OrdinalIgnoreCase));
            SetPlaylist(files, Math.Max(0, idx));
        }
        catch
        {
            SetPlaylist([filePath], 0);
        }
    }

    private static Avalonia.Media.Imaging.Bitmap? FindCoverArt(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(dir)) return null;

        // 1. Same name as the audio file with an image extension.
        var baseName = Path.GetFileNameWithoutExtension(filePath);
        foreach (var imgExt in new[] { ".jpg", ".jpeg", ".png", ".webp" })
        {
            var candidate = Path.Combine(dir, baseName + imgExt);
            if (File.Exists(candidate)) return TryLoadBitmap(candidate);
        }

        // 2. Well-known cover art file names in the same folder.
        foreach (var name in CoverArtNames)
        {
            foreach (var imgExt in new[] { ".jpg", ".jpeg", ".png", ".webp" })
            {
                var candidate = Path.Combine(dir, name + imgExt);
                if (File.Exists(candidate)) return TryLoadBitmap(candidate);
            }
        }

        return null;
    }

    private static Avalonia.Media.Imaging.Bitmap? TryLoadBitmap(string path)
    {
        try { return new Avalonia.Media.Imaging.Bitmap(path); }
        catch { return null; }
    }

    private async Task NavigateTrackAsync(int delta)
    {
        if (_playlist.Count <= 1) return;
        var next = _playlistIndex + delta;
        if (next < 0 || next >= _playlist.Count) return;
        _playlistIndex = next;
        await OpenPlaylistIndexInternalAsync(_playlistIndex);
    }

    private async Task PlayFromIndexAsync(int index)
    {
        if (index < 0 || index >= _playlist.Count) return;
        _playlistIndex = index;
        await OpenPlaylistIndexInternalAsync(_playlistIndex);
    }

    /// <summary>
    /// Core: opens the file at <paramref name="index"/> in the playlist, updating audio metadata.
    /// Does NOT modify <c>_playlist</c> or <c>_playlistIndex</c>.
    /// </summary>
    private async Task OpenPlaylistIndexInternalAsync(int index)
    {
        var filePath = _playlist[index];
        SaveResumePosition();
        ErrorMessage = null;

        var ext = Path.GetExtension(filePath);
        var audioOnly = AudioExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        IsAudioOnly = audioOnly;
        OnPropertyChanged(nameof(IsAudioMode));

        CoverArtBitmap = audioOnly ? FindCoverArt(filePath) : null;
        TrackTitle = null; TrackArtist = null; TrackAlbum = null;

        RebuildPlaylistItems();
        NotifyPlaylistState();

        var resume = _settings.GetResumePosition(filePath);
        await _playback.OpenAsync(filePath, resume);
        _settings.AddRecentFile(filePath);

        var fileName = Path.GetFileName(filePath);
        Title = string.IsNullOrWhiteSpace(fileName) ? "Lumyn" : $"{fileName} - Lumyn";
        ShowOsd(fileName ?? "");
        RefreshState();

        var cached = _settings.GetSubtitleSettings(filePath);
        if (cached is not null)
        {
            var restored = SubtitleSettingsFromEntry(cached);
            CurrentSubtitleSettings = restored;
            await Task.Delay(400);
            await ApplySubtitleSettingsAsync(restored, saveToCache: false);
        }
        else
        {
            CurrentSubtitleSettings = new SubtitleSettings(
                null, SubtitleFontSize.Medium, SubtitleFont.SansSerif, SubtitleColor.White, 0);
            var matchingSubtitle = FindMatchingSubtitleFile(filePath);
            if (!string.IsNullOrWhiteSpace(matchingSubtitle))
            {
                await Task.Delay(400);
                await LoadSubtitleFileAsync(matchingSubtitle);
            }
        }

        if (audioOnly)
        {
            await Task.Delay(600);
            TrackTitle  = _playback.GetMetadata("title")  ?? Path.GetFileNameWithoutExtension(filePath);
            TrackArtist = _playback.GetMetadata("artist");
            TrackAlbum  = _playback.GetMetadata("album");
        }
    }

    private void RemoveFromPlaylist(int index)
    {
        if (index < 0 || index >= _playlist.Count) return;
        _playlist.RemoveAt(index);
        if (_playlistIndex >= _playlist.Count)
            _playlistIndex = Math.Max(0, _playlist.Count - 1);
        RebuildPlaylistItems();
        NotifyPlaylistState();
    }

    public void MovePlaylistItem(int from, int to)
    {
        if (from == to || from < 0 || to < 0 ||
            from >= _playlist.Count || to >= _playlist.Count) return;
        var item = _playlist[from];
        _playlist.RemoveAt(from);
        _playlist.Insert(to, item);
        // Adjust the currently-playing index to follow the moved track.
        if (_playlistIndex == from)
            _playlistIndex = to;
        else if (from < _playlistIndex && to >= _playlistIndex)
            _playlistIndex--;
        else if (from > _playlistIndex && to <= _playlistIndex)
            _playlistIndex++;
        RebuildPlaylistItems();
        NotifyPlaylistState();
    }

    private void ClearPlaylist()
    {
        _playlist.Clear();
        _playlistIndex = -1;
        RebuildPlaylistItems();
        NotifyPlaylistState();
    }

    private void SetPlaylist(IEnumerable<string> files, int activeIndex)
    {
        _playlist = [.. files];
        _playlistIndex = _playlist.Count > 0 ? Math.Clamp(activeIndex, 0, _playlist.Count - 1) : -1;
        RebuildPlaylistItems();
    }

    private void RebuildPlaylistItems()
    {
        PlaylistItems = _playlist.Count == 0
            ? []
            : [.. _playlist.Select((p, i) => new PlaylistItem(i, p, i == _playlistIndex))];
        OnPropertyChanged(nameof(PlaylistItems));
        OnPropertyChanged(nameof(PlaylistCount));
    }

    private void NotifyPlaylistState()
    {
        OnPropertyChanged(nameof(HasPlaylist));
        OnPropertyChanged(nameof(HasFolderTracks));
        OnPropertyChanged(nameof(PlaylistPositionLabel));
        OnPropertyChanged(nameof(FolderTrackLabel));
        OnPropertyChanged(nameof(HasPreviousTrack));
        OnPropertyChanged(nameof(HasNextTrack));
    }


    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    // Helper to format speed label in OSD without exposing a property
    private readonly struct SpeedDisplay(float speed)
    {
        public override string ToString() => Math.Abs(speed - 1.0f) < 0.001f ? "1×" : $"{speed:0.##}×";
    }

    private sealed class RelayCommand(Action<object?> execute) : ICommand
    {
#pragma warning disable CS0067
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute(parameter);
    }
}
