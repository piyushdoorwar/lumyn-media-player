using Lumyn.Core.Services;

namespace Lumyn.Test;

public sealed class SettingsServiceThumbnailTests : IDisposable
{
    private readonly string _settingsDir =
        Path.Combine(Path.GetTempPath(), "Lumyn.Test", Guid.NewGuid().ToString("N"));

    // ── GetThumbnailPath ─────────────────────────────────────────────────────

    [Fact]
    public void GetThumbnailPath_SameFilePath_ReturnsSamePath()
    {
        var settings = CreateSettings();
        var file = "/home/user/videos/movie.mkv";

        Assert.Equal(settings.GetThumbnailPath(file), settings.GetThumbnailPath(file));
    }

    [Fact]
    public void GetThumbnailPath_DifferentFilePaths_ReturnDifferentPaths()
    {
        var settings = CreateSettings();

        var path1 = settings.GetThumbnailPath("/videos/movie-a.mkv");
        var path2 = settings.GetThumbnailPath("/videos/movie-b.mkv");

        Assert.NotEqual(path1, path2);
    }

    [Fact]
    public void GetThumbnailPath_DoesNotContainRawFilePath()
    {
        var settings = CreateSettings();
        var file = "/home/user/private-folder/secret-movie.mkv";

        var thumbPath = settings.GetThumbnailPath(file);

        Assert.DoesNotContain("secret-movie", thumbPath);
        Assert.DoesNotContain("private-folder", thumbPath);
    }

    [Fact]
    public void GetThumbnailPath_ResultIsInsideThumbsDirectory()
    {
        var settings = CreateSettings();
        var thumbPath = settings.GetThumbnailPath("/videos/movie.mkv");

        Assert.StartsWith(_settingsDir, thumbPath);
        Assert.EndsWith(".jpg", thumbPath);
    }

    [Fact]
    public void GetThumbnailPath_IsDeterministicAcrossInstances()
    {
        var file = "/home/user/videos/movie.mkv";

        var path1 = CreateSettings().GetThumbnailPath(file);
        var path2 = CreateSettings().GetThumbnailPath(file);

        Assert.Equal(path1, path2);
    }

    // ── GetExistingThumbnail ─────────────────────────────────────────────────

    [Fact]
    public void GetExistingThumbnail_ReturnsNullWhenFileAbsent()
    {
        var settings = CreateSettings();

        Assert.Null(settings.GetExistingThumbnail("/videos/no-thumbnail.mkv"));
    }

    [Fact]
    public void GetExistingThumbnail_ReturnsPathWhenFilePresent()
    {
        var settings = CreateSettings();
        var file = "/videos/has-thumbnail.mkv";
        var thumbPath = settings.GetThumbnailPath(file);
        Directory.CreateDirectory(Path.GetDirectoryName(thumbPath)!);
        File.WriteAllBytes(thumbPath, [0xFF, 0xD8]); // minimal JPEG header

        Assert.Equal(thumbPath, settings.GetExistingThumbnail(file));
    }

    [Fact]
    public void GetExistingThumbnail_ReturnsNullAfterFileDeleted()
    {
        var settings = CreateSettings();
        var file = "/videos/temporary.mkv";
        var thumbPath = settings.GetThumbnailPath(file);
        Directory.CreateDirectory(Path.GetDirectoryName(thumbPath)!);
        File.WriteAllBytes(thumbPath, [0xFF, 0xD8]);

        Assert.NotNull(settings.GetExistingThumbnail(file));

        File.Delete(thumbPath);

        Assert.Null(settings.GetExistingThumbnail(file));
    }

    // ── PruneOrphanedThumbnails ──────────────────────────────────────────────

    [Fact]
    public void PruneOrphanedThumbnails_DeletesFilesNotInRecentList()
    {
        var settings = CreateSettings();
        var kept     = "/videos/kept.mkv";
        var orphan   = "/videos/orphan.mkv";

        settings.AddRecentFile(kept);

        // Write both thumbnail files to disk.
        var keptPath   = settings.GetThumbnailPath(kept);
        var orphanPath = settings.GetThumbnailPath(orphan);
        File.WriteAllBytes(keptPath,   [0xFF, 0xD8]);
        File.WriteAllBytes(orphanPath, [0xFF, 0xD8]);

        settings.PruneOrphanedThumbnails();

        Assert.True(File.Exists(keptPath),    "thumbnail for recent file should be kept");
        Assert.False(File.Exists(orphanPath), "thumbnail for non-recent file should be deleted");
    }

    [Fact]
    public void PruneOrphanedThumbnails_KeepsAllFilesWhenAllAreRecent()
    {
        var settings = CreateSettings();
        var files = Enumerable.Range(1, 3).Select(i => $"/videos/movie{i}.mkv").ToArray();

        foreach (var f in files)
        {
            settings.AddRecentFile(f);
            File.WriteAllBytes(settings.GetThumbnailPath(f), [0xFF, 0xD8]);
        }

        settings.PruneOrphanedThumbnails();

        foreach (var f in files)
            Assert.True(File.Exists(settings.GetThumbnailPath(f)));
    }

    [Fact]
    public void PruneOrphanedThumbnails_EmptyRecentList_DeletesAll()
    {
        var settings   = CreateSettings();
        var orphanPath = settings.GetThumbnailPath("/videos/old.mkv");
        File.WriteAllBytes(orphanPath, [0xFF, 0xD8]);

        settings.PruneOrphanedThumbnails();

        Assert.False(File.Exists(orphanPath));
    }

    private SettingsService CreateSettings() => new(_settingsDir);

    public void Dispose()
    {
        if (Directory.Exists(_settingsDir))
            Directory.Delete(_settingsDir, recursive: true);
    }
}
