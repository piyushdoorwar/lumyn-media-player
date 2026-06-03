namespace Lumyn.Core.Services;

/// <summary>Shared time-formatting helpers for track durations.</summary>
public static class MediaTime
{
    /// <summary>
    /// Formats a track duration as <c>m:ss</c> (or <c>h:mm:ss</c> past an hour).
    /// Returns an empty string when the duration is unknown or non-positive.
    /// </summary>
    public static string FormatDuration(TimeSpan? duration)
    {
        if (duration is not { } d || d <= TimeSpan.Zero) return string.Empty;
        return d.TotalHours >= 1
            ? $"{(int)d.TotalHours}:{d.Minutes:D2}:{d.Seconds:D2}"
            : $"{d.Minutes}:{d.Seconds:D2}";
    }
}
