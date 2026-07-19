using Avalonia;
using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using Lumyn.Core.Services;

namespace Lumyn.App.Controls;

public sealed class MpvVideoSurface : OpenGlControlBase
{
    public static readonly StyledProperty<PlaybackService?> PlaybackProperty =
        AvaloniaProperty.Register<MpvVideoSurface, PlaybackService?>(nameof(Playback));

    private bool _rendererInitialized;
    private bool _glReady;
    private int _renderRequestQueued;
    private bool _isReadyForPlaybackOpen;

    public event EventHandler? ReadyForPlaybackOpen;

    public bool IsReadyForPlaybackOpen
    {
        get => _isReadyForPlaybackOpen;
        private set
        {
            if (_isReadyForPlaybackOpen == value)
                return;

            _isReadyForPlaybackOpen = value;
            if (value)
                ReadyForPlaybackOpen?.Invoke(this, EventArgs.Empty);
        }
    }

    public PlaybackService? Playback
    {
        get => GetValue(PlaybackProperty);
        set => SetValue(PlaybackProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PlaybackProperty && _glReady && !_rendererInitialized)
            RequestNextFrameRendering();
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        base.OnOpenGlInit(gl);
        _glReady = true;
        TryInitializeRenderer(gl);
        IsReadyForPlaybackOpen = _rendererInitialized;
        if (_rendererInitialized)
            RequestNextFrameRendering();
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        IsReadyForPlaybackOpen = false;
        _glReady = false;
        _rendererInitialized = false;
        Interlocked.Exchange(ref _renderRequestQueued, 0);
        Playback?.ShutdownRenderer();
        base.OnOpenGlDeinit(gl);
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        Interlocked.Exchange(ref _renderRequestQueued, 0);

        var playback = Playback;
        if (!_glReady || playback is null)
            return;

        TryInitializeRenderer(gl);
        if (_rendererInitialized)
            IsReadyForPlaybackOpen = true;

        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var width = Math.Max(1, (int)Math.Round(Bounds.Width * scale));
        var height = Math.Max(1, (int)Math.Round(Bounds.Height * scale));
        playback.RenderVideo(fb, width, height);
    }

    private void TryInitializeRenderer(GlInterface gl)
    {
        if (_rendererInitialized || !_glReady || Playback is null)
            return;

        _rendererInitialized = Playback.InitializeRenderer(
            proc => gl.GetProcAddress(proc),
            QueueRenderRequest);
    }

    private void QueueRenderRequest()
    {
        if (!_glReady)
            return;

        if (Interlocked.Exchange(ref _renderRequestQueued, 1) == 1)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_glReady)
                RequestNextFrameRendering();
            else
                Interlocked.Exchange(ref _renderRequestQueued, 0);
        }, DispatcherPriority.Background);
    }
}
