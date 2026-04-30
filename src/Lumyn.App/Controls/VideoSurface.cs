using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Lumyn.App.Controls;

/// <summary>
/// Lightweight custom control that renders libvlc software-decoded frames.
/// <para>
/// Unlike binding a <see cref="WriteableBitmap"/> to an <c>&lt;Image&gt;</c>,
/// this control calls <see cref="InvalidateVisual"/> directly so Avalonia always
/// schedules a repaint — the standard binding path skips re-renders when the
/// same bitmap reference is set again (which is every frame for same-resolution video).
/// </para>
/// <para>
/// <b>Thread safety</b>: <see cref="PushFrame"/> MUST be called on the UI thread.
/// The staging buffer is written on the VLC decode thread (see <c>MainViewModel</c>).
/// </para>
/// </summary>
public sealed class VideoSurface : Control
{
    private WriteableBitmap? _bitmap;

    /// <summary>
    /// Copy <paramref name="data"/> into the internal bitmap and schedule a repaint.
    /// Must be called on the UI thread.
    /// </summary>
    public void PushFrame(byte[] data, int w, int h, int pitch)
    {
        // Recreate bitmap only when dimensions change (allocation is expensive).
        if (_bitmap is null || _bitmap.PixelSize.Width != w || _bitmap.PixelSize.Height != h)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(
                new PixelSize(w, h),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);
        }

        using var fb = _bitmap.Lock();
        unsafe
        {
            fixed (byte* src = data)
                Buffer.MemoryCopy(src, (void*)fb.Address, (long)(pitch * h), (long)(pitch * h));
        }

        // Always triggers a repaint regardless of whether the bitmap reference changed.
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (_bitmap is null)
        {
            context.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
            return;
        }

        // Letterbox / pillarbox — fit the video inside the control bounds.
        var ctl = Bounds.Size;
        var vidW = (double)_bitmap.PixelSize.Width;
        var vidH = (double)_bitmap.PixelSize.Height;
        var scale = Math.Min(ctl.Width / vidW, ctl.Height / vidH);
        var dstW = vidW * scale;
        var dstH = vidH * scale;
        var dst = new Rect(
            (ctl.Width  - dstW) / 2,
            (ctl.Height - dstH) / 2,
            dstW, dstH);

        context.DrawImage(_bitmap, new Rect(0, 0, vidW, vidH), dst);
    }
}
