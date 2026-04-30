using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Lumyn.App.Models;

namespace Lumyn.App.Views;

public partial class VideoAdjustmentsDialog : Window
{
    private static readonly Choice[] AspectChoices =
    [
        new("Auto (from file)", VideoAspect.Auto),
        new("16:9",             VideoAspect.Ratio16x9),
        new("4:3",              VideoAspect.Ratio4x3),
        new("2.35:1 (cinema)",  VideoAspect.Ratio235x1),
        new("1:1 (square)",     VideoAspect.Square),
    ];

    private static readonly IBrush SelectedBg   = new SolidColorBrush(Color.Parse("#3A9B4B"));
    private static readonly IBrush SelectedBdr   = new SolidColorBrush(Color.Parse("#48B35A"));
    private static readonly IBrush NormalBg      = new SolidColorBrush(Color.Parse("#3D3846"));
    private static readonly IBrush NormalBorder  = new SolidColorBrush(Color.Parse("#5E5968"));

    // ── Current state ────────────────────────────────────────────────────────
    private int         _brightness;
    private int         _contrast;
    private int         _saturation;
    private int         _rotation;
    private int         _zoomSlider; // slider integer (-100..100), maps to log₂ zoom / 100
    private VideoAspect _aspect;

    private readonly Action<VideoAdjustments>? _preview;
    private bool _suppressCallbacks;

    public VideoAdjustmentsDialog()
        : this(VideoAdjustments.Default, null) { }

    public VideoAdjustmentsDialog(VideoAdjustments current, Action<VideoAdjustments>? onPreview)
    {
        AvaloniaXamlLoader.Load(this);

        _preview    = onPreview;
        _brightness = current.Brightness;
        _contrast   = current.Contrast;
        _saturation = current.Saturation;
        _rotation   = current.Rotation;
        _zoomSlider = ZoomToSlider(current.Zoom);
        _aspect     = current.Aspect;

        var aspectCombo = this.FindControl<ComboBox>("AspectCombo")!;
        aspectCombo.ItemsSource = AspectChoices;

        RefreshAll();
    }

    // ── Slider event handlers ────────────────────────────────────────────────

    private void BrightnessSlider_Changed(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressCallbacks) return;
        _brightness = (int)e.NewValue;
        UpdateLabel("BrightnessVal", FormatOffset(_brightness));
        _preview?.Invoke(BuildCurrent());
    }

    private void ContrastSlider_Changed(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressCallbacks) return;
        _contrast = (int)e.NewValue;
        UpdateLabel("ContrastVal", FormatOffset(_contrast));
        _preview?.Invoke(BuildCurrent());
    }

    private void SaturationSlider_Changed(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressCallbacks) return;
        _saturation = (int)e.NewValue;
        UpdateLabel("SaturationVal", FormatOffset(_saturation));
        _preview?.Invoke(BuildCurrent());
    }

    private void ZoomSlider_Changed(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressCallbacks) return;
        _zoomSlider = (int)e.NewValue;
        UpdateLabel("ZoomVal", FormatZoom(_zoomSlider));
        _preview?.Invoke(BuildCurrent());
    }

    // ── Combo / button handlers ──────────────────────────────────────────────

    private void AspectCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressCallbacks) return;
        if (sender is ComboBox { SelectedItem: Choice choice })
        {
            _aspect = choice.Value;
            _preview?.Invoke(BuildCurrent());
        }
    }

    private void RotButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn &&
            int.TryParse(btn.Tag?.ToString(), out var deg))
        {
            _rotation = deg;
            RefreshRotationButtons();
            _preview?.Invoke(BuildCurrent());
        }
    }

    // ── Action buttons ────────────────────────────────────────────────────────

    private void OkButton_Click(object? sender, RoutedEventArgs e)
        => Close(BuildCurrent());

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
        => Close(null);

    private void ResetButton_Click(object? sender, RoutedEventArgs e)
    {
        _brightness = 0;
        _contrast   = 0;
        _saturation = 0;
        _rotation   = 0;
        _zoomSlider = 0;
        _aspect     = VideoAspect.Auto;
        RefreshAll();
        _preview?.Invoke(BuildCurrent());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private VideoAdjustments BuildCurrent() =>
        new(_brightness, _contrast, _saturation, _rotation, SliderToZoom(_zoomSlider), _aspect);

    private void RefreshAll()
    {
        _suppressCallbacks = true;
        try
        {
            SetSlider("BrightnessSlider", _brightness);
            SetSlider("ContrastSlider",   _contrast);
            SetSlider("SaturationSlider", _saturation);
            SetSlider("ZoomSlider",       _zoomSlider);

            UpdateLabel("BrightnessVal", FormatOffset(_brightness));
            UpdateLabel("ContrastVal",   FormatOffset(_contrast));
            UpdateLabel("SaturationVal", FormatOffset(_saturation));
            UpdateLabel("ZoomVal",       FormatZoom(_zoomSlider));

            var combo = this.FindControl<ComboBox>("AspectCombo");
            if (combo is not null)
                combo.SelectedIndex = Math.Max(0, Array.FindIndex(AspectChoices, c => c.Value == _aspect));

            RefreshRotationButtons();
        }
        finally
        {
            _suppressCallbacks = false;
        }
    }

    private void RefreshRotationButtons()
    {
        MarkRotSelected("Rot0Btn",   _rotation == 0);
        MarkRotSelected("Rot90Btn",  _rotation == 90);
        MarkRotSelected("Rot180Btn", _rotation == 180);
        MarkRotSelected("Rot270Btn", _rotation == 270);
    }

    private void MarkRotSelected(string name, bool selected)
    {
        var btn = this.FindControl<Button>(name);
        if (btn is null) return;
        btn.Background  = selected ? SelectedBg    : NormalBg;
        btn.BorderBrush = selected ? SelectedBdr   : NormalBorder;
        btn.Foreground  = new SolidColorBrush(Colors.White);
    }

    private void SetSlider(string name, double value)
    {
        var s = this.FindControl<Slider>(name);
        if (s is not null) s.Value = value;
    }

    private void UpdateLabel(string name, string text)
    {
        var tb = this.FindControl<TextBlock>(name);
        if (tb is not null) tb.Text = text;
    }

    private static string FormatOffset(int v) =>
        v == 0 ? "0" : v > 0 ? $"+{v}" : $"{v}";

    // Slider -100..100  →  zoom percentage label
    // zoom = log₂(scale), so scale = 2^(slider/100), display = scale*100 %
    private static string FormatZoom(int sliderVal) =>
        $"{Math.Pow(2, sliderVal / 100.0) * 100:F0}%";

    private static double SliderToZoom(int sliderVal) => sliderVal / 100.0;
    private static int    ZoomToSlider(double zoom)   => (int)Math.Round(zoom * 100);

    private sealed record Choice(string Label, VideoAspect Value)
    {
        public override string ToString() => Label;
    }
}
