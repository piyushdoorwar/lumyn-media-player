using LibVLCSharp.Shared;
using Lumyn.Core.Models;

namespace Lumyn.Core.Services;

public sealed class PlaybackService : IDisposable
{
    private readonly LibVLC? _libVlc;
    private readonly MediaState _state = new();
    private bool _disposed;

    public PlaybackService()
    {
        try
        {
            // Linux resolves libvlc from system packages such as vlc/libvlc-dev.
            LibVLCSharp.Shared.Core.Initialize();

            _libVlc = new LibVLC("--no-video-title-show");
            MediaPlayer = new MediaPlayer(_libVlc)
            {
                Volume = _state.Volume
            };

            MediaPlayer.Playing += (_, _) =>
            {
                _state.IsPlaying = true;
                StateChanged?.Invoke(this, _state);
            };
            MediaPlayer.Paused += (_, _) =>
            {
                _state.IsPlaying = false;
                StateChanged?.Invoke(this, _state);
            };
            MediaPlayer.Stopped += (_, _) =>
            {
                _state.IsPlaying = false;
                StateChanged?.Invoke(this, _state);
            };
            MediaPlayer.EndReached += (_, _) => EndReached?.Invoke(this, EventArgs.Empty);
            MediaPlayer.EncounteredError += (_, _) => ErrorOccurred?.Invoke(this, "VLC failed to play this file.");
        }
        catch (Exception ex)
        {
            InitializationError = $"VLC/libvlc could not be initialized: {ex.Message}";
        }
    }

    public MediaPlayer? MediaPlayer { get; }

    public string? InitializationError { get; }

    public string? CurrentFilePath => _state.FilePath;

    public TimeSpan Position => TimeSpan.FromMilliseconds(Math.Max(0, MediaPlayer?.Time ?? 0));

    public TimeSpan Duration => TimeSpan.FromMilliseconds(Math.Max(0, MediaPlayer?.Length ?? 0));

    public bool IsPlaying => MediaPlayer?.IsPlaying ?? false;

    public bool IsMuted => MediaPlayer?.Mute ?? false;

    public int Volume => MediaPlayer?.Volume ?? _state.Volume;

    public event EventHandler<MediaState>? StateChanged;
    public event EventHandler? EndReached;
    public event EventHandler<string>? ErrorOccurred;

    public Task OpenAsync(string filePath, TimeSpan resumePosition)
    {
        if (MediaPlayer is null || _libVlc is null)
        {
            ErrorOccurred?.Invoke(this, InitializationError ?? "VLC/libvlc is not available.");
            return Task.CompletedTask;
        }

        if (!File.Exists(filePath))
        {
            ErrorOccurred?.Invoke(this, "The selected file does not exist.");
            return Task.CompletedTask;
        }

        _state.FilePath = filePath;
        using var media = new Media(_libVlc, new Uri(filePath));
        if (!MediaPlayer.Play(media))
        {
            ErrorOccurred?.Invoke(this, "VLC could not start playback.");
            return Task.CompletedTask;
        }

        if (resumePosition > TimeSpan.Zero)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(250).ConfigureAwait(false);
                Seek(resumePosition);
            });
        }

        StateChanged?.Invoke(this, Snapshot());
        return Task.CompletedTask;
    }

    public void TogglePlayPause()
    {
        if (MediaPlayer?.Media is null)
        {
            return;
        }

        if (MediaPlayer.IsPlaying)
        {
            MediaPlayer.Pause();
        }
        else
        {
            MediaPlayer.Play();
        }

        StateChanged?.Invoke(this, Snapshot());
    }

    public void Seek(TimeSpan position)
    {
        if (MediaPlayer?.Media is null)
        {
            return;
        }

        MediaPlayer.Time = (long)Math.Max(0, position.TotalMilliseconds);
        StateChanged?.Invoke(this, Snapshot());
    }

    public void SeekRelative(TimeSpan offset)
    {
        Seek(Position + offset);
    }

    public void SetVolume(int volume)
    {
        var clamped = Math.Clamp(volume, 0, 100);
        if (MediaPlayer is null)
        {
            _state.Volume = clamped;
        }
        else
        {
            MediaPlayer.Volume = clamped;
        }

        StateChanged?.Invoke(this, Snapshot());
    }

    public void ChangeVolume(int delta)
    {
        SetVolume(Volume + delta);
    }

    public void ToggleMute()
    {
        if (MediaPlayer is null)
        {
            return;
        }

        MediaPlayer.Mute = !MediaPlayer.Mute;
        StateChanged?.Invoke(this, Snapshot());
    }

    public MediaState Snapshot()
    {
        _state.Position = Position;
        _state.Duration = Duration;
        _state.IsPlaying = MediaPlayer?.IsPlaying ?? false;
        _state.IsMuted = MediaPlayer?.Mute ?? false;
        _state.Volume = MediaPlayer?.Volume ?? _state.Volume;
        return _state;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        MediaPlayer?.Dispose();
        _libVlc?.Dispose();
        _disposed = true;
    }
}
