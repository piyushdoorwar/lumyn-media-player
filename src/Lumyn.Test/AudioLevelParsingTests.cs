using Lumyn.Core.Services;

namespace Lumyn.Test;

public sealed class AudioLevelParsingTests
{
    [Fact]
    public void ReadsOverallRmsFromMetadataMap()
    {
        const string json = """{"lavfi.astats.1.RMS_level":"-26.1","lavfi.astats.Overall.RMS_level":"-24.991298"}""";

        Assert.True(PlaybackService.TryReadRmsLevel(json, out var db));
        Assert.Equal(-24.991298, db, 6);
    }

    [Fact]
    public void TreatsMinusInfAsSilence()
    {
        Assert.True(PlaybackService.TryReadRmsLevel("""{"lavfi.astats.Overall.RMS_level":"-inf"}""", out var db));
        Assert.True(double.IsNegativeInfinity(db));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("""{"lavfi.astats.Overall.RMS_level":"nan-ish"}""")]
    [InlineData("""["lavfi.astats.Overall.RMS_level"]""")]
    public void RejectsMissingOrMalformedValues(string? json)
    {
        Assert.False(PlaybackService.TryReadRmsLevel(json, out _));
    }
}
