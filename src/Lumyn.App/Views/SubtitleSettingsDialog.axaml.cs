using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Lumyn.App.Models;

namespace Lumyn.App.Views;

public partial class SubtitleSettingsDialog : Window
{
    // ── Step size for the delay ± buttons (ms) ───────────────────────────────
    private const long DelayStep = 500;

    // ── Current values ───────────────────────────────────────────────────────
    private string?          _filePath;
    private SubtitleFontSize _fontSize  = SubtitleFontSize.Medium;
    private SubtitleFont     _font      = SubtitleFont.SansSerif;
    private SubtitleColor    _color     = SubtitleColor.White;
    private long             _delayMs   = 0;

    // ── Colour lookup ────────────────────────────────────────────────────────
    private static readonly IBrush SelectedBorder = new SolidColorBrush(Color.Parse("#E95420"));
    private static readonly IBrush NormalBorder   = new SolidColorBrush(Color.Parse("#666666"));

    public SubtitleSettingsDialog() : this(new SubtitleSettings(null,
        SubtitleFontSize.Medium, SubtitleFont.SansSerif, SubtitleColor.White, 0)) { }

    public SubtitleSettingsDialog(SubtitleSettings current)
    {
        AvaloniaXamlLoader.Load(this);

        _filePath = current.FilePath;
        _fontSize = current.FontSize;
        _font     = current.Font;
        _color    = current.Color;
        _delayMs  = current.DelayMs;

        RefreshAll();
    }

    // ── Event handlers ───────────────────────────────────────────────────────

    private async void BrowseButton_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load subtitle file",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Subtitle files")
                {
                    Patterns = ["*.srt", "*.ass", "*.ssa", "*.vtt", "*.sub"]
                },
                FilePickerFileTypes.All
            ]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            _filePath = path;
            RefreshFilePath();
        }
    }

    private void SizeButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && Enum.TryParse<SubtitleFontSize>(btn.Tag?.ToString(), out var size))
        {
            _fontSize = size;
            RefreshSizeButtons();
        }
    }

    private void FontButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && Enum.TryParse<SubtitleFont>(btn.Tag?.ToString(), out var font))
        {
            _font = font;
            RefreshFontButtons();
        }
    }

    private void ColorButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && Enum.TryParse<SubtitleColor>(btn.Tag?.ToString(), out var color))
        {
            _color = color;
            RefreshColorButtons();
        }
    }

    private void DelayMinus_Click(object? sender, RoutedEventArgs e)
    {
        _delayMs -= DelayStep;
        RefreshDelayLabel();
    }

    private void DelayPlus_Click(object? sender, RoutedEventArgs e)
    {
        _delayMs += DelayStep;
        RefreshDelayLabel();
    }

    private void ApplyButton_Click(object? sender, RoutedEventArgs e)
        => Close(new SubtitleSettings(_filePath, _fontSize, _font, _color, _delayMs));

    private void DisableButton_Click(object? sender, RoutedEventArgs e)
        // FilePath = null signals "clear / disable" to the ViewModel
        => Close(new SubtitleSettings(null, _fontSize, _font, _color, 0));

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
        => Close(null);

    // ── Refresh helpers ──────────────────────────────────────────────────────

    private void RefreshAll()
    {
        RefreshFilePath();
        RefreshSizeButtons();
        RefreshFontButtons();
        RefreshColorButtons();
        RefreshDelayLabel();
    }

    private void RefreshFilePath()
    {
        var box = this.FindControl<TextBox>("FilePathBox");
        if (box is not null)
            box.Text = _filePath is null ? string.Empty : System.IO.Path.GetFileName(_filePath);
    }

    private void RefreshSizeButtons()
    {
        MarkSelected("SizeSmallBtn",  _fontSize == SubtitleFontSize.Small);
        MarkSelected("SizeMediumBtn", _fontSize == SubtitleFontSize.Medium);
        MarkSelected("SizeLargeBtn",  _fontSize == SubtitleFontSize.Large);
    }

    private void RefreshFontButtons()
    {
        MarkSelected("FontSansBtn",  _font == SubtitleFont.SansSerif);
        MarkSelected("FontSerifBtn", _font == SubtitleFont.Serif);
        MarkSelected("FontMonoBtn",  _font == SubtitleFont.Monospace);
        MarkSelected("FontArialBtn", _font == SubtitleFont.Arial);
    }

    private void RefreshColorButtons()
    {
        MarkColorSelected("ColorWhiteBtn",       _color == SubtitleColor.White);
        MarkColorSelected("ColorWhiteBorderBtn", _color == SubtitleColor.WhiteWithBorder);
        MarkColorSelected("ColorYellowBtn",      _color == SubtitleColor.Yellow);
        MarkColorSelected("ColorGreyBtn",        _color == SubtitleColor.Grey);
        MarkColorSelected("ColorBlackBtn",       _color == SubtitleColor.Black);
    }

    private void RefreshDelayLabel()
    {
        var label = this.FindControl<TextBlock>("DelayLabel");
        if (label is not null)
            label.Text = _delayMs == 0 ? "0 ms" : $"{_delayMs:+0;-0} ms";
    }

    /// <summary>Highlights a regular (text) button as selected / unselected.</summary>
    private void MarkSelected(string name, bool selected)
    {
        var btn = this.FindControl<Button>(name);
        if (btn is null) return;
        btn.Background  = selected ? SelectedBorder : new SolidColorBrush(Color.Parse("#3D3846"));
        btn.BorderBrush = selected ? SelectedBorder : new SolidColorBrush(Color.Parse("#5E5968"));
        btn.Foreground  = new SolidColorBrush(Colors.White);
    }

    /// <summary>Highlights a colour swatch button with a thick accent ring.</summary>
    private void MarkColorSelected(string name, bool selected)
    {
        var btn = this.FindControl<Button>(name);
        if (btn is null) return;
        btn.BorderBrush     = selected ? SelectedBorder : NormalBorder;
        btn.BorderThickness = selected ? new Thickness(3) : new Thickness(1);
    }
}
