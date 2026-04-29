namespace Lumyn.Core.Models;

public sealed class MediaState
{
    public string? FilePath { get; set; }
    public TimeSpan Position { get; set; }
    public TimeSpan Duration { get; set; }
    public bool IsPlaying { get; set; }
    public bool IsMuted { get; set; }
    public int Volume { get; set; } = 80;
}
