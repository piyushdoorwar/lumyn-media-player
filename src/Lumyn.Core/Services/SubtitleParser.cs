using System.Text;
using System.Text.RegularExpressions;

namespace Lumyn.Core.Services;

/// <summary>A single timed subtitle entry.</summary>
public sealed record SubtitleLine(TimeSpan Start, TimeSpan End, string Text);

/// <summary>
/// Minimal parser for SRT and basic ASS/SSA subtitle files.
/// Only the timing and visible text is extracted — styling is ignored.
/// </summary>
public static class SubtitleParser
{
    private const long MaxSubtitleBytes = 32L * 1024 * 1024;
    // ── SRT ───────────────────────────────────────────────────────────────────
    // Matches: 00:01:23,456 --> 00:01:27,890
    private static readonly Regex SrtTimecode = new(
        @"(?:(?<sh>\d+):)?(?<sm>\d{1,2}):(?<ss>\d{2})[,\.](?<sf>\d{1,3})\s*-->\s*(?:(?<eh>\d+):)?(?<em>\d{1,2}):(?<es>\d{2})[,\.](?<ef>\d{1,3})",
        RegexOptions.Compiled);

    // ── ASS / SSA ─────────────────────────────────────────────────────────────
    // Matches the Dialogue line: Layer,Start,End,Style,Name,MarginL,R,V,Effect,Text
    private static readonly Regex AssDialogue = new(
        @"^Dialogue:\s*\d+,(\d+:\d+:\d+\.\d+),(\d+:\d+:\d+\.\d+),[^,]*,[^,]*,[^,]*,[^,]*,[^,]*,[^,]*,(.*)$",
        RegexOptions.Compiled);

    // Strips ASS inline override tags like {\pos(...)}, {\an8}, {\c&H...} etc.
    private static readonly Regex AssInlineTags = new(@"\{[^}]*\}", RegexOptions.Compiled);

    // Strips HTML-like tags left in some SRT files
    private static readonly Regex HtmlTags = new(@"<[^>]+>", RegexOptions.Compiled);

    /// <summary>Parse a subtitle file, auto-detecting SRT vs ASS/SSA.</summary>
    public static List<SubtitleLine> Parse(string filePath)
    {
        if (!File.Exists(filePath)) return [];

        string[] lines;
        try
        {
            var info = new FileInfo(filePath);
            if (info.Length > MaxSubtitleBytes) return [];
            lines = DecodeSubtitleBytes(File.ReadAllBytes(filePath)).ReplaceLineEndings("\n").Split('\n');
        }
        catch { return []; }

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var result = ext is ".ass" or ".ssa" ? ParseAss(lines) : ParseSrt(lines);
        result.Sort((a, b) => a.Start.CompareTo(b.Start));
        return result;
    }

    // ── SRT parser ────────────────────────────────────────────────────────────

    private static List<SubtitleLine> ParseSrt(string[] lines)
    {
        var result = new List<SubtitleLine>();
        int i = 0;

        while (i < lines.Length)
        {
            // Skip blank lines and sequence numbers
            while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i])) i++;
            if (i >= lines.Length) break;

            // Skip a purely numeric sequence number if present
            if (int.TryParse(lines[i].Trim(), out _)) i++;
            if (i >= lines.Length) break;

            // Timecode line
            var m = SrtTimecode.Match(lines[i]);
            if (!m.Success) { i++; continue; }

            var start = ParseSrtTime(m, "s");
            var end   = ParseSrtTime(m, "e");
            i++;

            // Text lines until blank or EOF
            var text = new System.Text.StringBuilder();
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]))
            {
                if (text.Length > 0) text.Append('\n');
                text.Append(HtmlTags.Replace(lines[i], ""));
                i++;
            }

            var t = text.ToString().Trim();
            if (t.Length > 0)
                result.Add(new SubtitleLine(start, end, t));
        }

        return result;
    }

    private static TimeSpan ParseSrtTime(Match m, string prefix)
    {
        var hours = int.TryParse(m.Groups[$"{prefix}h"].Value, out var h) ? h : 0;
        var minutes = int.Parse(m.Groups[$"{prefix}m"].Value);
        var seconds = int.Parse(m.Groups[$"{prefix}s"].Value);
        var fraction = m.Groups[$"{prefix}f"].Value.PadRight(3, '0');
        var milliseconds = int.Parse(fraction[..3]);
        return new TimeSpan(0, hours, minutes, seconds, milliseconds);
    }

    internal static string DecodeSubtitleBytes(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { return DecodeWindows1252(bytes); }
    }

    private static string DecodeWindows1252(byte[] bytes)
    {
        ReadOnlySpan<char> controls =
            "€\u0081‚ƒ„…†‡ˆ‰Š‹Œ\u008dŽ\u008f\u0090‘’“”•–—˜™š›œ\u009džŸ";
        var chars = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            var value = bytes[i];
            chars[i] = value is >= 0x80 and <= 0x9F ? controls[value - 0x80] : (char)value;
        }
        return new string(chars);
    }

    // ── ASS / SSA parser ──────────────────────────────────────────────────────

    private static List<SubtitleLine> ParseAss(string[] lines)
    {
        var result = new List<SubtitleLine>();

        foreach (var line in lines)
        {
            var m = AssDialogue.Match(line);
            if (!m.Success) continue;

            if (!TryParseAssTime(m.Groups[1].Value, out var start)) continue;
            if (!TryParseAssTime(m.Groups[2].Value, out var end))   continue;

            var raw  = m.Groups[3].Value;
            // Strip override tags and \N / \n hard line breaks
            var text = AssInlineTags.Replace(raw, "")
                          .Replace(@"\N", "\n")
                          .Replace(@"\n", "\n")
                          .Trim();

            if (text.Length > 0)
                result.Add(new SubtitleLine(start, end, text));
        }

        return result;
    }

    // ASS time format: H:MM:SS.cc  (centiseconds)
    private static bool TryParseAssTime(string s, out TimeSpan t)
    {
        t = default;
        var parts = s.Split(':');
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], out var h)) return false;
        if (!int.TryParse(parts[1], out var min)) return false;
        var secParts = parts[2].Split('.');
        if (!int.TryParse(secParts[0], out var sec)) return false;
        var cs = secParts.Length > 1 && int.TryParse(secParts[1], out var x) ? x * 10 : 0;
        t = new TimeSpan(0, h, min, sec, cs);
        return true;
    }
}
