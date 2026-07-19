using System.Reflection;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Lumyn.Core.Models;

namespace Lumyn.Core.Services;

public sealed class PlaybackService : IDisposable
{
    private const ulong MpvRenderUpdateFrame = 1UL << 0;

    private static readonly Dictionary<string, string> LanguageNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ara"] = "Arabic",
        ["chi"] = "Chinese",
        ["zho"] = "Chinese",
        ["dan"] = "Danish",
        ["dut"] = "Dutch",
        ["nld"] = "Dutch",
        ["eng"] = "English",
        ["fin"] = "Finnish",
        ["fre"] = "French",
        ["fra"] = "French",
        ["ger"] = "German",
        ["deu"] = "German",
        ["hin"] = "Hindi",
        ["ita"] = "Italian",
        ["jpn"] = "Japanese",
        ["kor"] = "Korean",
        ["nor"] = "Norwegian",
        ["pol"] = "Polish",
        ["por"] = "Portuguese",
        ["rus"] = "Russian",
        ["spa"] = "Spanish",
        ["swe"] = "Swedish",
        ["tur"] = "Turkish"
    };

    private readonly MediaState _state = new();
    private readonly Thread? _eventThread;
    private readonly object _stateLock = new();
    private IntPtr _mpv;
    private volatile bool _disposed;
    private bool _initialized;
    private bool _loop;
    private bool _pause = true;
    private bool _rendererReady;
    private long _trackRevision;
    private IntPtr _renderContext;
    private MpvNative.MpvRenderUpdateCallback? _renderUpdateCallback;
    private MpvNative.MpvOpenGlGetProcAddress? _getProcAddressCallback;
    private Func<string, IntPtr>? _getProcAddress;
    private Action? _requestRender;
    private int _lastRenderFramebuffer = -1;
    private int _lastRenderWidth;
    private int _lastRenderHeight;
    private long _openGeneration;
    private long _pendingResumeGeneration;
    private TimeSpan _pendingResumePosition;
    private string? _pendingResumeFilePath;

    public PlaybackService()
    {
        try
        {
            _mpv = MpvNative.mpv_create();
            if (_mpv == IntPtr.Zero)
            {
                InitializationError = "mpv could not be initialized.";
                return;
            }

            SetOption("terminal", "no");
            SetOption("idle", "yes");
            SetOption("vo", "libmpv");
            SetOption("hwdec", "auto-safe");
            SetOption("osd-level", "0");

            var init = MpvNative.mpv_initialize(_mpv);
            if (init < 0)
            {
                InitializationError = $"mpv initialization failed: {init}.";
                MpvNative.mpv_destroy(_mpv);
                _mpv = IntPtr.Zero;
                return;
            }
            _initialized = true;

            Observe("time-pos", MpvFormat.Double);
            Observe("duration", MpvFormat.Double);
            Observe("pause", MpvFormat.Flag);
            Observe("mute", MpvFormat.Flag);
            Observe("volume", MpvFormat.Double);
            Observe("speed", MpvFormat.Double);
            Observe("aid", MpvFormat.String);
            Observe("sid", MpvFormat.String);

            SetVolume(_state.Volume);

            _eventThread = new Thread(EventLoop)
            {
                IsBackground = true,
                Name = "Lumyn mpv events"
            };
            _eventThread.Start();
        }
        catch (DllNotFoundException ex)
        {
            InitializationError = $"libmpv could not be loaded: {ex.Message}";
            DestroyAfterInitializationFailure();
        }
        catch (Exception ex)
        {
            InitializationError = $"mpv could not be initialized: {ex.Message}";
            DestroyAfterInitializationFailure();
        }
    }

    public string? InitializationError { get; }
    public string? CurrentFilePath => Snapshot().FilePath;
    public TimeSpan Position => Snapshot().Position;
    public TimeSpan Duration => Snapshot().Duration;
    public bool IsPlaying => Snapshot().IsPlaying;
    public bool IsMuted => Snapshot().IsMuted;
    public int Volume => Snapshot().Volume;
    public float Speed => Snapshot().Speed;
    public bool IsLooping => _loop;
    public long TrackRevision => Interlocked.Read(ref _trackRevision);

    public event EventHandler<MediaState>? StateChanged;
    public event EventHandler? EndReached;
    public event EventHandler<string>? ErrorOccurred;

    public Task<bool> OpenAsync(string filePath, TimeSpan resumePosition)
    {
        if (_mpv == IntPtr.Zero)
        {
            ErrorOccurred?.Invoke(this, InitializationError ?? "mpv is not available.");
            return Task.FromResult(false);
        }

        if (!File.Exists(filePath))
        {
            ErrorOccurred?.Invoke(this, "The selected file does not exist.");
            return Task.FromResult(false);
        }
        filePath = Path.GetFullPath(filePath);

        var generation = Interlocked.Increment(ref _openGeneration);
        var previousState = Snapshot();
        lock (_stateLock)
        {
            _state.FilePath = filePath;
            _state.Position = TimeSpan.Zero;
            _state.Duration = TimeSpan.Zero;
            _state.IsPlaying = true;
            _pause = false;
            _pendingResumeGeneration = generation;
            _pendingResumePosition = resumePosition > TimeSpan.Zero ? resumePosition : TimeSpan.Zero;
            _pendingResumeFilePath = filePath;
        }

        var result = Command("loadfile", filePath, "replace");
        if (result < 0)
        {
            lock (_stateLock)
            {
                if (_pendingResumeGeneration == generation)
                {
                    _pendingResumeGeneration = 0;
                    _pendingResumePosition = TimeSpan.Zero;
                    _pendingResumeFilePath = null;
                }
                _state.FilePath = previousState.FilePath;
                _state.Position = previousState.Position;
                _state.Duration = previousState.Duration;
                _pause = !previousState.IsPlaying;
            }
            ErrorOccurred?.Invoke(this, $"mpv could not open the selected file (error {result}).");
            RaiseStateChanged();
            return Task.FromResult(false);
        }

        RaiseStateChanged();
        return Task.FromResult(true);
    }

    public void Stop()
    {
        if (_mpv == IntPtr.Zero) return;
        Command("stop");
        lock (_stateLock)
        {
            _state.FilePath = null;
            _state.Position = TimeSpan.Zero;
            _state.Duration = TimeSpan.Zero;
            _state.IsPlaying = false;
            _pause = true;
        }
        RaiseStateChanged();
    }

    public void TogglePlayPause()
    {
        if (_mpv == IntPtr.Zero || string.IsNullOrWhiteSpace(_state.FilePath)) return;
        var snapshot = Snapshot();
        if (_pause && snapshot.Duration > TimeSpan.Zero &&
            snapshot.Position >= snapshot.Duration - TimeSpan.FromMilliseconds(250))
            Seek(TimeSpan.Zero);
        SetFlag("pause", !_pause);
    }

    public void Play()
    {
        if (_mpv == IntPtr.Zero || string.IsNullOrWhiteSpace(_state.FilePath)) return;
        SetFlag("pause", false);
    }

    public void Pause()
    {
        if (_mpv == IntPtr.Zero || string.IsNullOrWhiteSpace(_state.FilePath)) return;
        SetFlag("pause", true);
    }

    public void Seek(TimeSpan position)
    {
        if (_mpv == IntPtr.Zero || string.IsNullOrWhiteSpace(_state.FilePath)) return;
        SetDouble("time-pos", Math.Max(0, position.TotalSeconds));
        lock (_stateLock)
            _state.Position = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        RaiseStateChanged();
    }

    public void SeekRelative(TimeSpan offset)
    {
        var seconds = offset.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        Command("seek", seconds, "relative", "exact");
    }

    public void StepFrame()
    {
        if (_mpv == IntPtr.Zero || string.IsNullOrWhiteSpace(_state.FilePath)) return;
        SetFlag("pause", true);
        Command("frame-step");
    }

    public void StepFrameBack()
    {
        if (_mpv == IntPtr.Zero || string.IsNullOrWhiteSpace(_state.FilePath)) return;
        SetFlag("pause", true);
        Command("frame-back-step");
    }

    public void SetVolume(int volume)
    {
        var clamped = Math.Clamp(volume, 0, 150);
        lock (_stateLock)
            _state.Volume = clamped;
        if (_mpv != IntPtr.Zero)
            SetDouble("volume", clamped);
        RaiseStateChanged();
    }

    public void ChangeVolume(int delta) => SetVolume(Volume + delta);

    public void ToggleMute()
    {
        if (_mpv == IntPtr.Zero) return;
        SetFlag("mute", !IsMuted);
    }

    public void SetSpeed(float rate)
    {
        var clamped = Math.Clamp(rate, 0.25f, 4.0f);
        lock (_stateLock)
            _state.Speed = clamped;
        if (_mpv != IntPtr.Zero)
            SetDouble("speed", clamped);
        RaiseStateChanged();
    }

    // ── Video adjustments ──────────────────────────────────────────────────

    public void SetBrightness(int value)  => SetInt("brightness",  Math.Clamp(value, -100, 100));
    public void SetContrast(int value)    => SetInt("contrast",    Math.Clamp(value, -100, 100));
    public void SetSaturation(int value)  => SetInt("saturation",  Math.Clamp(value, -100, 100));
    public void SetVideoRotation(int deg) => SetInt("video-rotate", ((deg % 360) + 360) % 360);
    public void SetVideoZoom(double zoom) => SetDouble("video-zoom", Math.Clamp(zoom, -2.0, 2.0));
    public void SetVideoAspect(string aspect) => SetPropertyString("video-aspect-override", aspect);

    // ── Audio clarity ──────────────────────────────────────────────────────

    public void SetAudioFilter(string? filter)
    {
        SetPropertyString("af", string.IsNullOrWhiteSpace(filter) ? "" : filter);
        // Setting the `af` property replaces the entire chain, dropping our
        // labelled measurement filter — re-assert it so the visualizer keeps
        // reacting when the user toggles an audio-clarity mode.
        if (_audioMetering) ApplyAudioMetering();
    }

    // ── Audio level metering (drives the audio-mode visualizer) ─────────────

    private bool _audioMetering;

    /// <summary>
    /// Toggles a measurement-only <c>astats</c> filter on the audio chain so
    /// <see cref="GetAudioLevel"/> can report live loudness. Pass-through; does
    /// not alter the audio that is heard.
    /// </summary>
    public void SetAudioMetering(bool enabled)
    {
        if (_audioMetering == enabled) return;
        _audioMetering = enabled;
        ApplyAudioMetering();
    }

    private void ApplyAudioMetering()
    {
        // Remove any existing instance first (ignore failure when absent).
        Command("af", "remove", "@viz");
        if (_audioMetering)
            Command("af", "add", "@viz:lavfi=[astats=metadata=1:reset=1]");
    }

    /// <summary>
    /// Live overall RMS loudness normalized to 0..1, or -1 when unavailable
    /// (no metering filter, silence, or unparseable value).
    /// </summary>
    public double GetAudioLevel()
    {
        if (_mpv == IntPtr.Zero || !_audioMetering) return -1;

        // af-metadata is a structured property. Reading a nested metadata key
        // as a raw string crashes libmpv 0.37 (Ubuntu 24.04); request the
        // documented node map and select the key ourselves instead.
        var raw = GetStringMapValue("af-metadata/viz", "lavfi.astats.Overall.RMS_level");
        if (string.IsNullOrEmpty(raw)
            || !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var db))
            return -1;

        // astats reports RMS in dBFS (≤ 0; -inf for digital silence).
        // Map a useful musical window of ~-60..0 dB onto 0..1.
        if (double.IsNegativeInfinity(db) || db <= -60.0) return 0.0;
        if (db >= 0.0) return 1.0;
        return (db + 60.0) / 60.0;
    }

    // ── Metadata / track info ──────────────────────────────────────────────

    /// <summary>Reads a metadata tag by key (e.g. "title", "artist", "album"). Returns null if unavailable.</summary>
    public string? GetMetadata(string key) => GetString($"metadata/by-key/{key}");

    /// <summary>Returns true if the loaded file has at least one video track with actual dimensions.</summary>
    public bool HasVideoTrack => GetTracks("video").Length > 0;

    public void ToggleLoop()
    {
        _loop = !_loop;
        SetPropertyString("loop-file", _loop ? "inf" : "no");
        lock (_stateLock)
            _state.IsLooping = _loop;
        RaiseStateChanged();
    }

    /// <summary>
    /// Sets the mpv A-B loop points (in playback time). Pass <c>null</c> for a
    /// point to clear it. When both points are set, mpv loops playback between
    /// them; with only A set, playback is unaffected until B is also set.
    /// </summary>
    public void SetAbLoop(TimeSpan? a, TimeSpan? b)
    {
        if (_mpv == IntPtr.Zero) return;
        SetPropertyString("ab-loop-a", FormatAbLoop(a));
        SetPropertyString("ab-loop-b", FormatAbLoop(b));
    }

    private static string FormatAbLoop(TimeSpan? t) =>
        t is { } v
            ? Math.Max(0, v.TotalSeconds).ToString("0.###", CultureInfo.InvariantCulture)
            : "no";

    public bool TakeSnapshot(string outputPath)
    {
        if (_mpv == IntPtr.Zero || string.IsNullOrWhiteSpace(_state.FilePath)) return false;
        return Command("screenshot-to-file", outputPath, "video") >= 0;
    }

    public void LoadSubtitleFile(string path)
    {
        if (_mpv == IntPtr.Zero || string.IsNullOrWhiteSpace(_state.FilePath)) return;

        // Remove all previously added external subtitle tracks so stale mpv-rendered
        // lines from the old track cannot appear frozen on the video surface.
        var count = GetInt64("track-list/count");
        var toRemove = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var prefix = $"track-list/{i}";
            if (!string.Equals(GetString($"{prefix}/type"), "sub", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!GetFlag($"{prefix}/external"))
                continue;
            var id = (int)GetInt64($"{prefix}/id", -1);
            if (id >= 0) toRemove.Add(id);
        }
        foreach (var id in toRemove)
            Command("sub-remove", id.ToString(CultureInfo.InvariantCulture));

        Command("sub-add", path, "cached");
        MarkTracksChanged();
    }

    public MediaTrack[] GetAudioTracks() => GetTracks("audio");

    public MediaTrack[] GetSubtitleTracks() =>
    [
        new(-1, "Off", CurrentSubtitleTrack < 0),
        .. GetTracks("sub")
    ];

    public int CurrentAudioTrack => GetSelectedTrackId("audio");
    public int CurrentSubtitleTrack => GetSelectedTrackId("sub");

    public void SetAudioTrack(int id)
    {
        if (id >= 0)
            SetPropertyString("aid", id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        MarkTracksChanged();
    }

    public void SetSubtitleTrack(int id)
    {
        if (id < 0)
            SetPropertyString("sid", "no");
        else
            SetPropertyString("sid", id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        MarkTracksChanged();
    }

    public long SubtitleDelayMs
    {
        get => (long)(GetDouble("sub-delay") * 1000);
        set => SetDouble("sub-delay", value / 1000.0);
    }

    public void SetSubtitleAppearance(double fontSize, string fontFamily, string color, bool outlined)
    {
        SetDouble("sub-font-size", Math.Clamp(fontSize, 8, 120));
        SetPropertyString("sub-font", fontFamily);
        SetPropertyString("sub-color", color);
        SetDouble("sub-border-size", outlined ? 3.5 : 1.5);
        SetPropertyString("sub-border-color", "#000000");
    }

    public void CycleAudioTrack()
    {
        var tracks = GetAudioTracks();
        if (tracks.Length == 0) return;

        var current = CurrentAudioTrack;
        var idx = Array.FindIndex(tracks, t => t.Id == current);
        var next = tracks[(idx + 1 + tracks.Length) % tracks.Length];
        SetAudioTrack(next.Id);
    }

    public void CycleSubtitleTrack()
    {
        var tracks = GetSubtitleTracks();
        if (tracks.Length == 0) return;

        var current = CurrentSubtitleTrack;
        var idx = Array.FindIndex(tracks, t => t.Id == current);
        var next = tracks[(idx + 1 + tracks.Length) % tracks.Length];
        SetSubtitleTrack(next.Id);
    }

    public bool InitializeRenderer(Func<string, IntPtr> getProcAddress, Action requestRender)
    {
        if (_mpv == IntPtr.Zero) return false;
        if (_renderContext != IntPtr.Zero) return true;

        ResetRenderTarget();
        _getProcAddress = getProcAddress;
        _requestRender = requestRender;
        _getProcAddressCallback = (_, name) =>
        {
            var proc = Marshal.PtrToStringAnsi(name);
            return string.IsNullOrEmpty(proc) ? IntPtr.Zero : _getProcAddress?.Invoke(proc) ?? IntPtr.Zero;
        };

        unsafe
        {
            var apiType = Marshal.StringToHGlobalAnsi("opengl");
            var initParams = new MpvNative.MpvOpenGlInitParams
            {
                get_proc_address = Marshal.GetFunctionPointerForDelegate(_getProcAddressCallback),
                get_proc_address_ctx = IntPtr.Zero
            };
            var initPtr = Marshal.AllocHGlobal(Marshal.SizeOf<MpvNative.MpvOpenGlInitParams>());
            Marshal.StructureToPtr(initParams, initPtr, false);

            try
            {
                var parameters = stackalloc MpvNative.MpvRenderParam[3];
                parameters[0] = new MpvNative.MpvRenderParam(MpvRenderParamType.ApiType, apiType);
                parameters[1] = new MpvNative.MpvRenderParam(MpvRenderParamType.OpenGlInitParams, initPtr);
                parameters[2] = new MpvNative.MpvRenderParam(MpvRenderParamType.Invalid, IntPtr.Zero);

                var result = MpvNative.mpv_render_context_create(out _renderContext, _mpv, parameters);
                if (result < 0 || _renderContext == IntPtr.Zero)
                {
                    ErrorOccurred?.Invoke(this, $"mpv OpenGL renderer failed to initialize: {result}.");
                    ClearRendererCallbacks();
                    return false;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(apiType);
                Marshal.FreeHGlobal(initPtr);
            }
        }

        _renderUpdateCallback = _ => _requestRender?.Invoke();
        MpvNative.mpv_render_context_set_update_callback(_renderContext, _renderUpdateCallback, IntPtr.Zero);
        _rendererReady = true;
        return true;
    }

    public void RenderVideo(int framebuffer, int width, int height)
    {
        if (!_rendererReady || _renderContext == IntPtr.Zero || width <= 0 || height <= 0) return;

        var updateFlags = MpvNative.mpv_render_context_update(_renderContext);
        var targetChanged = framebuffer != _lastRenderFramebuffer ||
                            width != _lastRenderWidth ||
                            height != _lastRenderHeight;

        if ((updateFlags & MpvRenderUpdateFrame) == 0 && !targetChanged)
            return;

        unsafe
        {
            var fbo = new MpvNative.MpvOpenGlFbo(framebuffer, width, height, 0);
            var flip = 1;
            var block = 0;
            var parameters = stackalloc MpvNative.MpvRenderParam[4];
            parameters[0] = new MpvNative.MpvRenderParam(MpvRenderParamType.OpenGlFbo, (IntPtr)(&fbo));
            parameters[1] = new MpvNative.MpvRenderParam(MpvRenderParamType.FlipY, (IntPtr)(&flip));
            parameters[2] = new MpvNative.MpvRenderParam(MpvRenderParamType.BlockForTargetTime, (IntPtr)(&block));
            parameters[3] = new MpvNative.MpvRenderParam(MpvRenderParamType.Invalid, IntPtr.Zero);
            MpvNative.mpv_render_context_render(_renderContext, parameters);
        }

        _lastRenderFramebuffer = framebuffer;
        _lastRenderWidth = width;
        _lastRenderHeight = height;
    }

    public void ShutdownRenderer()
    {
        if (_renderContext != IntPtr.Zero)
        {
            MpvNative.mpv_render_context_free(_renderContext);
            _renderContext = IntPtr.Zero;
        }

        _rendererReady = false;
        ResetRenderTarget();
        ClearRendererCallbacks();
    }

    public MediaState Snapshot()
    {
        lock (_stateLock)
        {
            _state.IsPlaying = !_pause && !string.IsNullOrWhiteSpace(_state.FilePath);
            _state.IsLooping = _loop;
            return new MediaState
            {
                FilePath = _state.FilePath,
                Position = _state.Position,
                Duration = _state.Duration,
                IsPlaying = _state.IsPlaying,
                IsMuted = _state.IsMuted,
                Volume = _state.Volume,
                Speed = _state.Speed,
                IsLooping = _state.IsLooping
            };
        }
    }

    private void EventLoop()
    {
        while (!_disposed && _mpv != IntPtr.Zero)
        {
            var evt = Marshal.PtrToStructure<MpvNative.MpvEvent>(MpvNative.mpv_wait_event(_mpv, 0.25));
            if (evt.event_id == MpvEventId.None) continue;

            switch (evt.event_id)
            {
                case MpvEventId.Shutdown:
                    return;
                case MpvEventId.FileLoaded:
                    TimeSpan resumePosition;
                    var loadedPath = GetString("path");
                    lock (_stateLock)
                    {
                        _pause = GetFlag("pause");
                        if (_pendingResumeGeneration == Interlocked.Read(ref _openGeneration) &&
                            string.Equals(loadedPath, _pendingResumeFilePath, StringComparison.Ordinal))
                        {
                            resumePosition = _pendingResumePosition;
                            _pendingResumeGeneration = 0;
                            _pendingResumePosition = TimeSpan.Zero;
                            _pendingResumeFilePath = null;
                        }
                        else
                        {
                            resumePosition = TimeSpan.Zero;
                        }
                    }
                    if (resumePosition > TimeSpan.Zero)
                        Seek(resumePosition);
                    MarkTracksChanged();
                    RaiseStateChanged();
                    break;
                case MpvEventId.TracksChanged:
                case MpvEventId.TrackSwitched:
                    MarkTracksChanged();
                    RaiseStateChanged();
                    break;
                case MpvEventId.EndFile:
                    // reason 0 = MPV_END_FILE_REASON_EOF (natural end).
                    // Any other reason (stop=2, quit=3, error=4, redirect=5) means the file
                    // was replaced or stopped externally — do NOT auto-advance in those cases.
                    if (evt.data != IntPtr.Zero)
                    {
                        var endFileEvt = Marshal.PtrToStructure<MpvNative.MpvEventEndFile>(evt.data);
                        if (endFileEvt.reason == 4 || (endFileEvt.reason == 0 && !_loop))
                        {
                            lock (_stateLock)
                            {
                                _pause = true;
                                _state.IsPlaying = false;
                                if (endFileEvt.reason == 0 && _state.Duration > TimeSpan.Zero)
                                    _state.Position = _state.Duration;
                            }
                        }

                        if (!_loop && endFileEvt.reason == 0)
                            EndReached?.Invoke(this, EventArgs.Empty);
                        else if (endFileEvt.reason == 4)
                            ErrorOccurred?.Invoke(this, $"Playback ended because mpv reported error {endFileEvt.error}.");
                    }
                    RaiseStateChanged();
                    break;
                case MpvEventId.Pause:
                    lock (_stateLock) _pause = true;
                    RaiseStateChanged();
                    break;
                case MpvEventId.Unpause:
                    lock (_stateLock) _pause = false;
                    RaiseStateChanged();
                    break;
                case MpvEventId.PropertyChange:
                    if (ApplyPropertyChange(evt.data))
                        RaiseStateChanged();
                    break;
            }
        }
    }

    private bool ApplyPropertyChange(IntPtr data)
    {
        if (data == IntPtr.Zero) return false;
        var property = Marshal.PtrToStructure<MpvNative.MpvEventProperty>(data);
        var name = Marshal.PtrToStringAnsi(property.name);
        if (string.IsNullOrWhiteSpace(name) || property.data == IntPtr.Zero) return false;
        var shouldRaise = true;

        lock (_stateLock)
        {
            switch (name)
            {
                case "time-pos" when property.format == MpvFormat.Double:
                    _state.Position = TimeSpan.FromSeconds(Math.Max(0, Marshal.PtrToStructure<double>(property.data)));
                    shouldRaise = false;
                    break;
                case "duration" when property.format == MpvFormat.Double:
                    _state.Duration = TimeSpan.FromSeconds(Math.Max(0, Marshal.PtrToStructure<double>(property.data)));
                    shouldRaise = false;
                    break;
                case "pause" when property.format == MpvFormat.Flag:
                    _pause = Marshal.PtrToStructure<int>(property.data) != 0;
                    break;
                case "mute" when property.format == MpvFormat.Flag:
                    _state.IsMuted = Marshal.PtrToStructure<int>(property.data) != 0;
                    break;
                case "volume" when property.format == MpvFormat.Double:
                    _state.Volume = (int)Math.Round(Math.Clamp(Marshal.PtrToStructure<double>(property.data), 0, 150));
                    break;
                case "speed" when property.format == MpvFormat.Double:
                    _state.Speed = (float)Math.Clamp(Marshal.PtrToStructure<double>(property.data), 0.25, 4.0);
                    break;
                case "aid" or "sid":
                    MarkTracksChanged();
                    break;
                default:
                    shouldRaise = false;
                    break;
            }
        }

        return shouldRaise;
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, Snapshot());

    private void MarkTracksChanged() => Interlocked.Increment(ref _trackRevision);

    private int Command(params string[] args)
    {
        if (_mpv == IntPtr.Zero) return -1;

        var ptrs = new IntPtr[args.Length + 1];
        try
        {
            for (var i = 0; i < args.Length; i++)
                ptrs[i] = StringToUtf8(args[i]);
            ptrs[^1] = IntPtr.Zero;

            unsafe
            {
                fixed (IntPtr* p = ptrs)
                    return MpvNative.mpv_command(_mpv, (IntPtr)p);
            }
        }
        finally
        {
            foreach (var ptr in ptrs)
                if (ptr != IntPtr.Zero)
                    Marshal.FreeHGlobal(ptr);
        }
    }

    private void SetOption(string name, string value)
    {
        if (_mpv != IntPtr.Zero)
            MpvNative.mpv_set_option_string(_mpv, name, value);
    }

    private void SetPropertyString(string name, string value)
    {
        if (_mpv != IntPtr.Zero)
            MpvNative.mpv_set_property_string(_mpv, name, value);
        RaiseStateChanged();
    }

    private void Observe(string name, MpvFormat format)
    {
        if (_mpv != IntPtr.Zero)
            MpvNative.mpv_observe_property(_mpv, 0, name, format);
    }

    private void SetFlag(string name, bool value)
    {
        if (_mpv == IntPtr.Zero) return;
        var raw = value ? 1 : 0;
        unsafe
        {
            MpvNative.mpv_set_property(_mpv, name, MpvFormat.Flag, (IntPtr)(&raw));
        }
    }

    public double[] GetChapterPositions()
    {
        if (_mpv == IntPtr.Zero) return [];
        var count = GetInt64("chapter-list/count");
        if (count <= 0) return [];
        var positions = new List<double>((int)count);
        for (var i = 0; i < count; i++)
        {
            var t = GetDouble($"chapter-list/{i}/time");
            if (!double.IsNaN(t))
                positions.Add(t);
        }
        return [.. positions];
    }

    public MediaChapter[] GetChapters()
    {
        if (_mpv == IntPtr.Zero) return [];

        var count = GetInt64("chapter-list/count");
        if (count <= 0) return [];

        var chapters = new List<MediaChapter>((int)count);
        for (var i = 0; i < count; i++)
        {
            var seconds = GetDouble($"chapter-list/{i}/time");
            if (double.IsNaN(seconds) || seconds < 0)
                continue;

            var title = GetString($"chapter-list/{i}/title");
            if (string.IsNullOrWhiteSpace(title))
                title = $"Chapter {i + 1}";

            chapters.Add(new MediaChapter(i, title.Trim(), TimeSpan.FromSeconds(seconds)));
        }

        return [.. chapters];
    }

    public void SeekToChapter(int direction)
    {
        if (_mpv == IntPtr.Zero || string.IsNullOrWhiteSpace(_state.FilePath)) return;
        Command("add", "chapter", direction > 0 ? "1" : "-1");
    }

    private bool GetFlag(string name)    {
        if (_mpv == IntPtr.Zero) return false;
        var raw = 0;
        unsafe
        {
            return MpvNative.mpv_get_property(_mpv, name, MpvFormat.Flag, (IntPtr)(&raw)) >= 0 && raw != 0;
        }
    }

    private void SetDouble(string name, double value)
    {
        if (_mpv == IntPtr.Zero) return;
        unsafe
        {
            MpvNative.mpv_set_property(_mpv, name, MpvFormat.Double, (IntPtr)(&value));
        }
    }

    private void SetInt(string name, long value)
    {
        if (_mpv == IntPtr.Zero) return;
        unsafe
        {
            MpvNative.mpv_set_property(_mpv, name, MpvFormat.Int64, (IntPtr)(&value));
        }
    }

    private double GetDouble(string name)
    {
        if (_mpv == IntPtr.Zero) return double.NaN;
        double value = double.NaN;
        unsafe
        {
            MpvNative.mpv_get_property(_mpv, name, MpvFormat.Double, (IntPtr)(&value));
        }
        return value;
    }

    private long GetInt64(string name, long fallback = 0)
    {
        if (_mpv == IntPtr.Zero) return fallback;
        long value = fallback;
        unsafe
        {
            return MpvNative.mpv_get_property(_mpv, name, MpvFormat.Int64, (IntPtr)(&value)) >= 0
                ? value
                : fallback;
        }
    }

    private string? GetString(string name)
    {
        if (_mpv == IntPtr.Zero) return null;
        var ptr = MpvNative.mpv_get_property_string(_mpv, name);
        if (ptr == IntPtr.Zero) return null;
        try
        {
            return Marshal.PtrToStringUTF8(ptr);
        }
        finally
        {
            MpvNative.mpv_free(ptr);
        }
    }

    private string? GetStringMapValue(string propertyName, string key)
    {
        if (_mpv == IntPtr.Zero) return null;

        unsafe
        {
            var node = new MpvNative.MpvNode();
            var result = MpvNative.mpv_get_property(
                _mpv,
                propertyName,
                MpvFormat.Node,
                (IntPtr)(&node));

            if (result < 0)
                return null;

            try
            {
                if (node.format != MpvFormat.NodeMap || node.value.list == IntPtr.Zero)
                    return null;

                var list = Marshal.PtrToStructure<MpvNative.MpvNodeList>(node.value.list);
                if (list.num <= 0 || list.values == IntPtr.Zero || list.keys == IntPtr.Zero)
                    return null;

                var nodeSize = Marshal.SizeOf<MpvNative.MpvNode>();
                for (var i = 0; i < list.num; i++)
                {
                    var keyPtr = Marshal.ReadIntPtr(list.keys, i * IntPtr.Size);
                    if (!string.Equals(Marshal.PtrToStringUTF8(keyPtr), key, StringComparison.Ordinal))
                        continue;

                    var valuePtr = IntPtr.Add(list.values, i * nodeSize);
                    var value = Marshal.PtrToStructure<MpvNative.MpvNode>(valuePtr);
                    return value.format == MpvFormat.String && value.value.@string != IntPtr.Zero
                        ? Marshal.PtrToStringUTF8(value.value.@string)
                        : null;
                }

                return null;
            }
            finally
            {
                MpvNative.mpv_free_node_contents(&node);
            }
        }
    }

    private MediaTrack[] GetTracks(string type)
    {
        if (_mpv == IntPtr.Zero) return [];

        var count = GetInt64("track-list/count");
        if (count <= 0) return [];

        var tracks = new List<MediaTrack>();
        for (var i = 0; i < count; i++)
        {
            var prefix = $"track-list/{i}";
            if (!string.Equals(GetString($"{prefix}/type"), type, StringComparison.OrdinalIgnoreCase))
                continue;

            var id = (int)GetInt64($"{prefix}/id", -1);
            if (id < 0) continue;

            var selected = GetFlag($"{prefix}/selected");
            tracks.Add(new MediaTrack(id, BuildTrackName(prefix, type, id), selected));
        }

        return [.. tracks];
    }

    private int GetSelectedTrackId(string type)
    {
        foreach (var track in GetTracks(type))
        {
            if (track.IsSelected)
                return track.Id;
        }

        return -1;
    }

    private string BuildTrackName(string prefix, string type, int id)
    {
        var title = GetString($"{prefix}/title");
        var lang = GetString($"{prefix}/lang");
        var codec = GetString($"{prefix}/codec");
        var external = GetFlag($"{prefix}/external");

        var fallback = type == "audio" ? $"Audio {id}" : $"Subtitle {id}";
        var main = !string.IsNullOrWhiteSpace(title) ? title! : fallback;
        var details = new List<string>();

        if (!string.IsNullOrWhiteSpace(lang))
            details.Add(FormatLanguageName(lang!));
        if (!string.IsNullOrWhiteSpace(codec))
            details.Add(codec!);
        if (external)
            details.Add("external");

        return details.Count == 0 ? main : $"{main} ({string.Join(", ", details)})";
    }

    private static string FormatLanguageName(string language)
    {
        var code = language.Trim();
        if (LanguageNames.TryGetValue(code, out var name))
            return name;

        try
        {
            if (code.Length == 2)
                return CultureInfo.GetCultureInfo(code).EnglishName;
        }
        catch (CultureNotFoundException)
        {
        }

        return code;
    }

    private static IntPtr StringToUtf8(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var ptr = Marshal.AllocHGlobal(bytes.Length + 1);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        Marshal.WriteByte(ptr, bytes.Length, 0);
        return ptr;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ShutdownRenderer();

        if (_mpv != IntPtr.Zero)
            MpvNative.mpv_wakeup(_mpv);

        if (_eventThread is { IsAlive: true })
            _eventThread.Join();

        if (_mpv != IntPtr.Zero)
        {
            MpvNative.mpv_terminate_destroy(_mpv);
            _mpv = IntPtr.Zero;
            _initialized = false;
        }
    }

    private void DestroyAfterInitializationFailure()
    {
        if (_mpv == IntPtr.Zero) return;
        if (_initialized)
            MpvNative.mpv_terminate_destroy(_mpv);
        else
            MpvNative.mpv_destroy(_mpv);
        _mpv = IntPtr.Zero;
        _initialized = false;
    }

    private void ResetRenderTarget()
    {
        _lastRenderFramebuffer = -1;
        _lastRenderWidth = 0;
        _lastRenderHeight = 0;
    }

    private void ClearRendererCallbacks()
    {
        _requestRender = null;
        _getProcAddress = null;
        _renderUpdateCallback = null;
        _getProcAddressCallback = null;
    }
}

public sealed record MediaTrack(int Id, string Name, bool IsSelected = false);

public sealed record MediaChapter(int Index, string Title, TimeSpan Position);

internal enum MpvFormat
{
    None = 0,
    String = 1,
    OsdString = 2,
    Flag = 3,
    Int64 = 4,
    Double = 5,
    Node = 6,
    NodeArray = 7,
    NodeMap = 8
}

internal enum MpvEventId
{
    None = 0,
    Shutdown = 1,
    StartFile = 6,
    EndFile = 7,
    FileLoaded = 8,
    TracksChanged = 9,
    TrackSwitched = 10,
    Idle = 11,
    Pause = 12,
    Unpause = 13,
    VideoReconfig = 17,
    AudioReconfig = 18,
    Seek = 20,
    PlaybackRestart = 21,
    PropertyChange = 22
}

internal enum MpvRenderParamType
{
    Invalid = 0,
    ApiType = 1,
    OpenGlInitParams = 2,
    OpenGlFbo = 3,
    FlipY = 4,
    BlockForTargetTime = 12
}

internal static partial class MpvNative
{
    private const string Library = "mpv";

    static MpvNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(MpvNative).Assembly, ResolveLibrary);
    }

    private static IntPtr ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, Library, StringComparison.Ordinal))
            return IntPtr.Zero;

        var candidates = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { "mpv-2.dll", "libmpv-2.dll", "mpv.dll" }
            : new[] { "libmpv.so.2", "libmpv.so" };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var loadErrors = new List<string>();

            foreach (var candidate in candidates)
            {
                foreach (var directory in GetLinuxBundledLibraryDirectories())
                {
                    var path = Path.Combine(directory, candidate);
                    if (!File.Exists(path))
                    {
                        loadErrors.Add($"{path}: file not found");
                        continue;
                    }

                    try
                    {
                        return NativeLibrary.Load(path);
                    }
                    catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
                    {
                        loadErrors.Add($"{path}: {ex.Message}");
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SNAP")) && loadErrors.Count > 0)
            {
                throw new DllNotFoundException("Unable to load bundled libmpv from the snap. " + string.Join(Environment.NewLine, loadErrors));
            }
        }

        foreach (var candidate in candidates)
        {
            if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out var handle))
                return handle;

            var localPath = Path.Combine(AppContext.BaseDirectory, candidate);
            if (NativeLibrary.TryLoad(localPath, out handle))
                return handle;
        }

        return IntPtr.Zero;
    }

    private static IEnumerable<string> GetLinuxBundledLibraryDirectories()
    {
        yield return AppContext.BaseDirectory;
        yield return Path.Combine(AppContext.BaseDirectory, "lib");

        var snap = Environment.GetEnvironmentVariable("SNAP");
        if (!string.IsNullOrWhiteSpace(snap))
        {
            yield return Path.Combine(snap, "opt", "lumyn");
            yield return Path.Combine(snap, "opt", "lumyn", "lib");
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr MpvOpenGlGetProcAddress(IntPtr ctx, IntPtr name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void MpvRenderUpdateCallback(IntPtr ctx);

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct MpvEvent
    {
        public readonly MpvEventId event_id;
        public readonly int error;
        public readonly ulong reply_userdata;
        public readonly IntPtr data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct MpvEventProperty
    {
        public readonly IntPtr name;
        public readonly MpvFormat format;
        public readonly IntPtr data;
    }

    /// <summary>Maps to <c>mpv_event_end_file</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct MpvEventEndFile
    {
        /// <summary>0 = EOF (natural end), 2 = stop, 3 = quit, 4 = error, 5 = redirect.</summary>
        public readonly int reason;
        public readonly int error;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct MpvNodeValue
    {
        [FieldOffset(0)] public IntPtr @string;
        [FieldOffset(0)] public int flag;
        [FieldOffset(0)] public long int64;
        [FieldOffset(0)] public double @double;
        [FieldOffset(0)] public IntPtr list;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MpvNode
    {
        public MpvNodeValue value;
        public MpvFormat format;
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct MpvNodeList
    {
        public readonly int num;
        public readonly IntPtr values;
        public readonly IntPtr keys;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MpvOpenGlInitParams
    {
        public IntPtr get_proc_address;
        public IntPtr get_proc_address_ctx;
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct MpvOpenGlFbo(int fbo, int w, int h, int internalFormat)
    {
        public readonly int fbo = fbo;
        public readonly int w = w;
        public readonly int h = h;
        public readonly int internal_format = internalFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct MpvRenderParam(MpvRenderParamType type, IntPtr data)
    {
        public readonly MpvRenderParamType type = type;
        public readonly IntPtr data = data;
    }

    [LibraryImport(Library)]
    public static partial IntPtr mpv_create();

    [LibraryImport(Library)]
    public static partial void mpv_terminate_destroy(IntPtr ctx);

    [LibraryImport(Library)]
    public static partial void mpv_destroy(IntPtr ctx);

    [LibraryImport(Library)]
    public static partial void mpv_wakeup(IntPtr ctx);

    [LibraryImport(Library)]
    public static partial int mpv_initialize(IntPtr ctx);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int mpv_set_option_string(IntPtr ctx, string name, string value);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int mpv_observe_property(IntPtr ctx, ulong replyUserData, string name, MpvFormat format);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int mpv_set_property(IntPtr ctx, string name, MpvFormat format, IntPtr data);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int mpv_set_property_string(IntPtr ctx, string name, string value);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int mpv_get_property(IntPtr ctx, string name, MpvFormat format, IntPtr data);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr mpv_get_property_string(IntPtr ctx, string name);

    [LibraryImport(Library)]
    public static partial void mpv_free(IntPtr data);

    [LibraryImport(Library)]
    public static unsafe partial void mpv_free_node_contents(MpvNode* node);

    [LibraryImport(Library)]
    public static partial int mpv_command(IntPtr ctx, IntPtr args);

    [LibraryImport(Library)]
    public static partial IntPtr mpv_wait_event(IntPtr ctx, double timeout);

    [LibraryImport(Library)]
    public static unsafe partial int mpv_render_context_create(out IntPtr res, IntPtr mpv, MpvRenderParam* parameters);

    [LibraryImport(Library)]
    public static unsafe partial void mpv_render_context_set_update_callback(
        IntPtr ctx,
        MpvRenderUpdateCallback callback,
        IntPtr callbackContext);

    [LibraryImport(Library)]
    public static partial ulong mpv_render_context_update(IntPtr ctx);

    [LibraryImport(Library)]
    public static unsafe partial int mpv_render_context_render(IntPtr ctx, MpvRenderParam* parameters);

    [LibraryImport(Library)]
    public static partial void mpv_render_context_free(IntPtr ctx);
}
