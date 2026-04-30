using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Lumyn.App.Models;
using Lumyn.Core.Models;
using Lumyn.Core.Services;

namespace Lumyn.App.ViewModels;

public sealed record TrackInfo(int Id, string Name, bool IsSelected = false);

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
    private readonly DispatcherTimer _osdTimer;
    private int _stateRefreshQueued;
    private long _lastTrackRevision = -1;

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
    private string? _osdMessage;
    private TrackInfo[] _audioTracks = [];
    private TrackInfo[] _subtitleTracks = [];
    private string? _currentSubtitleText;

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
    private string[] _folderTracks = [];
    private int _folderTrackIndex = -1;

    public MainViewModel(PlaybackService playback, SettingsService settings)
    {
        _playback = playback;
        _settings = settings;

        _osdTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _osdTimer.Tick += (_, _) =>
        {
            _osdTimer.Stop();
            OsdMessage = null;
        };

        _playback.StateChanged += (_, _) => QueueRefreshState();
        _playback.EndReached += (_, _) => Dispatcher.UIThread.InvokeAsync(() =>
        {
            _settings.ClearResumePosition(CurrentFilePath);
            RefreshState();
        });
        _playback.ErrorOccurred += (_, message) =>
            Dispatcher.UIThread.InvokeAsync(() => ErrorMessage = message);

        TogglePlayPauseCommand = new RelayCommand(_ => _playback.TogglePlayPause());
        StopCommand            = new RelayCommand(_ => Stop());
        ToggleMuteCommand      = new RelayCommand(_ => ToggleMute());
        SeekBackwardCommand    = new RelayCommand(_ => SeekRelative(-5));
        SeekForwardCommand     = new RelayCommand(_ => SeekRelative(5));
        SeekBackward30Command  = new RelayCommand(_ => SeekRelative(-30));
        SeekForward30Command   = new RelayCommand(_ => SeekRelative(30));
        VolumeUpCommand        = new RelayCommand(_ => { Volume += 5; ShowOsd($"Volume: {Volume}%"); });
        VolumeDownCommand      = new RelayCommand(_ => { Volume -= 5; ShowOsd($"Volume: {Volume}%"); });
        SpeedUpCommand         = new RelayCommand(_ => AdjustSpeed(+1));
        SpeedDownCommand       = new RelayCommand(_ => AdjustSpeed(-1));
        ResetSpeedCommand      = new RelayCommand(_ => SetSpeed(1.0f));
        ToggleLoopCommand      = new RelayCommand(_ => ToggleLoop());
        StepFrameCommand       = new RelayCommand(_ => _playback.StepFrame());
        TakeScreenshotCommand  = new RelayCommand(_ => TakeScreenshot());
        ToggleAlwaysOnTopCommand = new RelayCommand(_ => IsAlwaysOnTop = !IsAlwaysOnTop);
        SetAudioTrackCommand   = new RelayCommand(p => SetAudioTrack(p));
        SetSubtitleTrackCommand = new RelayCommand(p => SetSubtitleTrack(p));
        CycleAudioTrackCommand = new RelayCommand(_ => CycleAudioTrack());
        CycleSubtitleTrackCommand = new RelayCommand(_ => CycleSubtitleTrack());
        SetSpeedCommand        = new RelayCommand(p => ParseAndSetSpeed(p));
        PreviousTrackCommand   = new RelayCommand(_ => { _ = NavigateTrackAsync(-1); });
        NextTrackCommand       = new RelayCommand(_ => { _ = NavigateTrackAsync(+1); });

        _playback.EndReached += (_, _) => Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Auto-advance to next track in audio mode when not looping.
            if (_isAudioOnly && !_playback.IsLooping && HasNextTrack)
                _ = NavigateTrackAsync(+1);
        });

        ErrorMessage = _playback.InitializationError;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // ── Read-only state ──────────────────────────────────────────────────────

    public PlaybackService Playback => _playback;
    public string? CurrentFilePath => _playback.CurrentFilePath;
    public IReadOnlyList<string> RecentFiles => _settings.RecentFiles;

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

    public bool HasFolderTracks => _folderTracks.Length > 1;

    public string FolderTrackLabel => _folderTracks.Length > 1
        ? $"{_folderTrackIndex + 1} / {_folderTracks.Length}"
        : string.Empty;

    public bool HasPreviousTrack => _folderTracks.Length > 1 && _folderTrackIndex > 0;
    public bool HasNextTrack     => _folderTracks.Length > 1 && _folderTrackIndex < _folderTracks.Length - 1;

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
                _playback.SetVolume(next);
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
    public ICommand TakeScreenshotCommand { get; }
    public ICommand ToggleAlwaysOnTopCommand { get; }
    public ICommand SetAudioTrackCommand { get; }
    public ICommand SetSubtitleTrackCommand { get; }
    public ICommand CycleAudioTrackCommand { get; }
    public ICommand CycleSubtitleTrackCommand { get; }
    public ICommand SetSpeedCommand { get; }    public ICommand PreviousTrackCommand { get; }
    public ICommand NextTrackCommand { get; }
    // ── Public methods ───────────────────────────────────────────────────────

    public void PausePlayback() => _playback.Pause();

    public void ResumePlayback() => _playback.Play();

    public async Task OpenFileAsync(string filePath)
    {
        SaveResumePosition();
        ErrorMessage = null;

        // ── Audio mode setup ────────────────────────────────────────────────
        var ext = Path.GetExtension(filePath);
        var audioOnly = AudioExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        IsAudioOnly = audioOnly;
        OnPropertyChanged(nameof(IsAudioMode));

        if (audioOnly)
        {
            LoadFolderTracks(filePath);
            CoverArtBitmap = FindCoverArt(filePath);
            // Clear metadata until mpv loads the file.
            TrackTitle  = null;
            TrackArtist = null;
            TrackAlbum  = null;
        }
        else
        {
            _folderTracks     = [];
            _folderTrackIndex = -1;
            CoverArtBitmap    = null;
            TrackTitle        = null;
            TrackArtist       = null;
            TrackAlbum        = null;
        }

        NotifyFolderNavState();
        // ────────────────────────────────────────────────────────────────────

        var resume = _settings.GetResumePosition(filePath);
        await _playback.OpenAsync(filePath, resume);
        _settings.AddRecentFile(filePath);

        var fileName = Path.GetFileName(filePath);
        Title = string.IsNullOrWhiteSpace(fileName) ? "Lumyn" : $"{fileName} - Lumyn";
        ShowOsd(Path.GetFileName(filePath) ?? "");
        RefreshState();

        // Restore cached subtitle settings for this file (if any)
        var cached = _settings.GetSubtitleSettings(filePath);
        if (cached is not null)
        {
            var restored = SubtitleSettingsFromEntry(cached);
            CurrentSubtitleSettings = restored;
            // Small delay so mpv has time to initialise media tracks before we restore subtitles.
            await Task.Delay(400);
            await ApplySubtitleSettingsAsync(restored, saveToCache: false);
        }
        else
        {
            // Reset to defaults so dialog opens clean for a new file
            CurrentSubtitleSettings = new SubtitleSettings(
                null, SubtitleFontSize.Medium, SubtitleFont.SansSerif, SubtitleColor.White, 0);

            var matchingSubtitle = FindMatchingSubtitleFile(filePath);
            if (!string.IsNullOrWhiteSpace(matchingSubtitle))
            {
                await Task.Delay(400);
                await LoadSubtitleFileAsync(matchingSubtitle);
            }
        }

        // Read ID3/metadata tags after mpv has loaded the file.
        if (audioOnly)
        {
            await Task.Delay(600);
            TrackTitle  = _playback.GetMetadata("title")  ?? Path.GetFileNameWithoutExtension(filePath);
            TrackArtist = _playback.GetMetadata("artist");
            TrackAlbum  = _playback.GetMetadata("album");
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
        var duration = _playback.Duration;
        if (duration > TimeSpan.Zero)
            _playback.Seek(TimeSpan.FromMilliseconds(duration.TotalMilliseconds * (_seekValue / 1000.0)));
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
        IsPlaying = state.IsPlaying;
        IsMuted = state.IsMuted;
        Speed = state.Speed;
        IsLooping = state.IsLooping;

        if (_volume != state.Volume)
        {
            _volume = state.Volume;
            OnPropertyChanged(nameof(Volume));
        }

        if (!_isSeeking && state.Duration > TimeSpan.Zero)
        {
            _seekValue = Math.Clamp(
                state.Position.TotalMilliseconds / state.Duration.TotalMilliseconds * 1000.0,
                0, 1000);
            OnPropertyChanged(nameof(SeekValue));
        }

        TimeText = $"{FormatTime(state.Position)} / {FormatTime(state.Duration)}";
        OnPropertyChanged(nameof(CurrentFilePath));
        OnPropertyChanged(nameof(HasMedia));
        OnPropertyChanged(nameof(IsAudioMode));
        OnPropertyChanged(nameof(RecentFiles));
        NotifyFolderNavState();
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
        _playback.Dispose();
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private void Stop()
    {
        SaveResumePosition();
        _playback.Stop();
        Title = "Lumyn";
        IsAudioOnly       = false;
        TrackTitle        = null;
        TrackArtist       = null;
        TrackAlbum        = null;
        CoverArtBitmap    = null;
        _folderTracks     = [];
        _folderTrackIndex = -1;
        NotifyFolderNavState();
        RefreshState();
    }

    private void ToggleMute()
    {
        _playback.ToggleMute();
        ShowOsd(_playback.IsMuted ? "Muted" : $"Volume: {_playback.Volume}%");
    }

    private void SeekRelative(int seconds)
    {
        _playback.SeekRelative(TimeSpan.FromSeconds(seconds));
        ShowOsd(seconds < 0 ? $"Rewind {Math.Abs(seconds)}s" : $"Forward {seconds}s");
    }

    private void SetSpeed(float rate)
    {
        _playback.SetSpeed(rate);
        Speed = rate;
        ShowOsd($"Speed: {new MainViewModel.SpeedDisplay(rate)}");
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

    private static string? FindMatchingSubtitleFile(string mediaPath)
    {
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

    private void LoadFolderTracks(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(dir))
        {
            _folderTracks     = [filePath];
            _folderTrackIndex = 0;
            return;
        }

        try
        {
            var files = Directory.EnumerateFiles(dir)
                .Where(f => AudioExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToArray();

            _folderTracks     = files.Length > 0 ? files : [filePath];
            _folderTrackIndex = Array.FindIndex(_folderTracks,
                f => string.Equals(f, filePath, StringComparison.OrdinalIgnoreCase));
            if (_folderTrackIndex < 0) _folderTrackIndex = 0;
        }
        catch
        {
            _folderTracks     = [filePath];
            _folderTrackIndex = 0;
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
        if (_folderTracks.Length <= 1) return;
        var next = _folderTrackIndex + delta;
        if (next < 0 || next >= _folderTracks.Length) return;
        await OpenFileAsync(_folderTracks[next]);
    }

    private void NotifyFolderNavState()
    {
        OnPropertyChanged(nameof(HasFolderTracks));
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
