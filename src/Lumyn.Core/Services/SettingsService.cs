using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lumyn.Core.Services;

public sealed class SettingsService
{
    private const int MaxRecentFiles = 12;

    private readonly string _settingsPath;
    private readonly Dictionary<string, double> _resumePositions;
    private readonly List<string> _recentFiles;
    private readonly Dictionary<string, SubtitleEntry> _subtitleSettings;

    public SettingsService()
    {
        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Lumyn");
        Directory.CreateDirectory(configDir);

        _settingsPath = Path.Combine(configDir, "settings.json");

        var settings = LoadSettings();
        _resumePositions    = settings.ResumePositions;
        _recentFiles        = settings.RecentFiles;
        _subtitleSettings   = settings.SubtitleSettings;
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
            _resumePositions.Remove(key);
        else
            _resumePositions[key] = position.TotalSeconds;

        Save();
    }

    public void ClearResumePosition(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        _resumePositions.Remove(KeyForFile(filePath));
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
            RecentFiles      = _recentFiles,
            SubtitleSettings = _subtitleSettings
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
        public List<string> RecentFiles { get; set; } = [];
        public Dictionary<string, SubtitleEntry> SubtitleSettings { get; set; } = [];
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
