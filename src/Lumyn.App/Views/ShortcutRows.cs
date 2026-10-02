using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Lumyn.App.Views;

/// <summary>
/// The keyboard shortcut reference shown in Settings → Shortcuts and the
/// standalone shortcuts dialog: grouped sections, each a card of key/action rows.
/// </summary>
internal static class ShortcutRows
{
    private static readonly (string Title, (string Key, string Action)[] Rows)[] Sections =
    [
        ("Playback",
        [
            ("Space", "Play / Pause"),
            ("S", "Open subtitles dialog"),
            (".", "Step one frame forward"),
            (",", "Step one frame back"),
            ("Page Up", "Previous chapter"),
            ("Page Down", "Next chapter"),
            ("L", "Toggle loop"),
            ("R", "A-B repeat: set A → set B → clear"),
        ]),
        ("Seeking",
        [
            ("Left", "Seek back (step)"),
            ("Right", "Seek forward (step)"),
            ("Ctrl + Left", "Seek back 30 s"),
            ("Ctrl + Right", "Seek forward 30 s"),
            ("click badge", "Cycle seek step: 5s / 10s / 30s"),
        ]),
        ("Volume",
        [
            ("Up", "Volume up 5 %"),
            ("Down", "Volume down 5 %"),
            ("M", "Toggle mute"),
        ]),
        ("Speed",
        [
            ("[", "Speed down"),
            ("]", "Speed up"),
            ("\\", "Reset speed to 1x"),
        ]),
        ("Tracks",
        [
            ("A", "Cycle audio track"),
            ("V", "Cycle subtitle track"),
            ("N", "Next track in folder"),
            ("P", "Previous track in folder"),
        ]),
        ("Window",
        [
            ("F", "Toggle fullscreen"),
            ("Escape", "Exit fullscreen"),
            ("T", "Toggle always on top"),
        ]),
        ("File & dialogs",
        [
            ("O", "Open file"),
            ("B", "Open markers"),
            ("Ctrl + G", "Jump to time"),
            ("Alt + I", "Take screenshot"),
        ]),
    ];

    /// <summary>Fills <paramref name="host"/> with a heading and a card per section.</summary>
    public static void Populate(StackPanel host)
    {
        for (var s = 0; s < Sections.Length; s++)
        {
            var (title, rows) = Sections[s];

            var heading = new TextBlock
            {
                Text = title,
                Margin = new Thickness(0, s == 0 ? 0 : 16, 0, 8)
            };
            heading.Classes.Add("h2");
            host.Children.Add(heading);

            var list = new StackPanel();
            for (var i = 0; i < rows.Length; i++)
                list.Children.Add(BuildRow(rows[i].Key, rows[i].Action, isLast: i == rows.Length - 1));

            var card = new Border { Padding = new Thickness(0), Child = list };
            card.Classes.Add("card");
            host.Children.Add(card);
        }
    }

    private static Control BuildRow(string key, string action, bool isLast)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("170,*"),
            MinHeight = 38
        };

        var keyCap = new Border
        {
            Margin = new Thickness(12, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = key }
        };
        keyCap.Classes.Add("kbd");
        grid.Children.Add(keyCap);

        var actionText = new TextBlock
        {
            Text = action,
            Foreground = Palette.Text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        Grid.SetColumn(actionText, 1);
        grid.Children.Add(actionText);

        return new Border
        {
            BorderBrush = Palette.LineSoft,
            BorderThickness = new Thickness(0, 0, 0, isLast ? 0 : 1),
            Child = grid
        };
    }
}
