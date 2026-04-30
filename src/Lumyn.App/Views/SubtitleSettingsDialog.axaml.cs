using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Lumyn.App.Models;
using Lumyn.Core.Models;
using Lumyn.Core.Services;

namespace Lumyn.App.Views;

public partial class SubtitleSettingsDialog : Window
{
    private const long DelayStep = 500;

    // ── Settings state ───────────────────────────────────────────────────────
    private string?          _filePath;
    private SubtitleFontSize _fontSize = SubtitleFontSize.Medium;
    private SubtitleFont     _font     = SubtitleFont.SansSerif;
    private SubtitleColor    _color    = SubtitleColor.White;
    private long             _delayMs  = 0;

    // ── Search state ─────────────────────────────────────────────────────────
    private readonly SubtitleSearchService _service = new();
    private CancellationTokenSource?       _cts;
    private SubtitleSearchResult?          _selectedResult;

    // ── Selected button styling ───────────────────────────────────────────────
    private static readonly IBrush SelectedBg     = new SolidColorBrush(Color.Parse("#E95420"));
    private static readonly IBrush NormalBg       = new SolidColorBrush(Color.Parse("#3D3846"));
    private static readonly IBrush NormalBorder   = new SolidColorBrush(Color.Parse("#5E5968"));

    public SubtitleSettingsDialog() : this(new SubtitleSettings(null,
        SubtitleFontSize.Medium, SubtitleFont.SansSerif, SubtitleColor.White, 0)) { }

    public SubtitleSettingsDialog(SubtitleSettings current, string? mediaFilePath = null)
    {
        AvaloniaXamlLoader.Load(this);

        _filePath = current.FilePath;
        _fontSize = current.FontSize;
        _font     = current.Font;
        _color    = current.Color;
        _delayMs  = current.DelayMs;

        // ── Init inline search ────────────────────────────────────────────────
        var langBox = this.FindControl<ComboBox>("LanguageBox")!;
        langBox.ItemsSource   = SubtitleSearchService.Languages.Select(l => l.Display).ToList();
        langBox.SelectedIndex = 0;

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
        }
    }

    private void FontCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo &&
            combo.SelectedItem is ComboBoxItem item &&
            Enum.TryParse<SubtitleFont>(item.Tag?.ToString(), out var font))
            _font = font;
    }

    private void ColorCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo &&
            combo.SelectedItem is ComboBoxItem item &&
            Enum.TryParse<SubtitleColor>(item.Tag?.ToString(), out var color))
            _color = color;
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
        => Close(new SubtitleSettings(_filePath, _fontSize, _font, _color, _delayMs));

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
    }

    private void RefreshFilePath()
    {
        var box = this.FindControl<TextBox>("FilePathBox");
        if (box is not null)
            box.Text = _filePath is null ? string.Empty : Path.GetFileName(_filePath);
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
        combo.SelectedIndex = _font switch
        {
            SubtitleFont.SansSerif => 0,
            SubtitleFont.Serif     => 1,
            SubtitleFont.Monospace => 2,
            SubtitleFont.Arial     => 3,
            _                      => 0
        };
    }

    private void RefreshColorCombo()
    {
        var combo = this.FindControl<ComboBox>("ColorCombo");
        if (combo is null) return;
        combo.SelectedIndex = _color switch
        {
            SubtitleColor.White           => 0,
            SubtitleColor.WhiteWithBorder => 1,
            SubtitleColor.Yellow          => 2,
            SubtitleColor.Grey            => 3,
            SubtitleColor.Black           => 4,
            _                             => 0
        };
    }

    private void RefreshDelayLabel()
    {
        var label = this.FindControl<TextBlock>("DelayLabel");
        if (label is not null)
            label.Text = _delayMs == 0 ? "0 ms" : $"{_delayMs:+0;-0} ms";
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
}
