using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Lumyn.App.Models;
using Lumyn.Core.Services;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Lumyn.App.Views;

public enum SettingsSection
{
    WatchModes,
    Playback,
    Video,
    AudioClarity,
    Interface,
    Shortcuts,
    Transmux
}

public sealed record SettingsDialogResult(
    VideoAdjustments VideoAdjustments,
    WatchMode? WatchMode,
    AudioClarityMode AudioClarityMode,
    UiVisibilitySettings UiVisibility,
    bool ResumeAudio,
    bool ResumeVideo);

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

    private static readonly WatchModeChoice[] WatchModes =
    [
        new(
            WatchMode.Cinema,
            "Cinema",
            [
                new("Icon.Brightness", "+4%"),
                new("Icon.Contrast", "+8%"),
                new("Icon.Color", "+6%"),
                new("Icon.SeekForward", "10s"),
                new("Icon.SpeedGauge", "1x"),
                new("Icon.Subtitle", "Large outlined")
            ]),
        new(
            WatchMode.Lecture,
            "Lecture",
            [
                new("Icon.Brightness", "+8%"),
                new("Icon.Contrast", "+10%"),
                new("Icon.Color", "-5%"),
                new("Icon.SeekForward", "30s"),
                new("Icon.SpeedGauge", "1.25x"),
                new("Icon.Subtitle", "Yellow")
            ]),
        new(
            WatchMode.LanguageLearning,
            "Language Learning",
            [
                new("Icon.Brightness", "0%"),
                new("Icon.Contrast", "0%"),
                new("Icon.Color", "0%"),
                new("Icon.SeekForward", "5s"),
                new("Icon.SpeedGauge", "0.75x"),
                new("Icon.Subtitle", "Large yellow")
            ]),
        new(
            WatchMode.Night,
            "Night",
            [
                new("Icon.Brightness", "-15%"),
                new("Icon.Contrast", "-5%"),
                new("Icon.Color", "-10%"),
                new("Icon.SeekForward", "10s"),
                new("Icon.SpeedGauge", "1x"),
                new("Icon.Subtitle", "Grey")
            ]),
        new(
            WatchMode.MusicVideo,
            "Music Video",
            [
                new("Icon.Brightness", "+6%"),
                new("Icon.Contrast", "+12%"),
                new("Icon.Color", "+18%"),
                new("Icon.SeekForward", "10s"),
                new("Icon.SpeedGauge", "1x"),
                new("Icon.Subtitle", "Small")
            ])
    ];

    private static readonly AudioClarityChoice[] AudioClarityModes =
    [
        new(
            AudioClarityMode.Off,
            "Off",
            [
                new("Icon.VolumeHigh", "Original mix"),
                new("Icon.AudioTrack", "No filter")
            ]),
        new(
            AudioClarityMode.VoiceBoost,
            "Voice Boost",
            [
                new("Icon.VolumeHigh", "Low rumble -2dB"),
                new("Icon.SpeedGauge", "No compression"),
                new("Icon.AudioTrack", "Dialogue +4dB")
            ]),
        new(
            AudioClarityMode.LoudnessNormalize,
            "Loudness Normalize",
            [
                new("Icon.VolumeHigh", "Even volume"),
                new("Icon.SpeedGauge", "Dynamic normalize"),
                new("Icon.AudioTrack", "Best for mixed sources")
            ]),
        new(
            AudioClarityMode.QuietMode,
            "Quiet Mode",
            [
                new("Icon.VolumeHigh", "Softer peaks"),
                new("Icon.SpeedGauge", "Compression"),
                new("Icon.AudioTrack", "Voice lift")
            ])
    ];

    private static readonly VisibilityChoice[] InterfaceOptions =
    [
        new("ShowPin", "Always on top", "Icon.AlwaysOnTop", "Top bar pin button"),
        new("ShowCast", "Cast", "Icon.Cast", "Bottom bar cast button"),
        new("ShowLoop", "Loop", "Icon.Loop", "Bottom bar loop toggle"),
        new("ShowAbLoop", "A-B repeat", "Icon.Loop", "Bottom bar A-B repeat button (shortcut: R)"),
        new("ShowSeekStep", "Seek step", "Icon.SeekForward", "Bottom bar step-size control"),
        new("ShowMarkers", "Markers", "Icon.BookmarkOutline", "Bottom bar bookmarks/chapters button"),
        new("ShowScreenshot", "Screenshot", "Icon.Screenshot", "Bottom bar screenshot button")
    ];

    private readonly Action<VideoAdjustments>? _preview;
    private bool _suppressCallbacks;
    private int _brightness;
    private int _contrast;
    private int _saturation;
    private int _rotation;
    private int _zoomSlider;
    private VideoAspect _aspect;
    private WatchMode? _selectedWatchMode;
    private AudioClarityMode _selectedAudioClarityMode;
    private UiVisibilitySettings _uiVisibility;
    private bool _resumeAudio;
    private bool _resumeVideo;

    public SettingsDialog()
        : this(VideoAdjustments.Default, AudioClarityMode.Off, new UiVisibilitySettings(),
               resumeAudio: false, resumeVideo: true, null, SettingsSection.WatchModes)
    {
    }

    public SettingsDialog(
        VideoAdjustments current,
        AudioClarityMode currentAudioClarityMode,
        UiVisibilitySettings uiVisibility,
        bool resumeAudio,
        bool resumeVideo,
        Action<VideoAdjustments>? onPreview,
        SettingsSection initialSection)
    {
        AvaloniaXamlLoader.Load(this);

        _preview = onPreview;
        _brightness = current.Brightness;
        _contrast = current.Contrast;
        _saturation = current.Saturation;
        _rotation = current.Rotation;
        _zoomSlider = ZoomToSlider(current.Zoom);
        _aspect = current.Aspect;
        _selectedAudioClarityMode = currentAudioClarityMode;
        _uiVisibility = uiVisibility.Clone();
        _resumeAudio = resumeAudio;
        _resumeVideo = resumeVideo;

        var aspectCombo = this.FindControl<ComboBox>("AspectCombo")!;
        aspectCombo.ItemsSource = AspectChoices;

        PopulateWatchModes();
        PopulateAudioClarityModes();
        PopulateInterfaceOptions();
        PopulateShortcuts();
        InitPlaybackToggles();
        RefreshAll();
        ShowSection(initialSection);
        KeyDown += OnKeyDown;
    }

    private void InitPlaybackToggles()
    {
        // Set IsChecked before subscribing so the change handlers don't fire on init.
        if (this.FindControl<ToggleSwitch>("ResumeAudioToggle") is { } audio)
        {
            audio.IsChecked = _resumeAudio;
            audio.IsCheckedChanged += ResumeAudioToggle_IsCheckedChanged;
        }
        if (this.FindControl<ToggleSwitch>("ResumeVideoToggle") is { } video)
        {
            video.IsChecked = _resumeVideo;
            video.IsCheckedChanged += ResumeVideoToggle_IsCheckedChanged;
        }
    }

    private void ResumeAudioToggle_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch t) _resumeAudio = t.IsChecked == true;
    }

    private void ResumeVideoToggle_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch t) _resumeVideo = t.IsChecked == true;
    }

    private void WatchModesNavButton_Click(object? sender, RoutedEventArgs e) => ShowSection(SettingsSection.WatchModes);

    private void PlaybackNavButton_Click(object? sender, RoutedEventArgs e) => ShowSection(SettingsSection.Playback);

    private void VideoNavButton_Click(object? sender, RoutedEventArgs e) => ShowSection(SettingsSection.Video);

    private void AudioClarityNavButton_Click(object? sender, RoutedEventArgs e) => ShowSection(SettingsSection.AudioClarity);

    private void InterfaceNavButton_Click(object? sender, RoutedEventArgs e) => ShowSection(SettingsSection.Interface);

    private void ShortcutsNavButton_Click(object? sender, RoutedEventArgs e) => ShowSection(SettingsSection.Shortcuts);

    private void TransmuxNavButton_Click(object? sender, RoutedEventArgs e) => ShowSection(SettingsSection.Transmux);

    private void OpenTransmuxWebsite_Click(object? sender, RoutedEventArgs e)
        => OpenUrl("https://piyushdoorwar.github.io/transmux/");

    private void BrightnessSlider_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressCallbacks) return;
        ClearSelectedWatchMode();
        _brightness = (int)e.NewValue;
        UpdateLabel("BrightnessVal", FormatOffset(_brightness));
        _preview?.Invoke(BuildCurrent());
    }

    private void ContrastSlider_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressCallbacks) return;
        ClearSelectedWatchMode();
        _contrast = (int)e.NewValue;
        UpdateLabel("ContrastVal", FormatOffset(_contrast));
        _preview?.Invoke(BuildCurrent());
    }

    private void SaturationSlider_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressCallbacks) return;
        ClearSelectedWatchMode();
        _saturation = (int)e.NewValue;
        UpdateLabel("SaturationVal", FormatOffset(_saturation));
        _preview?.Invoke(BuildCurrent());
    }

    private void ZoomSlider_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressCallbacks) return;
        ClearSelectedWatchMode();
        _zoomSlider = (int)e.NewValue;
        UpdateLabel("ZoomVal", FormatZoom(_zoomSlider));
        _preview?.Invoke(BuildCurrent());
    }

    private void AspectCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressCallbacks) return;
        if (sender is ComboBox { SelectedItem: Choice choice })
        {
            ClearSelectedWatchMode();
            _aspect = choice.Value;
            _preview?.Invoke(BuildCurrent());
        }
    }

    private void RotButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || !int.TryParse(btn.Tag?.ToString(), out var deg)) return;

        ClearSelectedWatchMode();
        _rotation = deg;
        RefreshRotationButtons();
        _preview?.Invoke(BuildCurrent());
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e) =>
        Close(new SettingsDialogResult(BuildCurrent(), _selectedWatchMode, _selectedAudioClarityMode,
                                       _uiVisibility.Clone(), _resumeAudio, _resumeVideo));

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(null);

    private void ResetButton_Click(object? sender, RoutedEventArgs e)
    {
        _brightness = 0;
        _contrast = 0;
        _saturation = 0;
        _rotation = 0;
        _zoomSlider = 0;
        _aspect = VideoAspect.Auto;
        _selectedWatchMode = null;
        RefreshAll();
        RefreshWatchModeSelection();
        _preview?.Invoke(BuildCurrent());
    }

    private void ShowSection(SettingsSection section)
    {
        var isWatchModes = section == SettingsSection.WatchModes;
        var isPlayback = section == SettingsSection.Playback;
        var isVideo = section == SettingsSection.Video;
        var isAudioClarity = section == SettingsSection.AudioClarity;
        var isInterface = section == SettingsSection.Interface;
        var isTransmux = section == SettingsSection.Transmux;
        SetVisible("WatchModesPanel", isWatchModes);
        SetVisible("PlaybackPanel", isPlayback);
        SetVisible("VideoPanel", isVideo);
        SetVisible("AudioClarityPanel", isAudioClarity);
        SetVisible("InterfacePanel", isInterface);
        SetVisible("ShortcutsPanel", section == SettingsSection.Shortcuts);
        SetVisible("TransmuxPanel", isTransmux);
        MarkSelected("WatchModesNavButton", isWatchModes);
        MarkSelected("PlaybackNavButton", isPlayback);
        MarkSelected("VideoNavButton", isVideo);
        MarkSelected("AudioClarityNavButton", isAudioClarity);
        MarkSelected("InterfaceNavButton", isInterface);
        MarkSelected("ShortcutsNavButton", section == SettingsSection.Shortcuts);
        MarkSelected("TransmuxNavButton", isTransmux);
    }

    private void PopulateWatchModes()
    {
        var list = this.FindControl<StackPanel>("WatchModesList");
        if (list is null) return;

        foreach (var mode in WatchModes)
        {
            var button = new Button
            {
                Tag = mode.Mode,
                Content = new Grid
                {
                    RowDefinitions =
                    {
                        new RowDefinition(GridLength.Auto),
                        new RowDefinition(GridLength.Auto)
                    },
                    Children =
                    {
                        BuildModeText(mode),
                        BuildModeEffects(mode)
                    }
                }
            };
            button.Classes.Add("option");
            button.Click += WatchModeButton_Click;
            list.Children.Add(button);
        }
    }

    private void PopulateAudioClarityModes()
    {
        var list = this.FindControl<StackPanel>("AudioClarityList");
        if (list is null) return;

        foreach (var mode in AudioClarityModes)
        {
            var button = new Button
            {
                Tag = mode.Mode,
                Content = new Grid
                {
                    RowDefinitions =
                    {
                        new RowDefinition(GridLength.Auto),
                        new RowDefinition(GridLength.Auto)
                    },
                    Children =
                    {
                        BuildAudioClarityText(mode),
                        BuildAudioClarityEffects(mode)
                    }
                }
            };
            button.Classes.Add("option");
            button.Click += AudioClarityButton_Click;
            list.Children.Add(button);
        }

        RefreshAudioClaritySelection();
    }

    private static TextBlock BuildAudioClarityText(AudioClarityChoice mode)
    {
        var text = new TextBlock
        {
            Text = mode.Name,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        };
        text.Classes.Add("title");
        Grid.SetRow(text, 0);
        return text;
    }

    private static Grid BuildAudioClarityEffects(AudioClarityChoice mode)
    {
        var effects = new Grid
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        for (var i = 0; i < mode.Effects.Length; i++)
            effects.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(i == mode.Effects.Length - 1 ? 190 : 150)));

        for (var i = 0; i < mode.Effects.Length; i++)
        {
            var effect = BuildEffect(mode.Effects[i]);
            Grid.SetColumn(effect, i);
            effects.Children.Add(effect);
        }

        Grid.SetRow(effects, 1);
        return effects;
    }

    private void AudioClarityButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AudioClarityMode mode }) return;

        _selectedAudioClarityMode = mode;
        RefreshAudioClaritySelection();
    }

    private void PopulateInterfaceOptions()
    {
        var list = this.FindControl<StackPanel>("InterfaceOptionsList");
        if (list is null) return;

        foreach (var option in InterfaceOptions)
            list.Children.Add(BuildVisibilityOption(option));
    }

    private Control BuildVisibilityOption(VisibilityChoice option)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };

        var border = new Border { Child = grid };
        border.Classes.Add("card");

        if (Palette.Icon(option.IconKey) is { } geometry)
        {
            var path = new PathIcon
            {
                Data = geometry,
                Width = 16,
                Height = 16,
                Foreground = Palette.Muted,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(path, 0);
            grid.Children.Add(path);
        }

        var name = new TextBlock { Text = option.Name };
        name.Classes.Add("title");
        var description = new TextBlock { Text = option.Description };
        description.Classes.Add("hint");
        var text = new StackPanel { Spacing = 2, Children = { name, description } };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var toggle = new ToggleSwitch
        {
            IsChecked = GetVisibilityValue(option.PropertyName),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = option.PropertyName
        };
        toggle.IsCheckedChanged += VisibilityToggle_IsCheckedChanged;
        Grid.SetColumn(toggle, 2);
        grid.Children.Add(toggle);

        return border;
    }

    private void VisibilityToggle_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch { Tag: string propertyName } toggle) return;
        SetVisibilityValue(propertyName, toggle.IsChecked == true);
    }

    private static TextBlock BuildModeText(WatchModeChoice mode)
    {
        var text = new TextBlock
        {
            Text = mode.Name,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        };
        text.Classes.Add("title");
        Grid.SetRow(text, 0);
        return text;
    }

    private static Grid BuildModeEffects(WatchModeChoice mode)
    {
        var effects = new Grid
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        for (var i = 0; i < mode.Effects.Length; i++)
            effects.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(i == mode.Effects.Length - 1 ? 150 : 68)));

        for (var i = 0; i < mode.Effects.Length; i++)
        {
            var effect = BuildEffect(mode.Effects[i]);
            Grid.SetColumn(effect, i);
            effects.Children.Add(effect);
        }

        Grid.SetRow(effects, 1);
        return effects;
    }

    private static StackPanel BuildEffect(WatchModeEffect effect)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        if (Palette.Icon(effect.IconKey) is { } geometry)
        {
            panel.Children.Add(new PathIcon
            {
                Data = geometry,
                Width = 13,
                Height = 13,
                Foreground = Palette.Muted,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        panel.Children.Add(new TextBlock
        {
            Text = effect.Text,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = Palette.Text,
            VerticalAlignment = VerticalAlignment.Center
        });

        return panel;
    }

    private void WatchModeButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: WatchMode mode }) return;

        _selectedWatchMode = mode;
        ApplyWatchModeToDraft(mode);
        RefreshAll();
        RefreshWatchModeSelection();
        _preview?.Invoke(BuildCurrent());
    }

    private void ApplyWatchModeToDraft(WatchMode mode)
    {
        var video = mode switch
        {
            WatchMode.Cinema => new VideoAdjustments(4, 8, 6, 0, 0.0, VideoAspect.Auto),
            WatchMode.Lecture => new VideoAdjustments(8, 10, -5, 0, 0.0, VideoAspect.Auto),
            WatchMode.LanguageLearning => VideoAdjustments.Default,
            WatchMode.Night => new VideoAdjustments(-15, -5, -10, 0, 0.0, VideoAspect.Auto),
            WatchMode.MusicVideo => new VideoAdjustments(6, 12, 18, 0, 0.0, VideoAspect.Auto),
            _ => VideoAdjustments.Default
        };

        _brightness = video.Brightness;
        _contrast = video.Contrast;
        _saturation = video.Saturation;
        _rotation = video.Rotation;
        _zoomSlider = ZoomToSlider(video.Zoom);
        _aspect = video.Aspect;
    }

    private void RefreshWatchModeSelection()
    {
        var list = this.FindControl<StackPanel>("WatchModesList");
        if (list is null) return;

        foreach (var button in list.Children.OfType<Button>())
        {
            button.Classes.Set("selected", button.Tag is WatchMode mode && _selectedWatchMode == mode);
        }
    }

    private void RefreshAudioClaritySelection()
    {
        var list = this.FindControl<StackPanel>("AudioClarityList");
        if (list is null) return;

        foreach (var button in list.Children.OfType<Button>())
        {
            button.Classes.Set("selected", button.Tag is AudioClarityMode mode && _selectedAudioClarityMode == mode);
        }
    }

    private void ClearSelectedWatchMode()
    {
        if (_selectedWatchMode is null) return;
        _selectedWatchMode = null;
        RefreshWatchModeSelection();
    }

    private bool GetVisibilityValue(string propertyName) => propertyName switch
    {
        nameof(UiVisibilitySettings.ShowScreenshot) => _uiVisibility.ShowScreenshot,
        nameof(UiVisibilitySettings.ShowPin) => _uiVisibility.ShowPin,
        nameof(UiVisibilitySettings.ShowCast) => _uiVisibility.ShowCast,
        nameof(UiVisibilitySettings.ShowSeekStep) => _uiVisibility.ShowSeekStep,
        nameof(UiVisibilitySettings.ShowLoop) => _uiVisibility.ShowLoop,
        nameof(UiVisibilitySettings.ShowAbLoop) => _uiVisibility.ShowAbLoop,
        nameof(UiVisibilitySettings.ShowMarkers) => _uiVisibility.ShowMarkers,
        _ => true
    };

    private void SetVisibilityValue(string propertyName, bool value)
    {
        switch (propertyName)
        {
            case nameof(UiVisibilitySettings.ShowScreenshot):
                _uiVisibility.ShowScreenshot = value;
                break;
            case nameof(UiVisibilitySettings.ShowPin):
                _uiVisibility.ShowPin = value;
                break;
            case nameof(UiVisibilitySettings.ShowCast):
                _uiVisibility.ShowCast = value;
                break;
            case nameof(UiVisibilitySettings.ShowSeekStep):
                _uiVisibility.ShowSeekStep = value;
                break;
            case nameof(UiVisibilitySettings.ShowLoop):
                _uiVisibility.ShowLoop = value;
                break;
            case nameof(UiVisibilitySettings.ShowAbLoop):
                _uiVisibility.ShowAbLoop = value;
                break;
            case nameof(UiVisibilitySettings.ShowMarkers):
                _uiVisibility.ShowMarkers = value;
                break;
        }
    }

    private void PopulateShortcuts()
    {
        if (this.FindControl<StackPanel>("ShortcutsList") is { } list)
            ShortcutRows.Populate(list);
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
        MarkSelected("Rot0Btn", _rotation == 0);
        MarkSelected("Rot90Btn", _rotation == 90);
        MarkSelected("Rot180Btn", _rotation == 180);
        MarkSelected("Rot270Btn", _rotation == 270);
    }

    private void MarkSelected(string name, bool selected) =>
        this.FindControl<Button>(name)?.Classes.Set("selected", selected);

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

    private static void OpenUrl(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
            else
            {
                Process.Start("xdg-open", url);
            }
        }
        catch
        {
            // Opening the external browser is best-effort only.
        }
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

    private sealed record WatchModeChoice(
        WatchMode Mode,
        string Name,
        WatchModeEffect[] Effects);

    private sealed record WatchModeEffect(string IconKey, string Text);

    private sealed record AudioClarityChoice(
        AudioClarityMode Mode,
        string Name,
        WatchModeEffect[] Effects);

    private sealed record VisibilityChoice(
        string PropertyName,
        string Name,
        string IconKey,
        string Description);
}
