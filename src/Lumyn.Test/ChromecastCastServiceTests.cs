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

    [Theory]
    [InlineData("bytes=0-99", 1000, 0, 99)]
    [InlineData("bytes=900-", 1000, 900, 999)]
    [InlineData("bytes=-100", 1000, 900, 999)]
    [InlineData("bytes=0-5000", 1000, 0, 999)]
    public void TryParseRange_HandlesValidSingleRanges(
        string value, long length, long expectedStart, long expectedEnd)
    {
        Assert.True(ChromecastCastService.TryParseRange(value, length, out var start, out var end));
        Assert.Equal(expectedStart, start);
        Assert.Equal(expectedEnd, end);
    }

    [Theory]
    [InlineData("bytes=1000-")]
    [InlineData("bytes=20-10")]
    [InlineData("bytes=0-1,5-6")]
    [InlineData("items=0-1")]
    public void TryParseRange_RejectsInvalidOrMultipleRanges(string value)
    {
        Assert.False(ChromecastCastService.TryParseRange(value, 1000, out _, out _));
    }
}
