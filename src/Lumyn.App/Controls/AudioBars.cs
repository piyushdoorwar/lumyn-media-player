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

    private const int BarCount = 14;

    // Per-bar phase offsets so neighbouring bars don't move in lockstep; the
    // loudness envelope scales them all together.
    private static readonly double[] PhaseOffsets = BuildPhaseOffsets(BarCount);

    // Per-bar weighting: a gentle hump so the centre bars reach higher than the
    // edges, giving the row a more waveform-like silhouette.
    private static readonly double[] BarWeights = BuildBarWeights(BarCount);

    private readonly double[] _levels = new double[BarCount];
    private readonly DispatcherTimer _timer;
    private double _phase;

    /// <summary>
    /// Optional pull source for live loudness in 0..1. Returns &lt; 0 when no
    /// real level is available, in which case a decorative animation is shown.
    /// </summary>
    public Func<double>? LevelProvider { get; set; }

    public AudioBars()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _timer.Tick += (_, _) =>
        {
            _phase += 0.18;
            if (_phase > Math.PI * 200) _phase -= Math.PI * 200;

            var level = LevelProvider?.Invoke() ?? -1.0;
            UpdateLevels(level);
            InvalidateVisual();
        };
    }

    private static double[] BuildPhaseOffsets(int n)
    {
        var offsets = new double[n];
        for (var i = 0; i < n; i++)
            offsets[i] = i * 0.7; // staggered, wraps naturally through the sine
        return offsets;
    }

    private static double[] BuildBarWeights(int n)
    {
        var weights = new double[n];
        for (var i = 0; i < n; i++)
        {
            // Cosine hump: ~0.55 at the edges, 1.0 in the middle.
            var t = (double)i / (n - 1);          // 0..1
            weights[i] = 0.55 + 0.45 * Math.Sin(t * Math.PI);
        }
        return weights;
    }

    private void UpdateLevels(double level)
    {
        var hasReal = level >= 0.0;
        for (var i = 0; i < BarCount; i++)
        {
            // Per-bar wiggle so the row stays lively even at a steady loudness.
            var sine = (Math.Sin(_phase + PhaseOffsets[i]) + 1.0) / 2.0;

            double target;
            if (hasReal)
            {
                // Loudness envelope drives the height; the sine just adds motion.
                var envelope = level * BarWeights[i];
                target = envelope * (0.6 + 0.4 * sine);
            }
            else
            {
                // No real signal: fall back to the original decorative wave.
                target = sine * BarWeights[i];
            }

            // Fast attack, slower release for a natural bounce.
            var current = _levels[i];
            var rate = target > current ? 0.6 : 0.18;
            _levels[i] = current + (target - current) * rate;
        }
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
            else { _timer.Stop(); Array.Clear(_levels); InvalidateVisual(); }
        }
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        const int n = BarCount;
        const double gap = 4.0;
        var barW = Math.Max(2.0, (w - gap * (n - 1)) / n);

        for (var i = 0; i < n; i++)
        {
            // _levels is 0..1; reserve a small floor so bars never vanish.
            var barH = IsActive
                ? 3.0 + _levels[i] * (h - 4.0)
                : 3.0;

            var x = i * (barW + gap);
            var y = h - barH;
            ctx.FillRectangle(IsActive ? ActiveBrush : IdleBrush, new Rect(x, y, barW, barH), 2f);
        }
    }
}
