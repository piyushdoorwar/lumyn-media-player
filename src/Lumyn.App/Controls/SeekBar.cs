using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using System.Collections.Generic;

namespace Lumyn.App.Controls;

public sealed class SeekBar : Control
{
    private static readonly IBrush TrackBrush   = new SolidColorBrush(Color.Parse("#554A4A4A"));
    private static readonly IBrush FillBrush    = new SolidColorBrush(Color.Parse("#3A9B4B"));
    private static readonly IBrush ChapterBrush = new SolidColorBrush(Color.Parse("#80F7F5F3"));

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

    public event EventHandler<RoutedEventArgs>? SeekCommitted
    {
        add => AddHandler(SeekCommittedEvent, value);
        remove => RemoveHandler(SeekCommittedEvent, value);
    }

    static SeekBar()
    {
        AffectsRender<SeekBar>(ValueProperty, MinimumProperty, MaximumProperty, ChapterPositionsProperty);
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
        return new Size(width, 18);
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

        var trackHeight = 5.0;
        var trackY = (height - trackHeight) / 2;
        var track = new Rect(0, trackY, width, trackHeight);
        var radius = trackHeight / 2;
        var ratio = GetRatio();
        var filled = new Rect(0, trackY, width * ratio, trackHeight);
        var thumbX = width * ratio;

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
