using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
using LibVLCSharp.Shared.Structures;
using Lumyn.Core.Models;

namespace Lumyn.Core.Services;

public sealed class PlaybackService : IDisposable
{
    // ── P/Invoke for subtitle delay (SpuDelay property is getter-only in LibVLCSharp) ──
    [DllImport("libvlc", EntryPoint = "libvlc_video_set_spu_delay")]
    private static extern int NativeSetSpuDelay(IntPtr mp, long delay);

    private readonly LibVLC? _libVlc;
    private readonly MediaState _state = new();
    private bool _disposed;
    private bool _loop;

    // ── Software video rendering ─────────────────────────────────────────────
    private IntPtr _videoBuffer = IntPtr.Zero;
    private uint _videoWidth;
    private uint _videoHeight;
    private uint _videoPitch;

    // Keep delegate references alive so GC doesn't collect them
    private readonly MediaPlayer.LibVLCVideoLockCb    _lockCb;
    private readonly MediaPlayer.LibVLCVideoUnlockCb  _unlockCb;
    private readonly MediaPlayer.LibVLCVideoDisplayCb _displayCb;
    private readonly MediaPlayer.LibVLCVideoFormatCb  _formatCb;
    private readonly MediaPlayer.LibVLCVideoCleanupCb _cleanupCb;

    public PlaybackService()
    {
        // Wire up delegates and keep references to prevent GC collection
        _formatCb  = OnVideoFormat;
        _cleanupCb = OnVideoCleanup;
        _lockCb    = OnVideoLock;
        _unlockCb  = OnVideoUnlock;
        _displayCb = OnVideoDisplay;

        try
        {
            // Linux resolves libvlc from system packages such as vlc/libvlc-dev.
            LibVLCSharp.Shared.Core.Initialize();

            // On X11 (including XWayland) VLC embeds directly into the VideoView
            // X11 sub-window — no extra flags needed beyond title suppression.
            _libVlc = new LibVLC("--no-video-title-show", "--no-spu");
            // --no-spu disables VLC's built-in subtitle blending into the video buffer.
            // Without this, VLC's SPU heap overflows ('subpicture heap full') when using
            // software rendering because the frame display rate can't keep up with the
            // subtitle generation rate.  We render subtitles ourselves as an Avalonia overlay.
            MediaPlayer = new MediaPlayer(_libVlc)
            {
                Volume = _state.Volume,
                Mute = false
            };

            // Software rendering callbacks — work on Wayland, X11, and all platforms
            MediaPlayer.SetVideoFormatCallbacks(_formatCb, _cleanupCb);
            MediaPlayer.SetVideoCallbacks(_lockCb, _unlockCb, _displayCb);

            MediaPlayer.Playing += (_, _) =>
            {
                // Re-apply volume/mute on each play in case VLC reset them.
                MediaPlayer.Volume = _state.Volume;
                MediaPlayer.Mute = false;
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
            MediaPlayer.EndReached += (_, _) =>
            {
                if (_loop && _state.FilePath is not null)
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(50).ConfigureAwait(false);
                        MediaPlayer?.Play();
                    });
                }
                else
                {
                    EndReached?.Invoke(this, EventArgs.Empty);
                }
            };
            MediaPlayer.EncounteredError += (_, _) =>
                ErrorOccurred?.Invoke(this, "VLC failed to play this file.");
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
    public float Speed => MediaPlayer?.Rate ?? 1.0f;
    public bool IsLooping => _loop;

    public event EventHandler<MediaState>? StateChanged;
    public event EventHandler? EndReached;
    public event EventHandler<string>? ErrorOccurred;

    // Fired on every decoded frame — subscribe to copy pixels to a WriteableBitmap
    public event EventHandler<VideoFrameData>? VideoFrameReady;

    // ── Open ────────────────────────────────────────────────────────────────

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

    // ── Transport ───────────────────────────────────────────────────────────

    public void Stop()
    {
        if (MediaPlayer?.Media is null) return;
        MediaPlayer.Stop();
        _state.FilePath = null;
        StateChanged?.Invoke(this, Snapshot());
    }

    public void TogglePlayPause()
    {
        if (MediaPlayer?.Media is null) return;
        if (MediaPlayer.IsPlaying)
            MediaPlayer.Pause();
        else
            MediaPlayer.Play();
        StateChanged?.Invoke(this, Snapshot());
    }

    public void Seek(TimeSpan position)
    {
        if (MediaPlayer?.Media is null) return;
        MediaPlayer.Time = (long)Math.Max(0, position.TotalMilliseconds);
        StateChanged?.Invoke(this, Snapshot());
    }

    public void SeekRelative(TimeSpan offset) => Seek(Position + offset);

    public void StepFrame()
    {
        if (MediaPlayer?.Media is null) return;
        if (MediaPlayer.IsPlaying) MediaPlayer.Pause();
        MediaPlayer.NextFrame();
    }

    // ── Volume ──────────────────────────────────────────────────────────────

    public void SetVolume(int volume)
    {
        var clamped = Math.Clamp(volume, 0, 100);
        if (MediaPlayer is null)
            _state.Volume = clamped;
        else
            MediaPlayer.Volume = clamped;
        _state.Volume = clamped;
        StateChanged?.Invoke(this, Snapshot());
    }

    public void ChangeVolume(int delta) => SetVolume(Volume + delta);

    public void ToggleMute()
    {
        if (MediaPlayer is null) return;
        MediaPlayer.Mute = !MediaPlayer.Mute;
        StateChanged?.Invoke(this, Snapshot());
    }

    // ── Speed ───────────────────────────────────────────────────────────────

    public void SetSpeed(float rate)
    {
        if (MediaPlayer is null) return;
        MediaPlayer.SetRate(rate);
        StateChanged?.Invoke(this, Snapshot());
    }

    // ── Loop ────────────────────────────────────────────────────────────────

    public void ToggleLoop()
    {
        _loop = !_loop;
        _state.IsLooping = _loop;
        StateChanged?.Invoke(this, Snapshot());
    }

    // ── Screenshot ──────────────────────────────────────────────────────────

    public bool TakeSnapshot(string outputPath) =>
        MediaPlayer?.TakeSnapshot(0, outputPath, 0, 0) ?? false;

    // ── Subtitle / Audio tracks ─────────────────────────────────────────────

    public void LoadSubtitleFile(string path)
    {
        if (MediaPlayer?.Media is null) return;
        var uri = new Uri(Path.GetFullPath(path)).AbsoluteUri;
        MediaPlayer.AddSlave(MediaSlaveType.Subtitle, uri, true);
    }

    public TrackDescription[] GetAudioTracks() =>
        MediaPlayer?.AudioTrackDescription ?? [];

    public TrackDescription[] GetSubtitleTracks() =>
        MediaPlayer?.SpuDescription ?? [];

    public int CurrentAudioTrack => MediaPlayer?.AudioTrack ?? -1;
    public int CurrentSubtitleTrack => MediaPlayer?.Spu ?? -1;

    public void SetAudioTrack(int id)
    {
        if (MediaPlayer is not null) MediaPlayer.SetAudioTrack(id);
    }

    public void SetSubtitleTrack(int id)
    {
        if (MediaPlayer is not null) MediaPlayer.SetSpu(id);
    }

    /// <summary>
    /// Subtitle / audio sync delay in milliseconds.
    /// Positive = subtitles appear later; negative = earlier.
    /// VLC's native API uses microseconds.
    /// </summary>
    public long SubtitleDelayMs
    {
        get => (MediaPlayer?.SpuDelay ?? 0L) / 1000L;
        set
        {
            if (MediaPlayer is not null)
                NativeSetSpuDelay(MediaPlayer.NativeReference, value * 1000L);
        }
    }

    public void CycleAudioTrack()
    {
        if (MediaPlayer is null) return;
        var tracks = MediaPlayer.AudioTrackDescription;
        if (tracks.Length <= 1) return;
        var current = MediaPlayer.AudioTrack;
        var idx = Array.FindIndex(tracks, t => t.Id == current);
        MediaPlayer.SetAudioTrack(tracks[(idx + 1) % tracks.Length].Id);
    }

    public void CycleSubtitleTrack()
    {
        if (MediaPlayer is null) return;
        var tracks = MediaPlayer.SpuDescription;
        if (tracks.Length == 0) return;
        var current = MediaPlayer.Spu;
        var idx = Array.FindIndex(tracks, t => t.Id == current);
        MediaPlayer.SetSpu(tracks[(idx + 1) % tracks.Length].Id);
    }

    // ── Software video callbacks ─────────────────────────────────────────────

    private uint OnVideoFormat(ref IntPtr opaque, IntPtr chroma,
        ref uint width, ref uint height, ref uint pitches, ref uint lines)
    {
        // Request BGRA (RV32) so we can copy directly into Avalonia's Bgra8888 bitmap
        Marshal.WriteByte(chroma, 0, (byte)'R');
        Marshal.WriteByte(chroma, 1, (byte)'V');
        Marshal.WriteByte(chroma, 2, (byte)'3');
        Marshal.WriteByte(chroma, 3, (byte)'2');

        _videoWidth  = width;
        _videoHeight = height;
        _videoPitch  = width * 4; // 4 bytes per pixel (BGRA)

        pitches = _videoPitch;
        lines   = height;

        if (_videoBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_videoBuffer);
            _videoBuffer = IntPtr.Zero;
        }
        _videoBuffer = Marshal.AllocHGlobal((int)(_videoPitch * height));
        return 1; // one picture buffer
    }

    private void OnVideoCleanup(ref IntPtr opaque)
    {
        if (_videoBuffer == IntPtr.Zero) return;
        Marshal.FreeHGlobal(_videoBuffer);
        _videoBuffer = IntPtr.Zero;
    }

    private IntPtr OnVideoLock(IntPtr opaque, IntPtr planes)
    {
        // Write our buffer address into planes[0]
        Marshal.WriteIntPtr(planes, _videoBuffer);
        return IntPtr.Zero;
    }

    private void OnVideoUnlock(IntPtr opaque, IntPtr picture, IntPtr planes) { }

    private void OnVideoDisplay(IntPtr opaque, IntPtr picture)
    {
        if (_videoBuffer == IntPtr.Zero) return;
        VideoFrameReady?.Invoke(this,
            new VideoFrameData(_videoBuffer, _videoWidth, _videoHeight, _videoPitch));
    }

    // ── Snapshot ────────────────────────────────────────────────────────────

    public MediaState Snapshot()
    {
        _state.Position = Position;
        _state.Duration = Duration;
        _state.IsPlaying = MediaPlayer?.IsPlaying ?? false;
        _state.IsMuted = MediaPlayer?.Mute ?? false;
        _state.Volume = MediaPlayer?.Volume ?? _state.Volume;
        _state.Speed = MediaPlayer?.Rate ?? 1.0f;
        _state.IsLooping = _loop;
        return _state;
    }

    public void Dispose()
    {
        if (_disposed) return;
        MediaPlayer?.Dispose();
        _libVlc?.Dispose();
        if (_videoBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_videoBuffer);
            _videoBuffer = IntPtr.Zero;
        }
        _disposed = true;
    }
}
