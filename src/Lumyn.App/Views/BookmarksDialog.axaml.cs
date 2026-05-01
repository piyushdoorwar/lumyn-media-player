using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Lumyn.App.ViewModels;
using Lumyn.Core.Services;

namespace Lumyn.App.Views;

public partial class BookmarksDialog : Window
{
    private readonly MainViewModel _vm;
    private List<BookmarkEntry> _entries = [];

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

        var empty = this.FindControl<TextBlock>("EmptyText");
        var list  = this.FindControl<ItemsControl>("BookmarksList");

        if (empty is not null) empty.IsVisible = _entries.Count == 0;
        if (list is null) return;

        list.Items.Clear();
        foreach (var (entry, idx) in _entries.Select((e, i) => (e, i)))
        {
            var row = BuildRow(entry, idx);
            list.Items.Add(row);
        }
    }

    private Grid BuildRow(BookmarkEntry entry, int index, bool startEditing = false)
    {
        // Columns: [timestamp pill] [label / edit box] [pencil] [jump] [delete]
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"),
            Margin = new Avalonia.Thickness(0, 2)
        };

        // ── Timestamp pill ──────────────────────────────────────────────────
        var timeBorder = new Border
        {
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2A2A2A")),
            CornerRadius = new Avalonia.CornerRadius(4),
            Padding = new Avalonia.Thickness(8, 4),
            Margin = new Avalonia.Thickness(0, 0, 10, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = entry.FormattedTime,
                FontSize = 12,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3A9B4B"))
            }
        };
        Grid.SetColumn(timeBorder, 0);

        // ── Label panel (TextBlock + TextBox toggled) ───────────────────────
        var labelPanel = new Panel { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };

        var labelText = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(entry.Label) ? entry.FormattedTime : entry.Label,
            FontSize = 12,
            Foreground = new Avalonia.Media.SolidColorBrush(
                string.IsNullOrWhiteSpace(entry.Label)
                    ? Avalonia.Media.Color.Parse("#4A4948")
                    : Avalonia.Media.Color.Parse("#C8C3C2")),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
            IsVisible = !startEditing
        };

        var labelBox = new TextBox
        {
            Text = entry.Label,
            FontSize = 12,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2A2A2A")),
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#E8E4E0")),
            BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3A5A40")),
            BorderThickness = new Avalonia.Thickness(1),
            Padding = new Avalonia.Thickness(6, 3),
            CornerRadius = new Avalonia.CornerRadius(4),
            Watermark = "Add a name…",
            IsVisible = startEditing,
            Tag = (index, labelText)
        };

        void CommitEdit()
        {
            var newLabel = labelBox.Text?.Trim() ?? "";
            _vm.RenameBookmark(index, newLabel);
            labelText.Text = string.IsNullOrWhiteSpace(newLabel) ? entry.FormattedTime : newLabel;
            labelText.Foreground = new Avalonia.Media.SolidColorBrush(
                string.IsNullOrWhiteSpace(newLabel)
                    ? Avalonia.Media.Color.Parse("#4A4948")
                    : Avalonia.Media.Color.Parse("#C8C3C2"));
            labelBox.IsVisible = false;
            labelText.IsVisible = true;
        }

        labelBox.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter) { CommitEdit(); e.Handled = true; }
            if (e.Key == Avalonia.Input.Key.Escape) { labelBox.IsVisible = false; labelText.IsVisible = true; e.Handled = true; }
        };
        labelBox.LostFocus += (_, _) => { if (labelBox.IsVisible) CommitEdit(); };

        labelPanel.Children.Add(labelText);
        labelPanel.Children.Add(labelBox);
        Grid.SetColumn(labelPanel, 1);

        // ── Pencil edit button ──────────────────────────────────────────────
        var editBtn = new Button
        {
            Background = Avalonia.Media.Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(0),
            Padding = new Avalonia.Thickness(6, 4),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Opacity = 0.5
        };
        if (Application.Current?.Resources.TryGetResource("Icon.Edit", Avalonia.Styling.ThemeVariant.Default, out var editIcon) == true
            && editIcon is Avalonia.Media.StreamGeometry geom)
            editBtn.Content = new PathIcon { Data = geom, Width = 11, Height = 11, Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#6A6560")) };
        else
            editBtn.Content = new TextBlock { Text = "✎", FontSize = 12, Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#6A6560")) };

        editBtn.Click += (_, _) =>
        {
            labelText.IsVisible = false;
            labelBox.IsVisible = true;
            labelBox.SelectAll();
            labelBox.Focus();
        };
        Grid.SetColumn(editBtn, 2);

        // ── Jump button ─────────────────────────────────────────────────────
        var jumpBtn = new Button
        {
            Content = "▶ Jump",
            Background = Avalonia.Media.Brushes.Transparent,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#6A6560")),
            BorderThickness = new Avalonia.Thickness(0),
            Padding = new Avalonia.Thickness(8, 4),
            FontSize = 11,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Tag = entry
        };
        jumpBtn.Click += JumpBtn_Click;
        Grid.SetColumn(jumpBtn, 3);

        // ── Delete button ───────────────────────────────────────────────────
        var deleteBtn = new Button
        {
            Background = Avalonia.Media.Brushes.Transparent,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#7A3535")),
            BorderThickness = new Avalonia.Thickness(0),
            Padding = new Avalonia.Thickness(6, 4),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Tag = index
        };
        if (Application.Current?.Resources.TryGetResource("Icon.WindowClose", Avalonia.Styling.ThemeVariant.Default, out var closeIcon) == true
            && closeIcon is Avalonia.Media.StreamGeometry closeGeom)
            deleteBtn.Content = new PathIcon { Data = closeGeom, Width = 10, Height = 10, Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#7A3535")) };
        else
            deleteBtn.Content = new TextBlock { Text = "✕", FontSize = 11 };
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

        return grid;
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
        _vm.AddBookmarkAtCurrentPosition("");
        // Refresh and immediately put the new row into edit mode
        _entries = [.. _vm.GetBookmarksForCurrentFile()];
        var empty = this.FindControl<TextBlock>("EmptyText");
        var list  = this.FindControl<ItemsControl>("BookmarksList");
        if (empty is not null) empty.IsVisible = _entries.Count == 0;
        if (list is null) return;
        list.Items.Clear();
        for (int i = 0; i < _entries.Count; i++)
        {
            var isNew = i == _entries.Count - 1;
            list.Items.Add(BuildRow(_entries[i], i, startEditing: isNew));
        }
    }

    // These are referenced by AXAML but implemented via code-behind BuildRow above.
    private void JumpButton_Click(object? sender, RoutedEventArgs e) { }
    private void DeleteButton_Click(object? sender, RoutedEventArgs e) { }
}
