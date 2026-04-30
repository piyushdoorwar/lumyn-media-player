using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Lumyn.App.Views;

public partial class JumpToTimeDialog : Window
{
    public JumpToTimeDialog()
    {
        AvaloniaXamlLoader.Load(this);
        var input = this.FindControl<TextBox>("TimeInput");
        if (input is not null)
        {
            input.AttachedToVisualTree += (_, _) => input.Focus();
            input.KeyDown += (_, e) =>
            {
                if (e.Key == Avalonia.Input.Key.Return) TryClose();
                if (e.Key == Avalonia.Input.Key.Escape) Close(null);
            };
        }
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e) => TryClose();

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(null);

    private void TryClose()
    {
        var text = this.FindControl<TextBox>("TimeInput")?.Text ?? "";
        Close(TryParseTime(text, out var t) ? t : (TimeSpan?)null);
    }

    private static bool TryParseTime(string input, out TimeSpan result)
    {
        input = input.Trim();

        if (TimeSpan.TryParseExact(input,
            [@"h\:mm\:ss", @"hh\:mm\:ss", @"m\:ss", @"mm\:ss"],
            null, out result))
            return true;

        if (double.TryParse(input,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out var seconds))
        {
            result = TimeSpan.FromSeconds(seconds);
            return true;
        }

        result = TimeSpan.Zero;
        return false;
    }
}
