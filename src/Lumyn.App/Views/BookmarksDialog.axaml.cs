using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Lumyn.App.ViewModels;
using Lumyn.Core.Services;

namespace Lumyn.App.Views;

public partial class BookmarksDialog : Window
{
    private readonly MainViewModel _vm;
    private List<BookmarkEntry> _entries = [];
    private IReadOnlyList<ChapterInfo> _chapters = [];

    public BookmarksDialog(MainViewModel vm)
    {
        AvaloniaXamlLoader.Load(this);
        _vm = vm;

        var filePath = vm.CurrentFilePath ?? "";
        if (this.FindControl<TextBlock>("FileNameText") is { } tb)
            tb.Text = string.IsNullOrWhiteSpace(filePath)
                ? "No file open"
                : Path.GetFileName(filePath);

        Refresh();
    }

    private void Refresh()
    {
        _entries = [.. _vm.GetBookmarksForCurrentFile()];
        _chapters = _vm.Chapters;

        var empty = this.FindControl<TextBlock>("EmptyBookmarksText");
        var list = this.FindControl<ItemsControl>("BookmarksList");

        if (empty is not null) empty.IsVisible = _entries.Count == 0;
        if (list is not null)
        {
            list.Items.Clear();
            foreach (var (entry, idx) in _entries.Select((e, i) => (e, i)))
                list.Items.Add(BuildBookmarkRow(entry, idx));
        }

        RefreshChapters();
    }

    private void RefreshChapters()
    {
        var hasChapters = _chapters.Count > 0;

        if (this.FindControl<Border>("ChaptersDivider") is { } divider)
            divider.IsVisible = hasChapters;
        if (this.FindControl<TextBlock>("ChaptersHeader") is { } header)
            header.IsVisible = hasChapters;

        var list = this.FindControl<ItemsControl>("ChaptersList");
        if (list is null) return;

        list.IsVisible = hasChapters;
        list.Items.Clear();
        foreach (var chapter in _chapters)
            list.Items.Add(BuildChapterRow(chapter, JumpToChapter));
    }

    private Border BuildBookmarkRow(BookmarkEntry entry, int index, bool startEditing = false)
    {
        // Columns: [timestamp pill] [label / edit box] [pencil] [jump] [delete]
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"),
            ColumnSpacing = 6,
            MinHeight = 30
        };

        var timeBorder = BuildTimePill(entry.FormattedTime, Palette.AccentText);
        Grid.SetColumn(timeBorder, 0);

        // ── Label panel (TextBlock + TextBox toggled) ───────────────────────
        var labelPanel = new Panel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var labelText = new TextBlock
        {
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsVisible = !startEditing
        };
        ApplyLabel(labelText, entry.Label);

        var labelBox = new TextBox
        {
            Text = entry.Label,
            FontSize = 12,
            MinHeight = 28,
            Padding = new Thickness(8, 4),
            PlaceholderText = "Add a name…",
            IsVisible = startEditing
        };

        void CommitEdit()
        {
            var newLabel = labelBox.Text?.Trim() ?? "";
            _vm.RenameBookmark(index, newLabel);
            ApplyLabel(labelText, newLabel);
            labelBox.IsVisible = false;
            labelText.IsVisible = true;
        }

        labelBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { CommitEdit(); e.Handled = true; }
            if (e.Key == Key.Escape) { labelBox.IsVisible = false; labelText.IsVisible = true; e.Handled = true; }
        };
        labelBox.LostFocus += (_, _) => { if (labelBox.IsVisible) CommitEdit(); };

        labelPanel.Children.Add(labelText);
        labelPanel.Children.Add(labelBox);
        Grid.SetColumn(labelPanel, 1);

        // ── Pencil edit button ──────────────────────────────────────────────
        var editBtn = BuildIconButton("Icon.Edit", 11, Palette.Muted);
        ToolTip.SetTip(editBtn, "Rename");
        editBtn.Click += (_, _) =>
        {
            labelText.IsVisible = false;
            labelBox.IsVisible = true;
            labelBox.SelectAll();
            labelBox.Focus();
        };
        Grid.SetColumn(editBtn, 2);

        var jumpBtn = BuildJumpButton(entry);
        jumpBtn.Click += JumpBtn_Click;
        Grid.SetColumn(jumpBtn, 3);

        // ── Delete button ───────────────────────────────────────────────────
        var deleteBtn = BuildIconButton("Icon.WindowClose", 10, Palette.Danger);
        deleteBtn.Tag = index;
        ToolTip.SetTip(deleteBtn, "Delete");
        deleteBtn.Click += DeleteBtn_Click;
        Grid.SetColumn(deleteBtn, 4);

        grid.Children.Add(timeBorder);
        grid.Children.Add(labelPanel);
        grid.Children.Add(editBtn);
        grid.Children.Add(jumpBtn);
        grid.Children.Add(deleteBtn);

        if (startEditing)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { labelBox.SelectAll(); labelBox.Focus(); },
                Avalonia.Threading.DispatcherPriority.Input);

        return WrapRow(grid);
    }

    private static Border BuildChapterRow(ChapterInfo chapter, Action<ChapterInfo> onJump)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 8,
            MinHeight = 30
        };

        var timeBorder = BuildTimePill(chapter.FormattedTime, Palette.Muted);
        Grid.SetColumn(timeBorder, 0);

        var title = new TextBlock
        {
            Text = chapter.Title,
            FontSize = 12,
            Foreground = Palette.Text,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(title, 1);

        var jumpBtn = BuildJumpButton(chapter);
        jumpBtn.Click += (_, _) => onJump(chapter);
        Grid.SetColumn(jumpBtn, 2);

        grid.Children.Add(timeBorder);
        grid.Children.Add(title);
        grid.Children.Add(jumpBtn);

        return WrapRow(grid);
    }

    // ── Row building blocks ─────────────────────────────────────────────────

    private static Border WrapRow(Control content)
    {
        var row = new Border
        {
            Padding = new Thickness(8, 6),
            Margin = new Thickness(0, 0, 0, 6),
            Child = content
        };
        row.Classes.Add("card");
        return row;
    }

    private static Border BuildTimePill(string text, IBrush foreground) => new()
    {
        Background = Palette.Surface3,
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(8, 3),
        Margin = new Thickness(0, 0, 6, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = text,
            FontFamily = Palette.Mono,
            FontSize = 11.5,
            FontWeight = FontWeight.SemiBold,
            Foreground = foreground
        }
    };

    private static void ApplyLabel(TextBlock labelText, string? label)
    {
        var hasLabel = !string.IsNullOrWhiteSpace(label);
        labelText.Text = hasLabel ? label : "Add label…";
        labelText.FontStyle = hasLabel ? FontStyle.Normal : FontStyle.Italic;
        labelText.Foreground = hasLabel ? Palette.Text : Palette.Faint;
    }

    private static Button BuildIconButton(string iconKey, double size, IBrush foreground)
    {
        var btn = new Button
        {
            Padding = new Thickness(6, 4),
            MinHeight = 26,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            Content = new PathIcon { Data = Palette.Icon(iconKey), Width = size, Height = size, Foreground = foreground }
        };
        btn.Classes.Add("ghost");
        return btn;
    }

    private static Button BuildJumpButton(object tag)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(new PathIcon { Data = Palette.Icon("Icon.Play"), Width = 10, Height = 10, Foreground = Palette.AccentText });
        content.Children.Add(new TextBlock { Text = "Jump", FontSize = 11.5, Foreground = Palette.AccentText, VerticalAlignment = VerticalAlignment.Center });

        var btn = new Button
        {
            Content = content,
            Padding = new Thickness(10, 4),
            MinHeight = 26,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = tag
        };
        btn.Classes.Add("soft");
        return btn;
    }

    private void JumpBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: BookmarkEntry entry })
        {
            _vm.JumpToBookmark(entry.Position);
            Close();
        }
    }

    private void DeleteBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int idx })
        {
            _vm.RemoveBookmark(idx);
            Refresh();
        }
    }

    private void AddButton_Click(object? sender, RoutedEventArgs e)
    {
        var before = _entries;
        _vm.AddBookmarkAtCurrentPosition("");
        _entries = [.. _vm.GetBookmarksForCurrentFile()];

        // Bookmarks are kept sorted by position, so the new entry is not
        // necessarily last: it is the one that was not in the previous snapshot.
        var newIndex = _entries.Count > before.Count
            ? _entries.FindIndex(entry => !before.Contains(entry))
            : -1;

        var empty = this.FindControl<TextBlock>("EmptyBookmarksText");
        var list  = this.FindControl<ItemsControl>("BookmarksList");
        if (empty is not null) empty.IsVisible = _entries.Count == 0;
        if (list is null) return;
        list.Items.Clear();
        for (int i = 0; i < _entries.Count; i++)
            list.Items.Add(BuildBookmarkRow(_entries[i], i, startEditing: i == newIndex));
    }

    private void JumpToChapter(ChapterInfo chapter)
    {
        _vm.JumpToChapter(chapter);
        Close();
    }
}
