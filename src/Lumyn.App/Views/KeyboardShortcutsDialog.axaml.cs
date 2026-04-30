using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace Lumyn.App.Views;

public partial class KeyboardShortcutsDialog : Window
{
    // Each section is (key label, action description)
    private static readonly (string Key, string Action)[] Playback =
    [
        ("Space",        "Play / Pause"),
        ("S",            "Open subtitles dialog"),
        (".",            "Step one frame forward"),
        ("L",            "Toggle loop"),
    ];

    private static readonly (string Key, string Action)[] Seeking =
    [
        ("←",            "Seek back 5 s"),
        ("→",            "Seek forward 5 s"),
        ("Ctrl + ←",     "Seek back 30 s"),
        ("Ctrl + →",     "Seek forward 30 s"),
    ];

    private static readonly (string Key, string Action)[] Volume =
    [
        ("↑",            "Volume up 5 %"),
        ("↓",            "Volume down 5 %"),
        ("M",            "Toggle mute"),
    ];

    private static readonly (string Key, string Action)[] Speed =
    [
        ("[",            "Speed down"),
        ("]",            "Speed up"),
        ("\\",           "Reset speed to 1×"),
    ];

    private static readonly (string Key, string Action)[] Tracks =
    [
        ("A",            "Cycle audio track"),
        ("V",            "Cycle subtitle track"),
    ];

    private static readonly (string Key, string Action)[] Window =
    [
        ("F",            "Toggle fullscreen"),
        ("Escape",       "Exit fullscreen"),
        ("T",            "Toggle always on top"),
    ];

    private static readonly (string Key, string Action)[] FileAndDialogs =
    [
        ("O",            "Open file"),
        ("Ctrl + G",     "Jump to time"),
        ("Alt + I",      "Take screenshot"),
    ];

    public KeyboardShortcutsDialog()
    {
        AvaloniaXamlLoader.Load(this);
        PopulateGroup("PlaybackGroup", Playback);
        PopulateGroup("SeekingGroup",  Seeking);
        PopulateGroup("VolumeGroup",   Volume);
        PopulateGroup("SpeedGroup",    Speed);
        PopulateGroup("TracksGroup",   Tracks);
        PopulateGroup("WindowGroup",   Window);
        PopulateGroup("FileGroup",     FileAndDialogs);
    }

    private void PopulateGroup(string gridName, (string Key, string Action)[] rows)
    {
        var grid = this.FindControl<Grid>(gridName);
        if (grid is null) return;

        for (var i = 0; i < rows.Length; i++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        for (var i = 0; i < rows.Length; i++)
        {
            var (key, action) = rows[i];
            var isEven = i % 2 == 0;
            var rowBg = isEven
                ? new SolidColorBrush(Color.Parse("#161616"))
                : new SolidColorBrush(Color.Parse("#1A1A1A"));

            // Background row span
            var bg = new Border
            {
                Background = rowBg,
                CornerRadius = new Avalonia.CornerRadius(4),
                Margin = new Avalonia.Thickness(0, 1),
            };
            Grid.SetRow(bg, i);
            Grid.SetColumnSpan(bg, 2);
            grid.Children.Add(bg);

            // Key badge
            var keyBorder = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#2D2D2D")),
                BorderBrush = new SolidColorBrush(Color.Parse("#4D4658")),
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new Avalonia.CornerRadius(4),
                Padding = new Avalonia.Thickness(8, 4),
                Margin = new Avalonia.Thickness(8, 5, 8, 5),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            };
            var keyText = new TextBlock
            {
                Text = key,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse("#E8E4E0")),
                FontFamily = new FontFamily("Courier New,Liberation Mono,monospace"),
            };
            keyBorder.Child = keyText;
            Grid.SetRow(keyBorder, i);
            Grid.SetColumn(keyBorder, 0);
            grid.Children.Add(keyBorder);

            // Action description
            var actionText = new TextBlock
            {
                Text = action,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.Parse("#DEDAD5")),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Avalonia.Thickness(0, 0, 8, 0),
            };
            Grid.SetRow(actionText, i);
            Grid.SetColumn(actionText, 1);
            grid.Children.Add(actionText);
        }
    }

    private void CloseBtn_Click(object? sender, RoutedEventArgs e) => Close();
}
