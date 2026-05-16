using Lumyn.Core.Services;

namespace Lumyn.Test;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _settingsDir = Path.Combine(Path.GetTempPath(), "Lumyn.Test", Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveSessionPreferences_ClampsVolumeAndSpeedAndNormalizesSeekStep()
    {
        var settings = CreateSettings();

        settings.SaveSessionPreferences(volume: 999, speed: 12.5f, seekStep: 99);

        var reloaded = CreateSettings();
        Assert.Equal(150, reloaded.LastVolume);
        Assert.Equal(4.0f, reloaded.LastSpeed);
        Assert.Equal(5, reloaded.SeekStep);
    }

    [Fact]
    public void RecentFiles_MovesExistingFileToTopAndKeepsOnlyTwelve()
    {
        var settings = CreateSettings();
        var files = Enumerable.Range(1, 14)
            .Select(i => Path.Combine(_settingsDir, $"video-{i}.mp4"))
            .ToArray();

        foreach (var file in files)
            settings.AddRecentFile(file);
        settings.AddRecentFile(files[5]);

        Assert.Equal(files[5], settings.RecentFiles[0]);
        Assert.Equal(12, settings.RecentFiles.Count);
        Assert.DoesNotContain(files[0], settings.RecentFiles);
        Assert.Equal(settings.RecentFiles.Count, settings.RecentFiles.Distinct().Count());
    }

    [Fact]
    public void ResumePosition_PersistsProgressAndRemovesNearStartOrNearEnd()
    {
        var settings = CreateSettings();
        var file = Path.Combine(_settingsDir, "movie.mkv");

        settings.SaveResumePosition(file, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(120));

        var reloaded = CreateSettings();
        var info = reloaded.GetResumeInfo(file);
        Assert.Equal(TimeSpan.FromSeconds(30), info.Position);
        Assert.Equal(25, info.ProgressPct);

        reloaded.SaveResumePosition(file, TimeSpan.FromSeconds(117), TimeSpan.FromSeconds(120));

        var cleared = CreateSettings().GetResumeInfo(file);
        Assert.Equal(TimeSpan.Zero, cleared.Position);
        Assert.Equal(-1, cleared.ProgressPct);
    }

    [Fact]
    public void PerFileSettings_UseHashedKeysInsteadOfRawPaths()
    {
        var settings = CreateSettings();
        var file = Path.Combine(_settingsDir, "private-folder", "movie.mkv");

        settings.SaveResumePosition(file, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(100));
        settings.SaveSubtitleSettings(file, new SubtitleEntry
        {
            FilePath = "subtitles/movie.srt",
            FontSize = "Large",
            Color = "Yellow",
            DelayMs = 250
        });
        settings.AddBookmark(file, TimeSpan.FromSeconds(60), "Good part");

        var json = File.ReadAllText(Path.Combine(_settingsDir, "settings.json"));
        Assert.DoesNotContain(file, json);
        Assert.Contains("subtitles/movie.srt", json);

        var reloaded = CreateSettings();
        Assert.Equal(TimeSpan.FromSeconds(30), reloaded.GetResumePosition(file));
        Assert.Equal("Yellow", reloaded.GetSubtitleSettings(file)?.Color);
        Assert.Equal("Good part", Assert.Single(reloaded.GetBookmarks(file)).Label);
    }

    [Fact]
    public void Bookmarks_AreSortedRenamedAndFormatted()
    {
        var settings = CreateSettings();
        var file = Path.Combine(_settingsDir, "movie.mp4");

        settings.AddBookmark(file, TimeSpan.FromSeconds(75), "Later");
        settings.AddBookmark(file, TimeSpan.FromSeconds(5), "Intro");
        settings.RenameBookmark(file, 1, "Scene");

        var bookmarks = settings.GetBookmarks(file);
        Assert.Collection(bookmarks,
            bookmark =>
            {
                Assert.Equal("Intro", bookmark.Label);
                Assert.Equal("0:05", bookmark.FormattedTime);
            },
            bookmark =>
            {
                Assert.Equal("Scene", bookmark.Label);
                Assert.Equal("1:15", bookmark.FormattedTime);
            });
    }

    private SettingsService CreateSettings() => new(_settingsDir);

    public void Dispose()
    {
        if (Directory.Exists(_settingsDir))
            Directory.Delete(_settingsDir, recursive: true);
    }
}
