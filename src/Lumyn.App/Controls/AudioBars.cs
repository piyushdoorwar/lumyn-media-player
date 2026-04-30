using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Lumyn.App.Controls;

/// <summary>
/// Animated equalizer-style bars that respond to audio playback state.
/// </summary>
public sealed class AudioBars : Control
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<AudioBars, bool>(nameof(IsActive));

    private static readonly IBrush ActiveBrush = new SolidColorBrush(Color.Parse("#3A9B4B"));
    private static readonly IBrush IdleBrush   = new SolidColorBrush(Color.Parse("#2A3D2E"));

    // Staggered phase offsets so bars animate independently.
    private static readonly double[] PhaseOffsets = [0.0, 0.85, 1.6, 2.4, 3.2, 3.9, 4.7];

    private readonly DispatcherTimer _timer;
    private double _phase;

    public AudioBars()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _timer.Tick += (_, _) =>
        {
            _phase += 0.18;
            if (_phase > Math.PI * 200) _phase -= Math.PI * 200;
            InvalidateVisual();
        };
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsActiveProperty)
        {
            if (IsActive) _timer.Start();
            else { _timer.Stop(); InvalidateVisual(); }
        }
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        const int n = 7;
        const double gap = 4.0;
        var barW = Math.Max(2.0, (w - gap * (n - 1)) / n);

        for (var i = 0; i < n; i++)
        {
            double barH;
            if (IsActive)
            {
                var sine = (Math.Sin(_phase + PhaseOffsets[i]) + 1.0) / 2.0;
                barH = 4.0 + sine * (h - 6.0);
            }
            else
            {
                barH = 3.0;
            }

            var x = i * (barW + gap);
            var y = h - barH;
            ctx.FillRectangle(IsActive ? ActiveBrush : IdleBrush, new Rect(x, y, barW, barH), 2f);
        }
    }
}
