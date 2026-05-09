using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Lumyn.App.Controls;

public sealed class FlowWatermark : Control
{
    private static readonly Pen[] LinePens =
    [
        new(new SolidColorBrush(Color.Parse("#383F3F3F")), 1.4),
        new(new SolidColorBrush(Color.Parse("#2E5A5A5A")), 1.2),
        new(new SolidColorBrush(Color.Parse("#2849B35C")), 1.2)
    ];

    private static readonly IBrush DotBrush = new SolidColorBrush(Color.Parse("#2649B35C"));

    private readonly DispatcherTimer _timer;
    private double _phase;

    public FlowWatermark()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _timer.Tick += (_, _) =>
        {
            _phase = (_phase + 0.018) % (Math.PI * 2);
            InvalidateVisual();
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0) return;

        var baseY = height * 0.62;
        var amplitude = Math.Clamp(height * 0.09, 16, 42);

        for (var line = 0; line < 6; line++)
        {
            var pen = LinePens[line % LinePens.Length];
            var yOffset = (line - 2.5) * 20;
            var localPhase = _phase + line * 0.72;
            Point? previous = null;

            for (var i = 0; i <= 56; i++)
            {
                var t = i / 56.0;
                var x = width * t;
                var wave = Math.Sin(t * Math.PI * 2.2 + localPhase) * amplitude;
                var drift = Math.Cos(t * Math.PI * 4.1 - localPhase * 0.7) * amplitude * 0.32;
                var y = baseY + yOffset + wave + drift;
                var point = new Point(x, y);

                if (previous is { } prev)
                    context.DrawLine(pen, prev, point);

                previous = point;
            }
        }

        for (var i = 0; i < 18; i++)
        {
            var t = (i / 18.0 + _phase * 0.045) % 1.0;
            var x = width * t;
            var y = baseY + Math.Sin(t * Math.PI * 3.0 + _phase) * amplitude * 1.35 + 22;
            context.DrawEllipse(DotBrush, null, new Point(x, y), 1.4, 1.4);
        }
    }
}
