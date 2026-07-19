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
        settings.Flush();

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
        settings.Flush();

        var reloaded = CreateSettings();
        var info = reloaded.GetResumeInfo(file);
        Assert.Equal(TimeSpan.FromSeconds(30), info.Position);
        Assert.Equal(25, info.ProgressPct);

        reloaded.SaveResumePosition(file, TimeSpan.FromSeconds(117), TimeSpan.FromSeconds(120));
        reloaded.Flush();

        var cleared = CreateSettings().GetResumeInfo(file);
        Assert.Equal(TimeSpan.Zero, cleared.Position);
        Assert.Equal(-1, cleared.ProgressPct);
    }

    [Fact]
    public void ClearResumePosition_RemovesPositionAndDuration()
    {
        var settings = CreateSettings();
        var file = Path.Combine(_settingsDir, "movie.mp4");
        settings.SaveResumePosition(file, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(120));
        settings.ClearResumePosition(file);
        settings.Flush();

        using var json = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(_settingsDir, "settings.json")));
        Assert.Empty(json.RootElement.GetProperty("ResumePositions").EnumerateObject());
        Assert.Empty(json.RootElement.GetProperty("ResumeDurations").EnumerateObject());
    }

    [Fact]
    public void LoadSettings_NormalizesNullCollectionsAndInvalidPreferences()
    {
        Directory.CreateDirectory(_settingsDir);
        File.WriteAllText(Path.Combine(_settingsDir, "settings.json"),
            """{"ResumePositions":null,"RecentFiles":null,"LastVolume":999,"LastSpeed":99,"SeekStep":42}""");

        using var settings = CreateSettings();
        Assert.Empty(settings.RecentFiles);
        Assert.Equal(150, settings.LastVolume);
        Assert.Equal(4.0f, settings.LastSpeed);
        Assert.Equal(5, settings.SeekStep);
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
        settings.Flush();

        var json = File.ReadAllText(Path.Combine(_settingsDir, "settings.json"));
        Assert.DoesNotContain(file, json);
        Assert.Contains("subtitles/movie.srt", json);

        var reloaded = CreateSettings();
        Assert.Equal(TimeSpan.FromSeconds(30), reloaded.GetResumePosition(file));
        Assert.Equal("Yellow", reloaded.GetSubtitleSettings(file)?.Color);
        Assert.Equal("Good part", Assert.Single(reloaded.GetBookmarks(file)).Label);
    }

    [Fact]
    public void AudioSettings_PersistPerFileAndCanBeCleared()
    {
        var settings = CreateSettings();
        var file = Path.Combine(_settingsDir, "movie.mp4");

        settings.SaveAudioSettings(file, new AudioSettingsEntry { Mode = "VoiceBoost" });
        settings.Flush();

        Assert.Equal("VoiceBoost", CreateSettings().GetAudioSettings(file)?.Mode);

        settings.ClearAudioSettings(file);
        settings.Flush();

        Assert.Null(CreateSettings().GetAudioSettings(file));
    }

    [Fact]
    public void UiVisibility_PersistsGlobalControlVisibility()
    {
        var settings = CreateSettings();

        settings.SaveUiVisibility(new UiVisibilitySettings
        {
            ShowScreenshot = false,
            ShowPin = true,
            ShowCast = false,
            ShowSeekStep = true,
            ShowLoop = true,
            ShowMarkers = false
        });
        settings.Flush();

        var reloaded = CreateSettings().UiVisibility;
        Assert.False(reloaded.ShowScreenshot);
        Assert.True(reloaded.ShowPin);
        Assert.False(reloaded.ShowCast);
        Assert.True(reloaded.ShowSeekStep);
        Assert.True(reloaded.ShowLoop);
        Assert.False(reloaded.ShowMarkers);
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

    [Fact]
    public void WindowGeometry_RoundTripsAfterFlush()
    {
        var settings = CreateSettings();

        Assert.Null(settings.GetWindowGeometry());

        settings.SaveWindowGeometry(width: 1280, height: 720, x: 40, y: 60, maximized: true);
        settings.Flush();

        var geo = CreateSettings().GetWindowGeometry();
        Assert.NotNull(geo);
        Assert.Equal(1280, geo!.Width);
        Assert.Equal(720, geo.Height);
        Assert.Equal(40, geo.X);
        Assert.Equal(60, geo.Y);
        Assert.True(geo.Maximized);
    }

    [Fact]
    public void Flush_PersistsBufferedWritesBeforeReload()
    {
        var settings = CreateSettings();

        settings.SaveSessionPreferences(volume: 55, speed: 1.5f, seekStep: 10);
        settings.Flush();

        var reloaded = CreateSettings();
        Assert.Equal(55, reloaded.LastVolume);
        Assert.Equal(1.5f, reloaded.LastSpeed);
        Assert.Equal(10, reloaded.SeekStep);
    }

    [Fact]
    public void ResumePreferences_DefaultAudioOffVideoOn_AndRoundTrip()
    {
        var settings = CreateSettings();

        // Defaults: audio off, video on.
        Assert.False(settings.ResumeAudio);
        Assert.True(settings.ResumeVideo);
        Assert.False(settings.ResumeEnabledFor(isAudio: true));
        Assert.True(settings.ResumeEnabledFor(isAudio: false));

        settings.SetResumePreferences(audio: true, video: false);
        settings.Flush();

        var reloaded = CreateSettings();
        Assert.True(reloaded.ResumeAudio);
        Assert.False(reloaded.ResumeVideo);
        Assert.True(reloaded.ResumeEnabledFor(isAudio: true));
        Assert.False(reloaded.ResumeEnabledFor(isAudio: false));
    }

    private SettingsService CreateSettings() => new(_settingsDir);

    public void Dispose()
    {
        if (Directory.Exists(_settingsDir))
            Directory.Delete(_settingsDir, recursive: true);
    }
}
