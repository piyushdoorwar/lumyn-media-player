using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Lumyn.App.Models;

namespace Lumyn.App.Views;

public enum SettingsSection
{
    Video,
    Shortcuts
}

public partial class SettingsDialog : Window
{
    private static readonly Choice[] AspectChoices =
    [
        new("Auto (from file)", VideoAspect.Auto),
        new("16:9",             VideoAspect.Ratio16x9),
        new("4:3",              VideoAspect.Ratio4x3),
        new("2.35:1 (cinema)",  VideoAspect.Ratio235x1),
        new("1:1 (square)",     VideoAspect.Square),
    ];

    private static readonly IBrush SelectedBg = new SolidColorBrush(Color.Parse("#3A9B4B"));
    private static readonly IBrush SelectedBdr = new SolidColorBrush(Color.Parse("#48B35A"));
    private static readonly IBrush NormalBg = new SolidColorBrush(Color.Parse("#3D3846"));
    private static readonly IBrush NormalBorder = new SolidColorBrush(Color.Parse("#5E5968"));
    private static readonly IBrush NavSelectedBg = new SolidColorBrush(Color.Parse("#263D2B"));
    private static readonly IBrush NavNormalBg = new SolidColorBrush(Color.Parse("#252525"));
    private static readonly IBrush NavSelectedBorder = new SolidColorBrush(Color.Parse("#3A9B4B"));
    private static readonly IBrush NavNormalBorder = new SolidColorBrush(Color.Parse("#353535"));

    private static readonly (string Key, string Action)[] Playback =
    [
        ("Space", "Play / Pause"),
        ("S", "Open subtitles dialog"),
        (".", "Step one frame forward"),
        (",", "Step one frame back"),
        ("Page Up", "Previous chapter"),
        ("Page Down", "Next chapter"),
        ("L", "Toggle loop"),
    ];

    private static readonly (string Key, string Action)[] Seeking =
    [
        ("Left", "Seek back (step)"),
        ("Right", "Seek forward (step)"),
        ("Ctrl + Left", "Seek back 30 s"),
        ("Ctrl + Right", "Seek forward 30 s"),
        ("click badge", "Cycle seek step: 5s / 10s / 30s"),
    ];

    private static readonly (string Key, string Action)[] Volume =
    [
        ("Up", "Volume up 5 %"),
        ("Down", "Volume down 5 %"),
        ("M", "Toggle mute"),
    ];

    private static readonly (string Key, string Action)[] Speed =
    [
        ("[", "Speed down"),
        ("]", "Speed up"),
        ("\\", "Reset speed to 1x"),
    ];

    private static readonly (string Key, string Action)[] Tracks =
    [
        ("A", "Cycle audio track"),
        ("V", "Cycle subtitle track"),
        ("N", "Next track in folder"),
        ("P", "Previous track in folder"),
    ];

    private static readonly (string Key, string Action)[] WindowShortcuts =
    [
        ("F", "Toggle fullscreen"),
        ("Escape", "Exit fullscreen"),
        ("T", "Toggle always on top"),
    ];

    private static readonly (string Key, string Action)[] FileAndDialogs =
    [
        ("O", "Open file"),
        ("Q", "Toggle queue / playlist"),
        ("B", "Open markers"),
        ("Ctrl + G", "Jump to time"),
        ("Alt + I", "Take screenshot"),
    ];

    private readonly Action<VideoAdjustments>? _preview;
    private bool _suppressCallbacks;
    private SettingsSection _section;
    private int _brightness;
    private int _contrast;
    private int _saturation;
    private int _rotation;
    private int _zoomSlider;
    private VideoAspect _aspect;

    public SettingsDialog()
        : this(VideoAdjustments.Default, null, SettingsSection.Video)
    {
    }

    public SettingsDialog(VideoAdjustments current, Action<VideoAdjustments>? onPreview, SettingsSection initialSection)
    {
        AvaloniaXamlLoader.Load(this);

        _preview = onPreview;
        _brightness = current.Brightness;
        _contrast = current.Contrast;
        _saturation = current.Saturation;
        _rotation = current.Rotation;
        _zoomSlider = ZoomToSlider(current.Zoom);
        _aspect = current.Aspect;

        var aspectCombo = this.FindControl<ComboBox>("AspectCombo")!;
        aspectCombo.ItemsSource = AspectChoices;

        PopulateShortcuts();
        RefreshAll();
        ShowSection(initialSection);
        KeyDown += OnKeyDown;
    }

    private void VideoNavButton_Click(object? sender, RoutedEventArgs e) => ShowSection(SettingsSection.Video);

    private void ShortcutsNavButton_Click(object? sender, RoutedEventArgs e) => ShowSection(SettingsSection.Shortcuts);

    private void BrightnessSlider_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressCallbacks) return;
        _brightness = (int)e.NewValue;
        UpdateLabel("BrightnessVal", FormatOffset(_brightness));
        _preview?.Invoke(BuildCurrent());
    }

    private void ContrastSlider_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressCallbacks) return;
        _contrast = (int)e.NewValue;
        UpdateLabel("ContrastVal", FormatOffset(_contrast));
        _preview?.Invoke(BuildCurrent());
    }

    private void SaturationSlider_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressCallbacks) return;
        _saturation = (int)e.NewValue;
        UpdateLabel("SaturationVal", FormatOffset(_saturation));
        _preview?.Invoke(BuildCurrent());
    }

    private void ZoomSlider_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressCallbacks) return;
        _zoomSlider = (int)e.NewValue;
        UpdateLabel("ZoomVal", FormatZoom(_zoomSlider));
        _preview?.Invoke(BuildCurrent());
    }

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
        if (sender is not Button btn || !int.TryParse(btn.Tag?.ToString(), out var deg)) return;

        _rotation = deg;
        RefreshRotationButtons();
        _preview?.Invoke(BuildCurrent());
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e) => Close(BuildCurrent());

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(null);

    private void ResetButton_Click(object? sender, RoutedEventArgs e)
    {
        _brightness = 0;
        _contrast = 0;
        _saturation = 0;
        _rotation = 0;
        _zoomSlider = 0;
        _aspect = VideoAspect.Auto;
        RefreshAll();
        _preview?.Invoke(BuildCurrent());
    }

    private void ShowSection(SettingsSection section)
    {
        _section = section;
        var isVideo = section == SettingsSection.Video;
        SetVisible("VideoPanel", isVideo);
        SetVisible("ShortcutsPanel", !isVideo);
        MarkNavSelected("VideoNavButton", isVideo);
        MarkNavSelected("ShortcutsNavButton", !isVideo);
    }

    private void PopulateShortcuts()
    {
        PopulateGroup("PlaybackGroup", Playback);
        PopulateGroup("SeekingGroup", Seeking);
        PopulateGroup("VolumeGroup", Volume);
        PopulateGroup("SpeedGroup", Speed);
        PopulateGroup("TracksGroup", Tracks);
        PopulateGroup("WindowGroup", WindowShortcuts);
        PopulateGroup("FileGroup", FileAndDialogs);
    }

    private void PopulateGroup(string gridName, (string Key, string Action)[] rows)
    {
        var grid = this.FindControl<Grid>(gridName);
        if (grid is null) return;

        for (var i = 0; i < rows.Length; i++)
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(43)));

        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(178)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        for (var i = 0; i < rows.Length; i++)
        {
            var (key, action) = rows[i];
            var rowBg = new Border
            {
                Background = new SolidColorBrush(Color.Parse(i % 2 == 0 ? "#161616" : "#1A1A1A")),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 1),
            };
            Grid.SetRow(rowBg, i);
            Grid.SetColumnSpan(rowBg, 2);
            grid.Children.Add(rowBg);

            var keyBorder = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#2D2D2D")),
                BorderBrush = new SolidColorBrush(Color.Parse("#4D4658")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 3),
                Margin = new Thickness(10, 6),
                MinHeight = 28,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = key,
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(Color.Parse("#E8E4E0")),
                    FontFamily = new FontFamily("Courier New,Liberation Mono,monospace"),
                }
            };
            Grid.SetRow(keyBorder, i);
            Grid.SetColumn(keyBorder, 0);
            grid.Children.Add(keyBorder);

            var actionText = new TextBlock
            {
                Text = action,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.Parse("#DEDAD5")),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 10, 0),
            };
            Grid.SetRow(actionText, i);
            Grid.SetColumn(actionText, 1);
            grid.Children.Add(actionText);
        }
    }

    private void RefreshAll()
    {
        _suppressCallbacks = true;
        try
        {
            SetSlider("BrightnessSlider", _brightness);
            SetSlider("ContrastSlider", _contrast);
            SetSlider("SaturationSlider", _saturation);
            SetSlider("ZoomSlider", _zoomSlider);

            UpdateLabel("BrightnessVal", FormatOffset(_brightness));
            UpdateLabel("ContrastVal", FormatOffset(_contrast));
            UpdateLabel("SaturationVal", FormatOffset(_saturation));
            UpdateLabel("ZoomVal", FormatZoom(_zoomSlider));

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

    private VideoAdjustments BuildCurrent() =>
        new(_brightness, _contrast, _saturation, _rotation, SliderToZoom(_zoomSlider), _aspect);

    private void RefreshRotationButtons()
    {
        MarkRotSelected("Rot0Btn", _rotation == 0);
        MarkRotSelected("Rot90Btn", _rotation == 90);
        MarkRotSelected("Rot180Btn", _rotation == 180);
        MarkRotSelected("Rot270Btn", _rotation == 270);
    }

    private void MarkRotSelected(string name, bool selected)
    {
        var btn = this.FindControl<Button>(name);
        if (btn is null) return;
        btn.Background = selected ? SelectedBg : NormalBg;
        btn.BorderBrush = selected ? SelectedBdr : NormalBorder;
        btn.Foreground = Brushes.White;
    }

    private void MarkNavSelected(string name, bool selected)
    {
        var btn = this.FindControl<Button>(name);
        if (btn is null) return;
        btn.Background = selected ? NavSelectedBg : NavNormalBg;
        btn.BorderBrush = selected ? NavSelectedBorder : NavNormalBorder;
    }

    private void SetVisible(string name, bool visible)
    {
        var control = this.FindControl<Control>(name);
        if (control is not null)
            control.IsVisible = visible;
    }

    private void SetSlider(string name, double value)
    {
        var slider = this.FindControl<Slider>(name);
        if (slider is not null)
            slider.Value = value;
    }

    private void UpdateLabel(string name, string text)
    {
        var tb = this.FindControl<TextBlock>(name);
        if (tb is not null)
            tb.Text = text;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        Close(null);
        e.Handled = true;
    }

    private static string FormatOffset(int value) =>
        value == 0 ? "0" : value > 0 ? $"+{value}" : $"{value}";

    private static string FormatZoom(int sliderVal) =>
        $"{Math.Pow(2, sliderVal / 100.0) * 100:F0}%";

    private static double SliderToZoom(int sliderVal) => sliderVal / 100.0;

    private static int ZoomToSlider(double zoom) => (int)Math.Round(zoom * 100);

    private sealed record Choice(string Label, VideoAspect Value)
    {
        public override string ToString() => Label;
    }
}
