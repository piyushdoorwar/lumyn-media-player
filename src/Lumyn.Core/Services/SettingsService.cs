using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lumyn.Core.Services;

public sealed class SettingsService
{
    private const int MaxRecentFiles = 12;

    private readonly string _settingsPath;
    private readonly Dictionary<string, double> _resumePositions;
    private readonly Dictionary<string, double> _resumeDurations;
    private readonly List<string> _recentFiles;
    private readonly Dictionary<string, SubtitleEntry> _subtitleSettings;
    private readonly Dictionary<string, List<BookmarkEntry>> _bookmarks;

    public SettingsService()
    {
        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Lumyn");
        Directory.CreateDirectory(configDir);

        _settingsPath = Path.Combine(configDir, "settings.json");

        var settings = LoadSettings();
        _resumePositions  = settings.ResumePositions;
        _resumeDurations  = settings.ResumeDurations;
        _recentFiles      = settings.RecentFiles;
        _subtitleSettings = settings.SubtitleSettings;
        _bookmarks        = settings.Bookmarks;
    }

    public IReadOnlyList<string> RecentFiles => _recentFiles.AsReadOnly();

    // ── Recent files ────────────────────────────────────────────────────────

    public void AddRecentFile(string filePath)
    {
        _recentFiles.Remove(filePath);
        _recentFiles.Insert(0, filePath);
        if (_recentFiles.Count > MaxRecentFiles)
            _recentFiles.RemoveRange(MaxRecentFiles, _recentFiles.Count - MaxRecentFiles);
        Save();
    }

    // ── Resume positions ────────────────────────────────────────────────────

    public TimeSpan GetResumePosition(string filePath)
    {
        return _resumePositions.TryGetValue(KeyForFile(filePath), out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.Zero;
    }

    public void SaveResumePosition(string? filePath, TimeSpan position, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;

        var key = KeyForFile(filePath);
        if (position.TotalSeconds < 5 || (duration > TimeSpan.Zero && duration - position < TimeSpan.FromSeconds(5)))
        {
            _resumePositions.Remove(key);
            _resumeDurations.Remove(key);
        }
        else
        {
            _resumePositions[key] = position.TotalSeconds;
            if (duration > TimeSpan.Zero)
                _resumeDurations[key] = duration.TotalSeconds;
        }

        Save();
    }

    /// <summary>Returns resume position and progress 0–100, or (Zero, -1) if none saved.</summary>
    public (TimeSpan Position, double ProgressPct) GetResumeInfo(string filePath)
    {
        var key = KeyForFile(filePath);
        if (!_resumePositions.TryGetValue(key, out var pos)) return (TimeSpan.Zero, -1);
        double pct = -1;
        if (_resumeDurations.TryGetValue(key, out var dur) && dur > 0)
            pct = Math.Clamp(pos / dur * 100.0, 0, 100);
        return (TimeSpan.FromSeconds(pos), pct);
    }

    public void ClearResumePosition(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        _resumePositions.Remove(KeyForFile(filePath));
        Save();
    }

    // ── Bookmarks ────────────────────────────────────────────────────────────

    public IReadOnlyList<BookmarkEntry> GetBookmarks(string filePath)
    {
        _bookmarks.TryGetValue(KeyForFile(filePath), out var list);
        return (list ?? []).AsReadOnly();
    }

    public void AddBookmark(string filePath, TimeSpan position, string label)
    {
        var key = KeyForFile(filePath);
        if (!_bookmarks.TryGetValue(key, out var list))
            _bookmarks[key] = list = [];
        list.Add(new BookmarkEntry { PositionSeconds = position.TotalSeconds, Label = label });
        list.Sort((a, b) => a.PositionSeconds.CompareTo(b.PositionSeconds));
        Save();
    }

    public void RemoveBookmark(string filePath, int index)
    {
        var key = KeyForFile(filePath);
        if (!_bookmarks.TryGetValue(key, out var list) || index < 0 || index >= list.Count) return;
        list.RemoveAt(index);
        Save();
    }

    // ── Persistence ─────────────────────────────────────────────────────────

    private SettingsFile LoadSettings()
    {
        if (!File.Exists(_settingsPath)) return new SettingsFile();
        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<SettingsFile>(json) ?? new SettingsFile();
        }
        catch
        {
            return new SettingsFile();
        }
    }

    private void Save()
    {
        var settings = new SettingsFile
        {
            ResumePositions  = _resumePositions,
            ResumeDurations  = _resumeDurations,
            RecentFiles      = _recentFiles,
            SubtitleSettings = _subtitleSettings,
            Bookmarks        = _bookmarks
        };
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsPath, json);
    }

    private static string KeyForFile(string filePath)
    {
        var normalized = Path.GetFullPath(filePath);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // ── Subtitle settings per file ───────────────────────────────────────────

    public SubtitleEntry? GetSubtitleSettings(string filePath)
    {
        _subtitleSettings.TryGetValue(KeyForFile(filePath), out var entry);
        return entry;
    }

    public void SaveSubtitleSettings(string filePath, SubtitleEntry entry)
    {
        _subtitleSettings[KeyForFile(filePath)] = entry;
        Save();
    }

    public void ClearSubtitleSettings(string filePath)
    {
        if (_subtitleSettings.Remove(KeyForFile(filePath)))
            Save();
    }

    private sealed class SettingsFile
    {
        public Dictionary<string, double> ResumePositions { get; set; } = [];
        public Dictionary<string, double> ResumeDurations { get; set; } = [];
        public List<string> RecentFiles { get; set; } = [];
        public Dictionary<string, SubtitleEntry> SubtitleSettings { get; set; } = [];
        public Dictionary<string, List<BookmarkEntry>> Bookmarks { get; set; } = [];
    }
}

/// <summary>Subtitle configuration cached per media file.</summary>
public sealed class SubtitleEntry
{
    public string? FilePath  { get; set; }
    public string  FontSize  { get; set; } = "Medium";
    public string  Font      { get; set; } = "SansSerif";
    public string  Color     { get; set; } = "White";
    public long    DelayMs   { get; set; }
    public int?    EmbeddedTrackId { get; set; }
}

/// <summary>A user-defined timestamp bookmark for a media file.</summary>
public sealed class BookmarkEntry
{
    public double PositionSeconds { get; set; }
    public string Label           { get; set; } = "";

    public TimeSpan Position => TimeSpan.FromSeconds(PositionSeconds);

    public string FormattedTime
    {
        get
        {
            var t = Position;
            return t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
                : $"{t.Minutes}:{t.Seconds:D2}";
        }
    }
}
