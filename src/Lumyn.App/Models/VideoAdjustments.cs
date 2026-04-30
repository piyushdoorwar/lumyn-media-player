namespace Lumyn.App.Models;

/// <summary>Aspect ratio presets surfaced in the Video Adjustments dialog.</summary>
public enum VideoAspect
{
    Auto,       // "no"     — honour the file's native aspect
    Ratio16x9,  // "16:9"
    Ratio4x3,   // "4:3"
    Ratio235x1, // "2.35:1" — CinemaScope
    Square,     // "1:1"
}

/// <summary>
/// Immutable snapshot of user-controlled video adjustment parameters.
/// <see cref="Zoom"/> maps directly to mpv's <c>video-zoom</c> (log₂ scale:
/// 0 = original size, 1 = 2×, −1 = 50%).
/// </summary>
public sealed record VideoAdjustments(
    int         Brightness,  // −100 .. 100   (mpv: brightness)
    int         Contrast,    // −100 .. 100   (mpv: contrast)
    int         Saturation,  // −100 .. 100   (mpv: saturation)
    int         Rotation,    // 0, 90, 180, 270 (mpv: video-rotate)
    double      Zoom,        // log₂(scale)  (mpv: video-zoom)
    VideoAspect Aspect)
{
    public static readonly VideoAdjustments Default =
        new(0, 0, 0, 0, 0.0, VideoAspect.Auto);

    public bool IsDefault =>
        Brightness == 0 && Contrast == 0 && Saturation == 0 &&
        Rotation == 0 && Math.Abs(Zoom) < 0.001 && Aspect == VideoAspect.Auto;

    /// <summary>Converts an aspect enum value to the string mpv expects.</summary>
    public static string AspectToMpv(VideoAspect a) => a switch
    {
        VideoAspect.Ratio16x9  => "16:9",
        VideoAspect.Ratio4x3   => "4:3",
        VideoAspect.Ratio235x1 => "2.35:1",
        VideoAspect.Square     => "1:1",
        _                      => "no",
    };
}
