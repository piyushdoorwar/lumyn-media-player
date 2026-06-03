using Lumyn.Core.Services;

namespace Lumyn.Test;

public sealed class MediaTimeTests
{
    [Theory]
    [InlineData(0, "")]            // zero / unknown → blank
    [InlineData(5, "0:05")]
    [InlineData(65, "1:05")]
    [InlineData(173, "2:53")]      // the YouTube-Music example
    [InlineData(600, "10:00")]
    [InlineData(3661, "1:01:01")]  // past an hour → h:mm:ss
    public void FormatDuration_FormatsAsMinutesSeconds(int totalSeconds, string expected)
    {
        var result = MediaTime.FormatDuration(TimeSpan.FromSeconds(totalSeconds));
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FormatDuration_NullOrNegative_IsBlank()
    {
        Assert.Equal(string.Empty, MediaTime.FormatDuration(null));
        Assert.Equal(string.Empty, MediaTime.FormatDuration(TimeSpan.FromSeconds(-5)));
    }
}
