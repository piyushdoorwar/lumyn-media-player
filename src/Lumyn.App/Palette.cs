using Avalonia;
using Avalonia.Media;

namespace Lumyn.App;

/// <summary>
/// Theme tokens from Assets/Styles/Theme.axaml for UI built in code-behind.
/// Prefer style classes (card, chip, option, nav, kbd…) over setting brushes directly.
/// </summary>
internal static class Palette
{
    public static IBrush Bg => Get("Lumyn.Bg");
    public static IBrush Surface => Get("Lumyn.Surface");
    public static IBrush Surface2 => Get("Lumyn.Surface2");
    public static IBrush Surface3 => Get("Lumyn.Surface3");
    public static IBrush Line => Get("Lumyn.Line");
    public static IBrush LineSoft => Get("Lumyn.LineSoft");
    public static IBrush Ink => Get("Lumyn.Ink");
    public static IBrush Text => Get("Lumyn.Text");
    public static IBrush Muted => Get("Lumyn.Muted");
    public static IBrush Faint => Get("Lumyn.Faint");
    public static IBrush Accent => Get("Lumyn.Accent");
    public static IBrush AccentBright => Get("Lumyn.AccentBright");
    public static IBrush AccentText => Get("Lumyn.AccentText");
    public static IBrush Danger => Get("Lumyn.Danger");

    public static FontFamily Mono =>
        Application.Current?.TryGetResource("Lumyn.Mono", null, out var v) == true && v is FontFamily f
            ? f
            : FontFamily.Default;

    /// <summary>Looks up a StreamGeometry from Assets/Icons/MediaIcons.axaml.</summary>
    public static Geometry? Icon(string key) =>
        Application.Current?.TryGetResource(key, null, out var v) == true ? v as Geometry : null;

    private static IBrush Get(string key) =>
        Application.Current?.TryGetResource(key, null, out var v) == true && v is IBrush b
            ? b
            : Brushes.Magenta;
}
