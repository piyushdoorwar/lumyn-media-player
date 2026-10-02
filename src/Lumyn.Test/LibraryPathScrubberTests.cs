using Lumyn.Core.Services;

namespace Lumyn.Test;

public sealed class LibraryPathScrubberTests
{
    private const string Bundled = "/opt/lumyn/lib";

    [Fact]
    public void RemovesOnlyTheBundledDirectory()
    {
        Assert.Equal("/usr/local/lib:/home/me/lib",
            LibraryPathScrubber.Without("/opt/lumyn/lib:/usr/local/lib:/home/me/lib", Bundled));
    }

    [Fact]
    public void MatchesWithTrailingSlash()
    {
        Assert.Equal("/usr/local/lib", LibraryPathScrubber.Without("/opt/lumyn/lib/:/usr/local/lib", Bundled + "/"));
    }

    [Fact]
    public void ReturnsNullWhenNothingIsLeft()
    {
        Assert.Null(LibraryPathScrubber.Without("/opt/lumyn/lib", Bundled));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/snap/lumyn/12/usr/lib:/opt/lumyn/lib64")]
    public void LeavesUnrelatedPathsUntouched(string? path)
    {
        Assert.Equal(path, LibraryPathScrubber.Without(path, Bundled));
    }
}
