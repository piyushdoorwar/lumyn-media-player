using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Lumyn.Core.Models;

namespace Lumyn.Core.Services;

/// <summary>
/// Searches for subtitles on free, no-key-required providers and downloads results.
/// <list type="bullet">
///   <item><b>OpenSubtitles REST</b> – rest.opensubtitles.org (X-User-Agent header, no account)</item>
///   <item><b>Podnapisi</b> – www.podnapisi.net public search API (no auth)</item>
/// </list>
/// </summary>
public sealed class SubtitleSearchService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "TemporaryUserAgent");
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-User-Agent", "TemporaryUserAgent");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    // ── Language code mapping ─────────────────────────────────────────────────

    /// <summary>
    /// Supported languages: (display, OpenSubtitles ISO 639-2, Podnapisi ISO 639-1).
    /// </summary>
    public static readonly IReadOnlyList<(string Display, string OsCode, string PnCode)> Languages =
    [
        ("English",    "eng", "en"),
        ("Spanish",    "spa", "es"),
        ("French",     "fre", "fr"),
        ("German",     "ger", "de"),
        ("Italian",    "ita", "it"),
        ("Portuguese", "por", "pt"),
        ("Dutch",      "dut", "nl"),
        ("Arabic",     "ara", "ar"),
        ("Chinese",    "chi", "zh"),
        ("Japanese",   "jpn", "ja"),
        ("Korean",     "kor", "ko"),
        ("Russian",    "rus", "ru"),
    ];

    // ── Search ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Searches both providers concurrently and merges results sorted by download count.
    /// </summary>
    public async Task<IReadOnlyList<SubtitleSearchResult>> SearchAsync(
        string query,
        string osLangCode = "eng",
        string pnLangCode = "en",
        CancellationToken ct = default)
    {
        var tasks = new[]
        {
            SearchOpenSubtitlesAsync(query, osLangCode, ct),
            SearchPodnapisiAsync(query, pnLangCode, ct)
        };

        var batches = await Task.WhenAll(tasks);
        return batches
            .SelectMany(r => r)
            .OrderByDescending(r => r.Downloads)
            .ToList();
    }

    // ── OpenSubtitles REST ────────────────────────────────────────────────────

    private static async Task<IEnumerable<SubtitleSearchResult>> SearchOpenSubtitlesAsync(
        string query, string langCode, CancellationToken ct)
    {
        try
        {
            // Spaces must be replaced by hyphens in the path segment
            var encoded = Uri.EscapeDataString(query).Replace("%20", "-");
            var url = $"https://rest.opensubtitles.org/search/query-{encoded}/sublanguageid-{langCode}";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("X-User-Agent", "TemporaryUserAgent");

            var response = await Http.SendAsync(req, ct);
            if (!response.IsSuccessStatusCode) return [];

            var json = await response.Content.ReadAsStringAsync(ct);
            return ParseOpenSubtitles(json);
        }
        catch { return []; }
    }

    private static IEnumerable<SubtitleSearchResult> ParseOpenSubtitles(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith('[')) return [];

        try
        {
            using var doc = JsonDocument.Parse(json);
            var results = new List<SubtitleSearchResult>();

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var dl = GetStr(item, "SubDownloadLink");
                if (string.IsNullOrWhiteSpace(dl)) continue;

                results.Add(new SubtitleSearchResult(
                    Source:      "OpenSubtitles",
                    Title:       GetStr(item, "MovieName") ?? "",
                    FileName:    GetStr(item, "SubFileName") ?? "subtitle.srt",
                    Language:    GetStr(item, "LanguageName") ?? GetStr(item, "SubLanguageID") ?? "",
                    Format:      (GetStr(item, "SubFormat") ?? "srt").ToUpperInvariant(),
                    DownloadUrl: dl,
                    Downloads:   int.TryParse(GetStr(item, "SubDownloadsCnt"), out var d) ? d : 0
                ));
            }

            return results.Take(60);
        }
        catch { return []; }
    }

    // ── Podnapisi ─────────────────────────────────────────────────────────────

    private static async Task<IEnumerable<SubtitleSearchResult>> SearchPodnapisiAsync(
        string query, string langCode, CancellationToken ct)
    {
        try
        {
            var encoded = Uri.EscapeDataString(query);
            var url = $"https://www.podnapisi.net/subtitles/search/old?keywords={encoded}&language={langCode}&format=json";

            var response = await Http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return [];

            var json = await response.Content.ReadAsStringAsync(ct);
            return ParsePodnapisi(json);
        }
        catch { return []; }
    }

    private static IEnumerable<SubtitleSearchResult> ParsePodnapisi(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("results", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return [];

            var results = new List<SubtitleSearchResult>();

            foreach (var item in arr.EnumerateArray())
            {
                var id = GetStr(item, "id");
                if (string.IsNullOrWhiteSpace(id)) continue;

                var title   = GetStr(item, "title") ?? "";
                var lang    = GetStr(item, "language") ?? "";
                var fmt     = (GetStr(item, "format") ?? "srt").ToUpperInvariant();
                var dlUrl   = $"https://www.podnapisi.net/subtitles/{id}/download";

                var dlCount = 0;
                if (item.TryGetProperty("downloads", out var dlp) && dlp.ValueKind == JsonValueKind.Number)
                    dlp.TryGetInt32(out dlCount);

                // Build a filename from the first release name or the title
                var releaseName = title;
                if (item.TryGetProperty("releases", out var relArr) && relArr.ValueKind == JsonValueKind.Array)
                {
                    var first = relArr.EnumerateArray().FirstOrDefault().GetString();
                    if (!string.IsNullOrWhiteSpace(first)) releaseName = first;
                }

                results.Add(new SubtitleSearchResult(
                    Source:      "Podnapisi",
                    Title:       title,
                    FileName:    $"{SanitizeFileName(releaseName)}.{fmt.ToLowerInvariant()}",
                    Language:    lang,
                    Format:      fmt,
                    DownloadUrl: dlUrl,
                    Downloads:   dlCount
                ));
            }

            return results.Take(30);
        }
        catch { return []; }
    }

    // ── Download ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Downloads the subtitle to a per-session temp directory and returns the local path.
    /// OpenSubtitles delivers gzip-compressed content; Podnapisi delivers a ZIP archive.
    /// </summary>
    public async Task<string> DownloadAsync(SubtitleSearchResult result, CancellationToken ct = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Lumyn", "subtitles");
        Directory.CreateDirectory(tempDir);

        var bytes     = await Http.GetByteArrayAsync(result.DownloadUrl, ct);
        var safeFile  = SanitizeFileName(result.FileName);
        if (string.IsNullOrWhiteSpace(Path.GetExtension(safeFile)))
            safeFile += $".{result.Format.ToLowerInvariant()}";
        var destPath  = Path.Combine(tempDir, safeFile);

        switch (result.Source)
        {
            case "OpenSubtitles":
                await ExtractGzipAsync(bytes, destPath, ct);
                break;

            case "Podnapisi":
                await ExtractZipAsync(bytes, destPath, ct);
                break;

            default:
                await File.WriteAllBytesAsync(destPath, bytes, ct);
                break;
        }

        return destPath;
    }

    private static async Task ExtractGzipAsync(byte[] bytes, string destPath, CancellationToken ct)
    {
        try
        {
            using var ms  = new MemoryStream(bytes);
            using var gz  = new GZipStream(ms, CompressionMode.Decompress);
            await using var fs = File.Create(destPath);
            await gz.CopyToAsync(fs, ct);
        }
        catch (InvalidDataException)
        {
            // Content is not gzip — save raw
            await File.WriteAllBytesAsync(destPath, bytes, ct);
        }
    }

    private static async Task ExtractZipAsync(byte[] bytes, string destPath, CancellationToken ct)
    {
        using var ms  = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        // Pick the first recognised subtitle entry
        var entry = zip.Entries.FirstOrDefault(e =>
            e.Name.EndsWith(".srt", StringComparison.OrdinalIgnoreCase) ||
            e.Name.EndsWith(".ass", StringComparison.OrdinalIgnoreCase) ||
            e.Name.EndsWith(".ssa", StringComparison.OrdinalIgnoreCase) ||
            e.Name.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase) ||
            e.Name.EndsWith(".sub", StringComparison.OrdinalIgnoreCase));

        if (entry is null)
            throw new InvalidDataException("No subtitle file found inside the downloaded archive.");

        var finalPath = Path.Combine(Path.GetDirectoryName(destPath)!, SanitizeFileName(entry.Name));
        await using var fs = File.Create(finalPath);
        await using var es = entry.Open();
        await es.CopyToAsync(fs, ct);

        // Rename destPath variable — caller uses our original destPath, so just write there too
        if (finalPath != destPath)
            File.Copy(finalPath, destPath, overwrite: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? GetStr(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) ? p.GetString() : null;

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
