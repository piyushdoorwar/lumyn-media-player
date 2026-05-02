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

    protected override void OnOpenGlInit(GlInterface gl)
    {
        base.OnOpenGlInit(gl);
        TryInitializeRenderer(gl);
        IsReadyForPlaybackOpen = true;
        RequestNextFrameRendering();
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        IsReadyForPlaybackOpen = false;
        _rendererInitialized = false;
        base.OnOpenGlDeinit(gl);
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        Interlocked.Exchange(ref _renderRequestQueued, 0);

        var playback = Playback;
        if (playback is null)
            return;

        TryInitializeRenderer(gl);

        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var width = Math.Max(1, (int)Math.Round(Bounds.Width * scale));
        var height = Math.Max(1, (int)Math.Round(Bounds.Height * scale));
        playback.RenderVideo(fb, width, height);
    }

    private void TryInitializeRenderer(GlInterface gl)
    {
        if (_rendererInitialized || Playback is null)
            return;

        Playback.InitializeRenderer(
            proc => gl.GetProcAddress(proc),
            QueueRenderRequest);
        _rendererInitialized = true;
    }

    private void QueueRenderRequest()
    {
        if (Interlocked.Exchange(ref _renderRequestQueued, 1) == 1)
            return;

        Dispatcher.UIThread.Post(RequestNextFrameRendering, DispatcherPriority.Background);
    }
}
