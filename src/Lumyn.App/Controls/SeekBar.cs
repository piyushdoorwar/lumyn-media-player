using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using System.Collections.Generic;

namespace Lumyn.App.Controls;

public sealed class SeekBar : Control
{
    private const double HitTargetHeight = 32.0;
    private const double TrackHeight = 5.0;

    private static readonly IBrush HitTestBrush = new SolidColorBrush(Colors.Transparent);
    private static readonly IBrush TrackBrush   = new SolidColorBrush(Color.Parse("#554A4A4A"));
    private static readonly IBrush FillBrush    = new SolidColorBrush(Color.Parse("#3A9B4B"));
    private static readonly IBrush ChapterBrush = new SolidColorBrush(Color.Parse("#80F7F5F3"));
    private static readonly IBrush LoopRegionBrush = new SolidColorBrush(Color.Parse("#5549B35C"));
    private static readonly IBrush LoopMarkerBrush = new SolidColorBrush(Color.Parse("#7BD88E"));

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<SeekBar, double>(nameof(Minimum), 0);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<SeekBar, double>(nameof(Maximum), 1000);

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<SeekBar, double>(
            nameof(Value),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    // List of chapter start positions in the same units as Value (e.g. seconds).
    // First chapter is at 0 and is not drawn (it would sit on the left edge).
    public static readonly StyledProperty<IReadOnlyList<double>> ChapterPositionsProperty =
        AvaloniaProperty.Register<SeekBar, IReadOnlyList<double>>(
            nameof(ChapterPositions),
            defaultValue: []);

    // A-B loop markers, in the same units as Value (-1 = unset). The region
    // between them is highlighted and each point gets a vertical tick.
    public static readonly StyledProperty<double> LoopStartProperty =
        AvaloniaProperty.Register<SeekBar, double>(nameof(LoopStart), -1);

    public static readonly StyledProperty<double> LoopEndProperty =
        AvaloniaProperty.Register<SeekBar, double>(nameof(LoopEnd), -1);

    public static readonly RoutedEvent<RoutedEventArgs> SeekCommittedEvent =
        RoutedEvent.Register<SeekBar, RoutedEventArgs>(
            nameof(SeekCommitted),
            RoutingStrategies.Bubble);

    private bool _isDragging;
    private double _displayValue;

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

    public IReadOnlyList<double> ChapterPositions
    {
        get => GetValue(ChapterPositionsProperty);
        set => SetValue(ChapterPositionsProperty, value);
    }

    public double LoopStart
    {
        get => GetValue(LoopStartProperty);
        set => SetValue(LoopStartProperty, value);
    }

    public double LoopEnd
    {
        get => GetValue(LoopEndProperty);
        set => SetValue(LoopEndProperty, value);
    }

    public event EventHandler<RoutedEventArgs>? SeekCommitted
    {
        add => AddHandler(SeekCommittedEvent, value);
        remove => RemoveHandler(SeekCommittedEvent, value);
    }

    static SeekBar()
    {
        AffectsRender<SeekBar>(ValueProperty, MinimumProperty, MaximumProperty, ChapterPositionsProperty,
            LoopStartProperty, LoopEndProperty);
        FocusableProperty.OverrideDefaultValue<SeekBar>(false);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ValueProperty && !_isDragging)
            _displayValue = Value;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 240 : availableSize.Width;
        return new Size(width, HitTargetHeight);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _displayValue = Value;
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0) return;

        var trackY = Math.Max(0, (height - TrackHeight) / 2);
        var track = new Rect(0, trackY, width, TrackHeight);
        var radius = TrackHeight / 2;
        var ratio = GetRatio();
        var filled = new Rect(0, trackY, width * ratio, TrackHeight);
        var thumbX = width * ratio;

        context.DrawRectangle(HitTestBrush, null, new Rect(0, 0, width, height));
        context.DrawRectangle(TrackBrush, null, track, radius, radius);
        context.DrawRectangle(FillBrush, null, filled, radius, radius);

        // Chapter tick marks — 1 px wide, slightly taller than the track
        var chapters = ChapterPositions;
        if (chapters is { Count: > 0 })
        {
            var range = Maximum - Minimum;
            const double tickH = 8.0;
            var tickY = (height - tickH) / 2;
            if (range > 0)
            {
                foreach (var pos in chapters)
                {
                    // Skip the very start (chapter 1 at 0) and out-of-range values
                    if (pos <= Minimum || pos >= Maximum) continue;
                    var tickX = Math.Round(width * (pos - Minimum) / range);
                    context.DrawRectangle(ChapterBrush, null,
                        new Rect(tickX - 0.5, tickY, 1, tickH));
                }
            }
        }

        // A-B loop markers (only drawn once a point is set).
        var loopRange = Maximum - Minimum;
        if (loopRange > 0)
        {
            double? aX = LoopStart >= Minimum ? width * (LoopStart - Minimum) / loopRange : null;
            double? bX = LoopEnd   >= Minimum ? width * (LoopEnd   - Minimum) / loopRange : null;

            if (aX is { } a && bX is { } b && b > a)
                context.DrawRectangle(LoopRegionBrush, null,
                    new Rect(a, trackY, b - a, TrackHeight));

            const double markH = 13.0;
            var markY = (height - markH) / 2;
            if (aX is { } ax) context.DrawRectangle(LoopMarkerBrush, null, new Rect(ax - 1, markY, 2, markH));
            if (bX is { } bx) context.DrawRectangle(LoopMarkerBrush, null, new Rect(bx - 1, markY, 2, markH));
        }
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
        Commit(e.Pointer);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (!_isDragging) return;

        _isDragging = false;
        RaiseEvent(new RoutedEventArgs(SeekCommittedEvent));
    }

    private void Commit(IPointer pointer)
    {
        _isDragging = false;
        pointer.Capture(null);
        Value = _displayValue;
        RaiseEvent(new RoutedEventArgs(SeekCommittedEvent));
    }

    private void SetValueFromPointer(PointerEventArgs e)
    {
        if (Bounds.Width <= 0) return;

        var point = e.GetPosition(this);
        var ratio = Math.Clamp(point.X / Bounds.Width, 0, 1);
        _displayValue = Minimum + ratio * (Maximum - Minimum);
        InvalidateVisual();
    }

    private double GetRatio()
    {
        var range = Maximum - Minimum;
        if (range <= 0) return 0;
        var value = _isDragging ? _displayValue : Value;
        return Math.Clamp((value - Minimum) / range, 0, 1);
    }
}
