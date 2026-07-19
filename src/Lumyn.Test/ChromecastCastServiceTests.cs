using Lumyn.Core.Services;

namespace Lumyn.Test;

public sealed class ChromecastCastServiceTests
{
    [Theory]
    [InlineData("movie.mkv")]
    [InlineData("clip.AVI")]
    [InlineData("recording.m2ts")]
    [InlineData("/media/video/movie.mov")]
    public void IsUnsupportedFormat_ReturnsTrueForContainersChromecastCannotPlayNatively(string path)
    {
        Assert.True(ChromecastCastService.IsUnsupportedFormat(path));
    }

    [Theory]
    [InlineData("movie.mp4")]
    [InlineData("clip.webm")]
    [InlineData("song.mp3")]
    [InlineData("track.flac")]
    public void IsUnsupportedFormat_ReturnsFalseForSupportedMediaExtensions(string path)
    {
        Assert.False(ChromecastCastService.IsUnsupportedFormat(path));
    }
}
