using Avalonia.Controls;
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

    private Grid BuildRow(BookmarkEntry entry, int index)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
            Margin = new Avalonia.Thickness(0, 1)
        };

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

        var labelText = new TextBlock
        {
            Text = entry.Label,
            FontSize = 12,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#C8C3C2")),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(labelText, 1);

        var jumpBtn = new Button
        {
            Content = "▶ Jump",
            Background = Avalonia.Media.Brushes.Transparent,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#6A6560")),
            BorderThickness = new Avalonia.Thickness(0),
            Padding = new Avalonia.Thickness(8, 4),
            FontSize = 11,
            Tag = entry
        };
        jumpBtn.Click += JumpBtn_Click;
        Grid.SetColumn(jumpBtn, 2);

        var deleteBtn = new Button
        {
            Content = "✕",
            Background = Avalonia.Media.Brushes.Transparent,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#6A3535")),
            BorderThickness = new Avalonia.Thickness(0),
            Padding = new Avalonia.Thickness(6, 4),
            FontSize = 11,
            Tag = index
        };
        deleteBtn.Click += DeleteBtn_Click;
        Grid.SetColumn(deleteBtn, 3);

        grid.Children.Add(timeBorder);
        grid.Children.Add(labelText);
        grid.Children.Add(jumpBtn);
        grid.Children.Add(deleteBtn);

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
        Refresh();
    }

    // These are referenced by AXAML but implemented via code-behind BuildRow above.
    private void JumpButton_Click(object? sender, RoutedEventArgs e) { }
    private void DeleteButton_Click(object? sender, RoutedEventArgs e) { }
}
