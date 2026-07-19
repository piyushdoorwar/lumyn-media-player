using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Lumyn.App.Models;
using Lumyn.App.ViewModels;
using Lumyn.Core.Models;
using Lumyn.Core.Services;

namespace Lumyn.App.Views;

public partial class SubtitleSettingsDialog : Window
{
    private const long DelayStep = 500;
    private static readonly Choice<SubtitleFont>[] FontChoices =
    [
        new("Sans-serif (default)", SubtitleFont.SansSerif),
        new("Serif", SubtitleFont.Serif),
        new("Monospace", SubtitleFont.Monospace),
        new("Arial", SubtitleFont.Arial)
    ];

    private static readonly Choice<SubtitleColor>[] ColorChoices =
    [
        new("White", SubtitleColor.White),
        new("White (outlined)", SubtitleColor.WhiteWithBorder),
        new("Yellow", SubtitleColor.Yellow),
        new("Grey", SubtitleColor.Grey),
        new("Black", SubtitleColor.Black)
    ];

    // ── Settings state ───────────────────────────────────────────────────────
    private string?          _filePath;
    private SubtitleFontSize _fontSize = SubtitleFontSize.Medium;
    private SubtitleFont     _font     = SubtitleFont.SansSerif;
    private SubtitleColor    _color    = SubtitleColor.White;
    private long             _delayMs  = 0;
    private int?             _embeddedTrackId;
    private readonly TrackInfo[] _embeddedSubtitleTracks;

    // ── Search state ─────────────────────────────────────────────────────────
    private readonly SubtitleSearchService _service = new();
    private CancellationTokenSource?       _cts;
    private SubtitleSearchResult?          _selectedResult;

    // ── Selected button styling ───────────────────────────────────────────────
    private static readonly IBrush SelectedBg     = new SolidColorBrush(Color.Parse("#3A9B4B"));
    private static readonly IBrush NormalBg       = new SolidColorBrush(Color.Parse("#3D3846"));
    private static readonly IBrush NormalBorder   = new SolidColorBrush(Color.Parse("#5E5968"));

    public SubtitleSettingsDialog() : this(new SubtitleSettings(null,
        SubtitleFontSize.Medium, SubtitleFont.SansSerif, SubtitleColor.White, 0)) { }

    public SubtitleSettingsDialog(
        SubtitleSettings current,
        string? mediaFilePath = null,
        IEnumerable<TrackInfo>? embeddedSubtitleTracks = null)
    {
        AvaloniaXamlLoader.Load(this);

        _filePath = current.FilePath;
        _fontSize = current.FontSize;
        _font     = current.Font;
        _color    = current.Color;
        _delayMs  = current.DelayMs;
        _embeddedTrackId = current.EmbeddedTrackId;
        _embeddedSubtitleTracks = [.. (embeddedSubtitleTracks ?? [])
            .Where(t => t.Id >= 0)];

        // ── Init inline search ────────────────────────────────────────────────
        InitEmbeddedTracks();

        var langBox = this.FindControl<ComboBox>("LanguageBox")!;
        langBox.ItemsSource   = SubtitleSearchService.Languages.Select(l => l.Display).ToList();
        langBox.SelectedIndex = 0;

        var fontCombo = this.FindControl<ComboBox>("FontCombo")!;
        fontCombo.ItemsSource = FontChoices;

        var colorCombo = this.FindControl<ComboBox>("ColorCombo")!;
        colorCombo.ItemsSource = ColorChoices;

        var searchBox = this.FindControl<TextBox>("SearchBox")!;
        searchBox.KeyDown += (_, e) => { if (e.Key == Key.Return) StartSearch(); };

        if (!string.IsNullOrWhiteSpace(mediaFilePath))
            searchBox.Text = Path.GetFileNameWithoutExtension(mediaFilePath);

        var list = this.FindControl<ListBox>("ResultsList")!;
        list.SelectionChanged += (_, _) =>
        {
            _selectedResult = list.SelectedItem as SubtitleSearchResult;
            var btn = this.FindControl<Button>("UseButton");
            if (btn is not null) btn.IsEnabled = _selectedResult is not null;
        };

        RefreshAll();
    }

    private void InitEmbeddedTracks()
    {
        var panel = this.FindControl<StackPanel>("EmbeddedTracksPanel");
        var list = this.FindControl<ListBox>("EmbeddedTracksList");
        if (panel is null || list is null) return;

        panel.IsVisible = _embeddedSubtitleTracks.Length > 0;
        list.ItemsSource = _embeddedSubtitleTracks;

        var selected = _embeddedSubtitleTracks.FirstOrDefault(t => t.Id == _embeddedTrackId);
        if (selected is null && _filePath is null)
            selected = _embeddedSubtitleTracks.FirstOrDefault(t => t.IsSelected);
        if (selected is not null)
        {
            _embeddedTrackId = selected.Id;
            list.SelectedItem = selected;
        }

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not TrackInfo track) return;
            _embeddedTrackId = track.Id;
            _filePath = null;
            RefreshFilePath();
        };
    }

    // ── File tab: browse ─────────────────────────────────────────────────────

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
            _embeddedTrackId = null;
            ClearEmbeddedTrackSelection();
            RefreshFilePath();
        }
    }

    // ── File tab: search ─────────────────────────────────────────────────────

    private void SearchButton_Click(object? sender, RoutedEventArgs e) => StartSearch();

    private void ResultsList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_selectedResult is not null) DownloadSelected();
    }

    private void UseButton_Click(object? sender, RoutedEventArgs e) => DownloadSelected();

    private async void DownloadResultButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: SubtitleSearchResult result }) return;

        e.Handled = true;

        var target = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Download subtitle",
            SuggestedFileName = result.FileName
        });
        if (target is null) return;

        var targetPath = target.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(targetPath)) return;

        SetSearchStatus($"Downloading {result.FileName}…", loading: true);
        SetSearchInputEnabled(false);

        try
        {
            var tempPath = await _service.DownloadAsync(result);
            await using var source = File.OpenRead(tempPath);
            await using var destination = await target.OpenWriteAsync();
            destination.SetLength(0);
            await source.CopyToAsync(destination);

            SetSearchStatus($"Downloaded: {Path.GetFileName(targetPath)}", loading: false);
        }
        catch (Exception ex)
        {
            SetSearchStatus($"Download failed: {ex.Message}", loading: false);
        }
        finally
        {
            SetSearchInputEnabled(true);
        }
    }

    private async void StartSearch()
    {
        var query = this.FindControl<TextBox>("SearchBox")?.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(query)) return;

        var langIdx = this.FindControl<ComboBox>("LanguageBox")!.SelectedIndex;
        var (_, osCode, pnCode) = SubtitleSearchService.Languages[Math.Max(0, langIdx)];

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        SetSearchStatus("Searching…", loading: true);
        SetSearchInputEnabled(false);

        try
        {
            var results = await _service.SearchAsync(query, osCode, pnCode, _cts.Token);
            this.FindControl<ListBox>("ResultsList")!.ItemsSource = results;
            SetSearchStatus(results.Count == 0
                ? "No results found. Try a different title or language."
                : $"{results.Count} results found.",
                loading: false);
        }
        catch (OperationCanceledException) { SetSearchStatus("", loading: false); }
        catch (Exception ex)              { SetSearchStatus($"Search failed: {ex.Message}", loading: false); }
        finally                           { SetSearchInputEnabled(true); }
    }

    private async void DownloadSelected()
    {
        if (_selectedResult is null) return;

        SetSearchStatus($"Downloading {_selectedResult.FileName}…", loading: true);
        SetSearchInputEnabled(false);
        var useBtn = this.FindControl<Button>("UseButton");
        if (useBtn is not null) useBtn.IsEnabled = false;

        try
        {
            var path = await _service.DownloadAsync(_selectedResult);
            if (!string.IsNullOrWhiteSpace(path))
            {
                _filePath = path;
                _embeddedTrackId = null;
                ClearEmbeddedTrackSelection();
                RefreshFilePath();
                SetSearchStatus($"Ready: {Path.GetFileName(path)}", loading: false);
            }
        }
        catch (Exception ex)
        {
            SetSearchStatus($"Download failed: {ex.Message}", loading: false);
            if (useBtn is not null) useBtn.IsEnabled = _selectedResult is not null;
        }
        finally { SetSearchInputEnabled(true); }
    }

    // ── Appearance tab handlers ───────────────────────────────────────────────

    private void SizeButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && Enum.TryParse<SubtitleFontSize>(btn.Tag?.ToString(), out var size))
        {
            _fontSize = size;
            RefreshSizeButtons();
            RefreshPreview();
        }
    }

    private void FontCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: Choice<SubtitleFont> choice })
        {
            _font = choice.Value;
            RefreshPreview();
        }
    }

    private void ColorCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: Choice<SubtitleColor> choice })
        {
            _color = choice.Value;
            RefreshPreview();
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

    // ── Action buttons ────────────────────────────────────────────────────────

    private void ApplyButton_Click(object? sender, RoutedEventArgs e)
        => Close(new SubtitleSettings(_filePath, _fontSize, _font, _color, _delayMs, _embeddedTrackId));

    private void DisableButton_Click(object? sender, RoutedEventArgs e)
        => Close(new SubtitleSettings(null, _fontSize, _font, _color, 0));

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        Close(null);
    }

    // ── Refresh helpers ───────────────────────────────────────────────────────

    private void RefreshAll()
    {
        RefreshFilePath();
        RefreshSizeButtons();
        RefreshFontCombo();
        RefreshColorCombo();
        RefreshDelayLabel();
        RefreshPreview();
    }

    private void RefreshFilePath()
    {
        var box = this.FindControl<TextBox>("FilePathBox");
        if (box is not null)
            box.Text = _embeddedTrackId is not null || _filePath is null
                ? string.Empty
                : Path.GetFileName(_filePath);
    }

    private void ClearEmbeddedTrackSelection()
    {
        var list = this.FindControl<ListBox>("EmbeddedTracksList");
        if (list is not null)
            list.SelectedItem = null;
    }

    private void RefreshSizeButtons()
    {
        MarkSelected("SizeSmallBtn",  _fontSize == SubtitleFontSize.Small);
        MarkSelected("SizeMediumBtn", _fontSize == SubtitleFontSize.Medium);
        MarkSelected("SizeLargeBtn",  _fontSize == SubtitleFontSize.Large);
    }

    private void RefreshFontCombo()
    {
        var combo = this.FindControl<ComboBox>("FontCombo");
        if (combo is null) return;
        combo.SelectedIndex = Math.Max(0, Array.FindIndex(FontChoices, c => c.Value == _font));
    }

    private void RefreshColorCombo()
    {
        var combo = this.FindControl<ComboBox>("ColorCombo");
        if (combo is null) return;
        combo.SelectedIndex = Math.Max(0, Array.FindIndex(ColorChoices, c => c.Value == _color));
    }

    private void RefreshDelayLabel()
    {
        var label = this.FindControl<TextBlock>("DelayLabel");
        if (label is not null)
            label.Text = _delayMs == 0 ? "0 ms" : $"{_delayMs:+0;-0} ms";
    }

    private void RefreshPreview()
    {
        var preview = this.FindControl<TextBlock>("PreviewText");
        if (preview is null) return;

        preview.FontSize = _fontSize switch
        {
            SubtitleFontSize.Small  => 18,
            SubtitleFontSize.Large  => 28,
            _                       => 23
        };

        preview.FontFamily = _font switch
        {
            SubtitleFont.Serif     => new FontFamily("Liberation Serif,DejaVu Serif,Times New Roman,serif"),
            SubtitleFont.Monospace => new FontFamily("Courier New,Liberation Mono,DejaVu Sans Mono,monospace"),
            SubtitleFont.Arial     => new FontFamily("Arial,Liberation Sans,sans-serif"),
            _                      => FontFamily.Default
        };

        preview.Foreground = _color switch
        {
            SubtitleColor.Yellow => new SolidColorBrush(Color.Parse("#FFE600")),
            SubtitleColor.Grey   => new SolidColorBrush(Color.Parse("#BBBBBB")),
            SubtitleColor.Black  => new SolidColorBrush(Color.Parse("#111111")),
            _                    => new SolidColorBrush(Color.Parse("#F7F5F3"))
        };
    }

    private void MarkSelected(string name, bool selected)
    {
        var btn = this.FindControl<Button>(name);
        if (btn is null) return;
        btn.Background  = selected ? SelectedBg   : NormalBg;
        btn.BorderBrush = selected ? SelectedBg   : NormalBorder;
        btn.Foreground  = new SolidColorBrush(Colors.White);
    }

    private void SetSearchStatus(string text, bool loading)
    {
        var s = this.FindControl<TextBlock>("StatusText");
        if (s is not null) s.Text = text;
        var b = this.FindControl<ProgressBar>("LoadingBar");
        if (b is not null) b.IsVisible = loading;
    }

    private void SetSearchInputEnabled(bool enabled)
    {
        foreach (var name in new[] { "SearchButton", "BrowseButton" })
        {
            var btn = this.FindControl<Button>(name);
            if (btn is not null) btn.IsEnabled = enabled;
        }
        var box  = this.FindControl<TextBox>("SearchBox");
        if (box  is not null) box.IsEnabled = enabled;
        var lang = this.FindControl<ComboBox>("LanguageBox");
        if (lang is not null) lang.IsEnabled = enabled;
    }

    private sealed record Choice<T>(string Label, T Value)
    {
        public override string ToString() => Label;
    }
}
