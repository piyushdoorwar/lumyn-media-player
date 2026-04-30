namespace Lumyn.Core.Models;

/// <summary>Raw frame data from libvlc's software renderer. Buffer is valid only during the callback.</summary>
public sealed class VideoFrameData(IntPtr buffer, uint width, uint height, uint pitch)
{
    public IntPtr Buffer { get; } = buffer;
    public uint Width    { get; } = width;
    public uint Height   { get; } = height;
    public uint Pitch    { get; } = pitch;
}
