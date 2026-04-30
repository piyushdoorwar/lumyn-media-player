namespace Lumyn.App.Models;

/// <summary>Font size tier, matching Amazon Prime Video-style presets.</summary>
public enum SubtitleFontSize { Small, Medium, Large }

/// <summary>Subtitle text colour options presented to the user.</summary>
public enum SubtitleColor
{
    White,
    WhiteWithBorder,
    Yellow,
    Grey,
    Black
}

/// <summary>Font family options suitable for subtitle rendering.</summary>
public enum SubtitleFont
{
    SansSerif,   // system default (Cantarell / Ubuntu)
    Serif,       // Liberation Serif / DejaVu Serif
    Monospace,   // Courier / DejaVu Sans Mono
    Arial        // Arial / Liberation Sans (wide compat)
}

/// <summary>Result returned from <c>SubtitleSettingsDialog</c>.</summary>
public sealed record SubtitleSettings(
    string?          FilePath,
    SubtitleFontSize FontSize,
    SubtitleFont     Font,
    SubtitleColor    Color,
    long             DelayMs   // signed ms; positive = later, negative = earlier
);
