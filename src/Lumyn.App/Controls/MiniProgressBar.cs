using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Lumyn.App.Controls;

public sealed class MiniProgressBar : Control
{
    private static readonly IBrush TrackBrush = new SolidColorBrush(Color.Parse("#2A2A2A"));
    private static readonly IBrush FillBrush = new SolidColorBrush(Color.Parse("#3A9B4B"));

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<MiniProgressBar, double>(nameof(Minimum), 0);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<MiniProgressBar, double>(nameof(Maximum), 100);

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<MiniProgressBar, double>(nameof(Value));

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    static MiniProgressBar()
    {
        AffectsRender<MiniProgressBar>(MinimumProperty, MaximumProperty, ValueProperty);
        FocusableProperty.OverrideDefaultValue<MiniProgressBar>(false);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 96 : availableSize.Width;
        return new Size(width, 3);
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0) return;

        var radius = height / 2;
        var track = new Rect(0, 0, width, height);
        var fillWidth = width * GetRatio();

        context.DrawRectangle(TrackBrush, null, track, radius, radius);

        if (fillWidth > 0)
            context.DrawRectangle(FillBrush, null, new Rect(0, 0, fillWidth, height), radius, radius);
    }

    private double GetRatio()
    {
        var range = Maximum - Minimum;
        if (range <= 0) return 0;
        return Math.Clamp((Value - Minimum) / range, 0, 1);
    }
}
