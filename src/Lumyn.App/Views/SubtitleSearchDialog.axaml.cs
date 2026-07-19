using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Lumyn.Core.Models;
using Lumyn.Core.Services;

namespace Lumyn.App.Views;

public partial class SubtitleSearchDialog : Window
{
    private readonly SubtitleSearchService _service = new();
    private CancellationTokenSource?       _cts;
    private SubtitleSearchResult?          _selected;

    public SubtitleSearchDialog(string initialQuery = "")
    {
        AvaloniaXamlLoader.Load(this);

        // Populate language ComboBox
        var langBox = this.FindControl<ComboBox>("LanguageBox")!;
        langBox.ItemsSource  = SubtitleSearchService.Languages.Select(l => l.Display).ToList();
        langBox.SelectedIndex = 0; // English

        // Search on Enter in the query box
        var searchBox = this.FindControl<TextBox>("SearchBox")!;
        searchBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Return) StartSearch();
        };
        searchBox.AttachedToVisualTree += (_, _) => searchBox.Focus();

        // Pre-populate the search box with the media filename (without extension)
        if (!string.IsNullOrWhiteSpace(initialQuery))
            searchBox.Text = initialQuery;

        // Keep track of the selected result
        var list = this.FindControl<ListBox>("ResultsList")!;
        list.SelectionChanged += (_, _) =>
        {
            _selected = list.SelectedItem as SubtitleSearchResult;
            var btn = this.FindControl<Button>("UseButton");
            if (btn is not null) btn.IsEnabled = _selected is not null;
        };
    }

    // ── Event handlers ───────────────────────────────────────────────────────

    private void SearchButton_Click(object? sender, RoutedEventArgs e) => StartSearch();

    private void ResultsList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_selected is not null)
            DownloadAndClose();
    }

    private void UseButton_Click(object? sender, RoutedEventArgs e) => DownloadAndClose();

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        Close(null);
    }

    // ── Search logic ─────────────────────────────────────────────────────────

    private async void StartSearch()
    {
        var query = this.FindControl<TextBox>("SearchBox")?.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(query)) return;

        var langIdx = this.FindControl<ComboBox>("LanguageBox")!.SelectedIndex;
        var (_, osCode, pnCode) = SubtitleSearchService.Languages[Math.Max(0, langIdx)];

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        SetStatus("Searching…", loading: true);
        SetResultsEnabled(false);

        try
        {
            var results = await _service.SearchAsync(query, osCode, pnCode, _cts.Token);
            var list = this.FindControl<ListBox>("ResultsList")!;
            list.ItemsSource = results;

            SetStatus(results.Count == 0
                ? "No results found. Try a different title or language."
                : $"{results.Count} results found.",
                loading: false);
        }
        catch (OperationCanceledException) { /* search was cancelled — no message */ }
        catch (Exception ex)
        {
            SetStatus($"Search failed: {ex.Message}", loading: false);
        }
        finally
        {
            SetResultsEnabled(true);
        }
    }

    // ── Download logic ────────────────────────────────────────────────────────

    private async void DownloadAndClose()
    {
        if (_selected is null) return;

        SetStatus($"Downloading {_selected.FileName}…", loading: true);
        SetControlsEnabled(false);

        try
        {
            var path = await _service.DownloadAsync(_selected);
            Close(path); // returns the local .srt/.ass path to SubtitleSettingsDialog
        }
        catch (Exception ex)
        {
            SetStatus($"Download failed: {ex.Message}", loading: false);
            SetControlsEnabled(true);
        }
    }

    // ── UI helpers ────────────────────────────────────────────────────────────

    private void SetStatus(string text, bool loading)
    {
        var statusText = this.FindControl<TextBlock>("StatusText");
        var loadingBar = this.FindControl<ProgressBar>("LoadingBar");
        if (statusText is not null) { statusText.Text = text; statusText.IsVisible = true; }
        if (loadingBar is not null) loadingBar.IsVisible = loading;
    }

    private void SetResultsEnabled(bool enabled)
    {
        var searchBtn = this.FindControl<Button>("SearchButton");
        if (searchBtn is not null) searchBtn.IsEnabled = enabled;
    }

    private void SetControlsEnabled(bool enabled)
    {
        SetResultsEnabled(enabled);
        var useBtn    = this.FindControl<Button>("UseButton");
        var cancelBtn = this.FindControl<Button>("CancelButton");
        if (useBtn    is not null) useBtn.IsEnabled    = enabled && _selected is not null;
        if (cancelBtn is not null) cancelBtn.IsEnabled  = enabled;
    }
}
