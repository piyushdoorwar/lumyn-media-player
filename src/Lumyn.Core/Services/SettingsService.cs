using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lumyn.Core.Services;

public sealed class SettingsService
{
    private readonly string _settingsPath;
    private readonly Dictionary<string, double> _resumePositions;

    public SettingsService()
    {
        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Lumyn");
        Directory.CreateDirectory(configDir);

        _settingsPath = Path.Combine(configDir, "settings.json");
        _resumePositions = LoadResumePositions();
    }

    public TimeSpan GetResumePosition(string filePath)
    {
        return _resumePositions.TryGetValue(KeyForFile(filePath), out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.Zero;
    }

    public void SaveResumePosition(string? filePath, TimeSpan position, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var key = KeyForFile(filePath);
        if (position.TotalSeconds < 5 || (duration > TimeSpan.Zero && duration - position < TimeSpan.FromSeconds(5)))
        {
            _resumePositions.Remove(key);
        }
        else
        {
            _resumePositions[key] = position.TotalSeconds;
        }

        Save();
    }

    public void ClearResumePosition(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        _resumePositions.Remove(KeyForFile(filePath));
        Save();
    }

    private Dictionary<string, double> LoadResumePositions()
    {
        if (!File.Exists(_settingsPath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<SettingsFile>(json);
            return settings?.ResumePositions ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void Save()
    {
        var settings = new SettingsFile { ResumePositions = _resumePositions };
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsPath, json);
    }

    private static string KeyForFile(string filePath)
    {
        var normalized = Path.GetFullPath(filePath);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed class SettingsFile
    {
        public Dictionary<string, double> ResumePositions { get; set; } = [];
    }
}
