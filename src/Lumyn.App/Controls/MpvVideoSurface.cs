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

    public PlaybackService? Playback
    {
        get => GetValue(PlaybackProperty);
        set => SetValue(PlaybackProperty, value);
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        base.OnOpenGlInit(gl);
        TryInitializeRenderer(gl);
        RequestNextFrameRendering();
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        _rendererInitialized = false;
        base.OnOpenGlDeinit(gl);
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
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
            () => Dispatcher.UIThread.Post(RequestNextFrameRendering, DispatcherPriority.Render));
        _rendererInitialized = true;
    }
}
