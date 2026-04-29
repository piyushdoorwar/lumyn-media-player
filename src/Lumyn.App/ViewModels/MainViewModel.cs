using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using LibVLCSharp.Shared;
using Lumyn.Core.Services;

namespace Lumyn.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly PlaybackService _playback;
    private readonly SettingsService _settings;
    private string _title = "Lumyn";
    private string _timeText = "00:00 / 00:00";
    private string? _errorMessage;
    private double _seekValue;
    private int _volume = 80;
    private bool _isPlaying;
    private bool _isMuted;
    private bool _controlsVisible = true;
    private bool _isSeeking;

    public MainViewModel(PlaybackService playback, SettingsService settings)
    {
        _playback = playback;
        _settings = settings;
        _playback.StateChanged += (_, _) => RefreshState();
        _playback.EndReached += (_, _) => _settings.ClearResumePosition(CurrentFilePath);
        _playback.ErrorOccurred += (_, message) => ErrorMessage = message;

        TogglePlayPauseCommand = new RelayCommand(_ => _playback.TogglePlayPause());
        ToggleMuteCommand = new RelayCommand(_ => _playback.ToggleMute());
        SeekBackwardCommand = new RelayCommand(_ => _playback.SeekRelative(TimeSpan.FromSeconds(-5)));
        SeekForwardCommand = new RelayCommand(_ => _playback.SeekRelative(TimeSpan.FromSeconds(5)));
        VolumeUpCommand = new RelayCommand(_ => Volume += 5);
        VolumeDownCommand = new RelayCommand(_ => Volume -= 5);
        ErrorMessage = _playback.InitializationError;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MediaPlayer? Player => _playback.MediaPlayer;

    public string? CurrentFilePath => _playback.CurrentFilePath;

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
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasMedia => !string.IsNullOrWhiteSpace(CurrentFilePath);

    public double SeekValue
    {
        get => _seekValue;
        set
        {
            if (SetField(ref _seekValue, Math.Clamp(value, 0, 1000)) && _isSeeking)
            {
                var duration = _playback.Duration;
                if (duration > TimeSpan.Zero)
                {
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
            {
                _playback.SetVolume(next);
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
        private set
        {
            if (SetField(ref _isMuted, value))
            {
                OnPropertyChanged(nameof(MuteButtonText));
            }
        }
    }

    public bool ControlsVisible
    {
        get => _controlsVisible;
        set => SetField(ref _controlsVisible, value);
    }

    public string PlayPauseText => IsPlaying ? "Pause" : "Play";

    public string MuteButtonText => IsMuted ? "Unmute" : "Mute";

    public ICommand TogglePlayPauseCommand { get; }
    public ICommand ToggleMuteCommand { get; }
    public ICommand SeekBackwardCommand { get; }
    public ICommand SeekForwardCommand { get; }
    public ICommand VolumeUpCommand { get; }
    public ICommand VolumeDownCommand { get; }

    public async Task OpenFileAsync(string filePath)
    {
        SaveResumePosition();
        ErrorMessage = null;

        var resume = _settings.GetResumePosition(filePath);
        await _playback.OpenAsync(filePath, resume);

        var fileName = Path.GetFileName(filePath);
        Title = string.IsNullOrWhiteSpace(fileName) ? "Lumyn" : $"{fileName} - Lumyn";
        RefreshState();
    }

    public void BeginSeek()
    {
        _isSeeking = true;
    }

    public void EndSeek()
    {
        _isSeeking = false;
        var duration = _playback.Duration;
        if (duration > TimeSpan.Zero)
        {
            _playback.Seek(TimeSpan.FromMilliseconds(duration.TotalMilliseconds * (_seekValue / 1000.0)));
        }
    }

    public void RefreshState()
    {
        var state = _playback.Snapshot();
        IsPlaying = state.IsPlaying;
        IsMuted = state.IsMuted;

        if (_volume != state.Volume)
        {
            _volume = state.Volume;
            OnPropertyChanged(nameof(Volume));
        }

        if (!_isSeeking && state.Duration > TimeSpan.Zero)
        {
            _seekValue = Math.Clamp(state.Position.TotalMilliseconds / state.Duration.TotalMilliseconds * 1000.0, 0, 1000);
            OnPropertyChanged(nameof(SeekValue));
        }

        TimeText = $"{FormatTime(state.Position)} / {FormatTime(state.Duration)}";
        OnPropertyChanged(nameof(PlayPauseText));
        OnPropertyChanged(nameof(CurrentFilePath));
        OnPropertyChanged(nameof(HasMedia));
    }

    public void SaveResumePosition()
    {
        _settings.SaveResumePosition(CurrentFilePath, _playback.Position, _playback.Duration);
    }

    public void Dispose()
    {
        SaveResumePosition();
        _playback.Dispose();
    }

    private static string FormatTime(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        return value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss")
            : value.ToString(@"mm\:ss");
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class RelayCommand(Action<object?> execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute(parameter);
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
