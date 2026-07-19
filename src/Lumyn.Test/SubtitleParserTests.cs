using Lumyn.Core.Services;

namespace Lumyn.Test;

public sealed class SubtitleParserTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "Lumyn.Test", Guid.NewGuid().ToString("N"));

    public SubtitleParserTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Parse_SrtSubtitle_ReturnsTimedLinesAndStripsHtml()
    {
        var path = WriteFile("sample.srt", """
            1
            00:00:01,250 --> 00:00:03,500
            <i>Hello</i> there

            2
            00:01:02.000 --> 00:01:04.250
            Second line
            wraps here
            """);

        var lines = SubtitleParser.Parse(path);

        Assert.Collection(lines,
            line =>
            {
                Assert.Equal(TimeSpan.FromMilliseconds(1250), line.Start);
                Assert.Equal(TimeSpan.FromMilliseconds(3500), line.End);
                Assert.Equal("Hello there", line.Text);
            },
            line =>
            {
                Assert.Equal(TimeSpan.FromSeconds(62), line.Start);
                Assert.Equal(TimeSpan.FromMilliseconds(64250), line.End);
                Assert.Equal("Second line\nwraps here", line.Text);
            });
    }

    [Fact]
    public void Parse_AssSubtitle_ReturnsDialogueTextWithoutOverrideTags()
    {
        var path = WriteFile("sample.ass", """
            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            Dialogue: 0,0:00:05.10,0:00:07.40,Default,,0,0,0,,{\an8}Top line\NSecond line
            Dialogue: 0,0:00:08.00,0:00:09.00,Default,,0,0,0,,{\pos(1,2)}
            """);

        var lines = SubtitleParser.Parse(path);

        var line = Assert.Single(lines);
        Assert.Equal(TimeSpan.FromMilliseconds(5100), line.Start);
        Assert.Equal(TimeSpan.FromMilliseconds(7400), line.End);
        Assert.Equal("Top line\nSecond line", line.Text);
    }

    [Fact]
    public void Parse_MissingOrUnreadableFile_ReturnsEmptyList()
    {
        var lines = SubtitleParser.Parse(Path.Combine(_tempDir, "missing.srt"));

        Assert.Empty(lines);
    }

    [Fact]
    public void Parse_WebVttWithoutHours_ParsesTiming()
    {
        var path = WriteFile("sample.vtt", """
            WEBVTT

            00:01.250 --> 00:03.500
            Short cue
            """);

        var line = Assert.Single(SubtitleParser.Parse(path));
        Assert.Equal(TimeSpan.FromMilliseconds(1250), line.Start);
        Assert.Equal(TimeSpan.FromMilliseconds(3500), line.End);
    }

    [Fact]
    public void Parse_Windows1252Subtitle_DecodesSmartQuotes()
    {
        var path = Path.Combine(_tempDir, "legacy.srt");
        File.WriteAllBytes(path,
        [
            .. System.Text.Encoding.ASCII.GetBytes("1\r\n00:00:01,000 --> 00:00:02,000\r\n"),
            0x93,
            .. System.Text.Encoding.ASCII.GetBytes("Hello"),
            0x94
        ]);

        Assert.Equal("“Hello”", Assert.Single(SubtitleParser.Parse(path)).Text);
    }

    private string WriteFile(string fileName, string content)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
