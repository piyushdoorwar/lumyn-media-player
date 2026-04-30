using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Avalonia.Threading;
using LibVLCSharp.Shared;
using Lumyn.App.Models;
using Lumyn.Core.Models;
using Lumyn.Core.Services;

namespace Lumyn.App.ViewModels;

public sealed record TrackInfo(int Id, string Name);

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly float[] SpeedSteps = [0.25f, 0.5f, 0.75f, 1.0f, 1.25f, 1.5f, 2.0f, 3.0f, 4.0f];

    private readonly PlaybackService _playback;
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _osdTimer;

    private string _title = "Lumyn";
    private string _timeText = "00:00 / 00:00";
    private string? _errorMessage;
    private double _seekValue;
    private int _volume = 80;
    private bool _isPlaying;
    private bool _isMuted;
    private bool _controlsVisible = true;
    private bool _isSeeking;
    private long _lastSeekTickMs;          // Interlocked, throttles live-drag seeks
    private const long SeekThrottleMs = 150;
    private float _speed = 1.0f;
    private bool _isLooping;
    private bool _isAlwaysOnTop;
    private string? _osdMessage;
    private TrackInfo[] _audioTracks = [];
    private TrackInfo[] _subtitleTracks = [];
    private string? _currentSubtitleText;

    // ── Subtitle overlay (Avalonia-rendered, replaces VLC's --no-spu path) ───
    private List<Lumyn.Core.Services.SubtitleLine> _subtitleLines = [];

    // Video frame double-buffering: VLC decode thread writes into _stagingBuffer,
    // UI thread reads from it. _frameReady acts as a cheap lock-free "new frame" flag.
    private byte[]? _stagingBuffer;
    private int _stagingWidth, _stagingHeight, _stagingPitch;
    private int _frameReady; // 0 = idle, 1 = new frame available (Interlocked)
    private readonly object _stagingLock = new();

    /// <summary>
    /// Set by the View to route decoded frames to <c>VideoSurface.PushFrame</c>.
    /// Called on the UI thread after staging copy is complete.
    /// </summary>
    public Action<byte[], int, int, int>? PushVideoFrame { get; set; }

    public MainViewModel(PlaybackService playback, SettingsService settings)
    {
        _playback = playback;
        _settings = settings;

        _osdTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _osdTimer.Tick += (_, _) =>
        {
            _osdTimer.Stop();
            OsdMessage = null;
        };

        _playback.StateChanged += (_, _) => Dispatcher.UIThread.InvokeAsync(RefreshState);
        _playback.EndReached += (_, _) => Dispatcher.UIThread.InvokeAsync(() =>
        {
            _settings.ClearResumePosition(CurrentFilePath);
            RefreshState();
        });
        _playback.ErrorOccurred += (_, message) =>
            Dispatcher.UIThread.InvokeAsync(() => ErrorMessage = message);

        _playback.VideoFrameReady += OnVideoFrameReady;

        TogglePlayPauseCommand = new RelayCommand(_ => _playback.TogglePlayPause());
        StopCommand            = new RelayCommand(_ => Stop());
        ToggleMuteCommand      = new RelayCommand(_ => ToggleMute());
        SeekBackwardCommand    = new RelayCommand(_ => _playback.SeekRelative(TimeSpan.FromSeconds(-5)));
        SeekForwardCommand     = new RelayCommand(_ => _playback.SeekRelative(TimeSpan.FromSeconds(5)));
        SeekBackward30Command  = new RelayCommand(_ => _playback.SeekRelative(TimeSpan.FromSeconds(-30)));
        SeekForward30Command   = new RelayCommand(_ => _playback.SeekRelative(TimeSpan.FromSeconds(30)));
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

        ErrorMessage = _playback.InitializationError;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // ── Read-only state ──────────────────────────────────────────────────────

    public MediaPlayer? Player => _playback.MediaPlayer;
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

    public double SeekValue
    {
        get => _seekValue;
        set
        {
            if (SetField(ref _seekValue, Math.Clamp(value, 0, 1000)) && _isSeeking)
            {
                // Throttle live-drag seeks so we don't flood VLC with hundreds of
                // consecutive Seek() calls. EndSeek() always does the final accurate seek.
                var nowMs = Environment.TickCount64;
                var lastMs = Interlocked.Read(ref _lastSeekTickMs);
                if (nowMs - lastMs >= SeekThrottleMs)
                {
                    Interlocked.Exchange(ref _lastSeekTickMs, nowMs);
                    var duration = _playback.Duration;
                    if (duration > TimeSpan.Zero)
                        _playback.Seek(TimeSpan.FromMilliseconds(duration.TotalMilliseconds * (_seekValue / 1000.0)));
                }
            }
        }
    }

    public int Volume
    {
        get => _volume;
        set
        {
            var next = Math.Clamp(value, 0, 100);
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

    public string SpeedLabel => Math.Abs(_speed - 1.0f) < 0.001f ? "1×" : $"{_speed:0.##}×";

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
    public ICommand SetSpeedCommand { get; }

    // ── Public methods ───────────────────────────────────────────────────────

    public async Task OpenFileAsync(string filePath)
    {
        SaveResumePosition();
        ErrorMessage = null;

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
            // Small delay so VLC has time to initialise the media before we add the slave
            await Task.Delay(400);
            await ApplySubtitleSettingsAsync(restored, saveToCache: false);
        }
        else
        {
            // Reset to defaults so dialog opens clean for a new file
            CurrentSubtitleSettings = new SubtitleSettings(
                null, SubtitleFontSize.Medium, SubtitleFont.SansSerif, SubtitleColor.White, 0);
        }
    }

    public async Task LoadSubtitleFileAsync(string path)
    {
        // Parse off the UI thread so large SRT files don't freeze the window.
        var lines = await Task.Run(() => Lumyn.Core.Services.SubtitleParser.Parse(path));
        _subtitleLines = lines;

        // VLC slave registration (fast, no I/O) + OSD — back on UI thread.
        _playback.LoadSubtitleFile(path);
        ShowOsd($"Subtitle: {Path.GetFileName(path)}");
    }

    /// <summary>
    /// Persists the last subtitle settings so the dialog re-opens with the previous values.
    /// </summary>
    public SubtitleSettings CurrentSubtitleSettings { get; private set; } =
        new(null, SubtitleFontSize.Medium, SubtitleFont.SansSerif, SubtitleColor.White, 0);

    /// <summary>
    /// Applies subtitle settings from the dialog: loads a file if selected and
    /// adjusts the sync delay immediately via VLC's native API.
    /// </summary>
    public Task ApplySubtitleSettingsAsync(SubtitleSettings s) => ApplySubtitleSettingsAsync(s, saveToCache: true);

    private async Task ApplySubtitleSettingsAsync(SubtitleSettings s, bool saveToCache)
    {
        CurrentSubtitleSettings = s;

        if (s.FilePath is null)
        {
            // Disable subtitles: clear parsed lines + reset delay
            _subtitleLines = [];
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
            if (s.FilePath is null)
                _settings.ClearSubtitleSettings(_playback.CurrentFilePath);
            else
                _settings.SaveSubtitleSettings(_playback.CurrentFilePath, SubtitleEntryFromSettings(s));
        }
    }

    // ── Subtitle settings ↔ SettingsEntry conversion ──────────────────────

    private static SubtitleSettings SubtitleSettingsFromEntry(SubtitleEntry e) => new(
        e.FilePath,
        Enum.TryParse<SubtitleFontSize>(e.FontSize, out var sz) ? sz : SubtitleFontSize.Medium,
        Enum.TryParse<SubtitleFont>(e.Font,     out var fn) ? fn : SubtitleFont.SansSerif,
        Enum.TryParse<SubtitleColor>(e.Color,   out var cl) ? cl : SubtitleColor.White,
        e.DelayMs);

    private static SubtitleEntry SubtitleEntryFromSettings(SubtitleSettings s) => new()
    {
        FilePath = s.FilePath,
        FontSize = s.FontSize.ToString(),
        Font     = s.Font.ToString(),
        Color    = s.Color.ToString(),
        DelayMs  = s.DelayMs
    };

    public void JumpTo(TimeSpan position)
    {
        _playback.Seek(position);
    }

    public void BeginSeek() => _isSeeking = true;

    public void EndSeek()
    {
        _isSeeking = false;
        var duration = _playback.Duration;
        if (duration > TimeSpan.Zero)
            _playback.Seek(TimeSpan.FromMilliseconds(duration.TotalMilliseconds * (_seekValue / 1000.0)));
    }

    public void RefreshState()
    {
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
        OnPropertyChanged(nameof(RecentFiles));
        RefreshTracks();

        // Update Avalonia subtitle overlay text.
        if (_subtitleLines.Count > 0 && state.IsPlaying)
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
        _playback.Dispose();
    }

    // ── Video frame rendering ────────────────────────────────────────────────

    // Called on VLC decode thread — copy pixels to staging buffer, never touch bitmap here
    private void OnVideoFrameReady(object? sender, VideoFrameData frame)
    {
        var needed = (int)(frame.Pitch * frame.Height);
        lock (_stagingLock)
        {
            if (_stagingBuffer is null || _stagingBuffer.Length < needed ||
                _stagingWidth != (int)frame.Width || _stagingHeight != (int)frame.Height)
            {
                _stagingBuffer = new byte[needed];
                _stagingWidth  = (int)frame.Width;
                _stagingHeight = (int)frame.Height;
                _stagingPitch  = (int)frame.Pitch;
            }
            System.Runtime.InteropServices.Marshal.Copy(frame.Buffer, _stagingBuffer, 0, needed);
        }
        // Signal that a new frame is staged; if a signal was already pending we skip
        // posting again — the UI will pick it up on the next render tick
        if (Interlocked.Exchange(ref _frameReady, 1) == 0)
            Dispatcher.UIThread.Post(FlushVideoFrame, DispatcherPriority.Render);
    }

    // Called on UI thread — copies staged pixels and pushes to VideoSurface
    private void FlushVideoFrame()
    {
        Interlocked.Exchange(ref _frameReady, 0);

        byte[] snapshot;
        int w, h, pitch;
        lock (_stagingLock)
        {
            if (_stagingBuffer is null) return;
            // Copy staging data while holding the lock so the VLC thread
            // cannot overwrite it mid-read.
            w     = _stagingWidth;
            h     = _stagingHeight;
            pitch = _stagingPitch;
            snapshot = new byte[pitch * h];
            System.Array.Copy(_stagingBuffer, snapshot, pitch * h);
        }

        PushVideoFrame?.Invoke(snapshot, w, h, pitch);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private void Stop()
    {
        SaveResumePosition();
        _playback.Stop();
        Title = "Lumyn";
        RefreshState();
    }

    private void ToggleMute()
    {
        _playback.ToggleMute();
        ShowOsd(_playback.IsMuted ? "Muted" : $"Volume: {_playback.Volume}%");
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
        var name = _subtitleTracks.FirstOrDefault(t => t.Id == id)?.Name ?? (id < 0 ? "Off" : id.ToString());
        ShowOsd($"Subtitle: {name}");
    }

    private void RefreshTracks()
    {
        AudioTracks = [.. _playback.GetAudioTracks()
            .Select(t => new TrackInfo(t.Id, string.IsNullOrWhiteSpace(t.Name) ? $"Track {t.Id}" : t.Name))];

        SubtitleTracks = [.. _playback.GetSubtitleTracks()
            .Select(t => new TrackInfo(t.Id, string.IsNullOrWhiteSpace(t.Name) ? (t.Id < 0 ? "Off" : $"Track {t.Id}") : t.Name))];
    }

    private static string FormatTime(TimeSpan value)
    {
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;
        return value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss")
            : value.ToString(@"mm\:ss");
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
