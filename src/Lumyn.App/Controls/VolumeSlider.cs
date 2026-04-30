using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Lumyn.App.Controls;

public sealed class VolumeSlider : Control
{
    private const double NormalMax = 100;

    private static readonly IBrush TrackBrush = new SolidColorBrush(Color.Parse("#554A4A4A"));
    private static readonly IBrush FillBrush = new SolidColorBrush(Color.Parse("#3A9B4B"));
    private static readonly IBrush BoostFillBrush = new SolidColorBrush(Color.Parse("#D94A45"));
    private static readonly IBrush BoostTrackBrush = new SolidColorBrush(Color.Parse("#5531262A"));
    private static readonly IBrush ThumbBrush = new SolidColorBrush(Color.Parse("#F7F5F3"));
    private static readonly IPen ThumbPen = new Pen(new SolidColorBrush(Color.Parse("#33111111")), 1);
    private static readonly IPen NormalMaxPen = new Pen(new SolidColorBrush(Color.Parse("#88DEDAD5")), 1);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<VolumeSlider, double>(nameof(Minimum), 0);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<VolumeSlider, double>(nameof(Maximum), 150);

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<VolumeSlider, double>(
            nameof(Value),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    private bool _isDragging;

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

    static VolumeSlider()
    {
        AffectsRender<VolumeSlider>(ValueProperty, MinimumProperty, MaximumProperty);
        FocusableProperty.OverrideDefaultValue<VolumeSlider>(false);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 96 : availableSize.Width;
        return new Size(width, 18);
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0) return;

        var trackHeight = 4.0;
        var trackY = (height - trackHeight) / 2;
        var radius = trackHeight / 2;
        var track = new Rect(0, trackY, width, trackHeight);
        var normalX = width * GetRatio(NormalMax);
        var valueX = width * GetRatio(Value);
        var normalFillX = Math.Min(valueX, normalX);

        context.DrawRectangle(TrackBrush, null, track, radius, radius);

        if (normalX < width)
            context.DrawRectangle(BoostTrackBrush, null, new Rect(normalX, trackY, width - normalX, trackHeight), radius, radius);

        if (normalFillX > 0)
            context.DrawRectangle(FillBrush, null, new Rect(0, trackY, normalFillX, trackHeight), radius, radius);

        if (valueX > normalX)
            context.DrawRectangle(BoostFillBrush, null, new Rect(normalX, trackY, valueX - normalX, trackHeight), radius, radius);

        context.DrawLine(NormalMaxPen, new Point(normalX, trackY - 3), new Point(normalX, trackY + trackHeight + 3));
        context.DrawEllipse(ThumbBrush, ThumbPen, new Point(valueX, height / 2), 6, 6);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _isDragging = true;
        e.Pointer.Capture(this);
        SetValueFromPointer(e);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isDragging) return;

        SetValueFromPointer(e);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_isDragging) return;

        SetValueFromPointer(e);
        _isDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _isDragging = false;
    }

    private void SetValueFromPointer(PointerEventArgs e)
    {
        if (Bounds.Width <= 0) return;

        var point = e.GetPosition(this);
        var ratio = Math.Clamp(point.X / Bounds.Width, 0, 1);
        Value = Minimum + ratio * (Maximum - Minimum);
    }

    private double GetRatio(double value)
    {
        var range = Maximum - Minimum;
        if (range <= 0) return 0;
        return Math.Clamp((value - Minimum) / range, 0, 1);
    }
}
