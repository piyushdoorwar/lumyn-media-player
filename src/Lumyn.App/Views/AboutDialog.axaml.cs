using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Lumyn.App.Views;

public partial class AboutDialog : Window
{
    private const string GitHubReleasesApi =
        "https://api.github.com/repos/piyushdoorwar/lumyn-media-player/releases/latest";
    private const string GitHubReleasesPage =
        "https://piyushdoorwar.github.io/lumyn-media-player/releases/";

    private static readonly string AppVersion = GetAppVersion();
    private string? _latestReleaseUrl;

    public AboutDialog()
    {
        AvaloniaXamlLoader.Load(this);

        SetField("VersionText", $"Version {AppVersion}");
        SetField("OsText", GetOsName());
        SetField("ArchText", RuntimeInformation.OSArchitecture.ToString());
        SetField("RuntimeText", $".NET {Environment.Version}");
        SetField("UpdateStatusText", "");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void SetField(string name, string text)
    {
        if (this.FindControl<TextBlock>(name) is { } tb) tb.Text = text;
    }

    private static string GetAppVersion()
    {
        // Look for VERSION file next to the executable.
        var exeDir = AppContext.BaseDirectory;
        var versionFile = Path.Combine(exeDir, "VERSION");
        if (File.Exists(versionFile))
        {
            var v = File.ReadAllText(versionFile).Trim();
            if (!string.IsNullOrWhiteSpace(v)) return v;
        }
        return typeof(AboutDialog).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    private static string GetOsName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return $"Windows ({Environment.OSVersion.Version})";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return $"macOS ({Environment.OSVersion.Version})";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Try to get a friendly distro name from /etc/os-release.
            try
            {
                var lines = File.ReadAllLines("/etc/os-release");
                var prettyLine = lines.FirstOrDefault(l => l.StartsWith("PRETTY_NAME="));
                if (prettyLine is not null)
                    return prettyLine.Split('=', 2)[1].Trim('"');
            }
            catch { /* fall through */ }
            return "Linux";
        }
        return RuntimeInformation.OSDescription;
    }

    // ── Update check ─────────────────────────────────────────────────────────

    private async void CheckUpdates_Click(object? sender, RoutedEventArgs e)
    {
        var btn = this.FindControl<Button>("CheckUpdatesButton");
        var statusText = this.FindControl<TextBlock>("UpdateStatusText");
        var openBtn = this.FindControl<Button>("OpenReleasesButton");

        if (btn is not null) { btn.IsEnabled = false; btn.Content = "Checking…"; }
        if (statusText is not null) statusText.Text = "";

        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("Lumyn", AppVersion));
            http.Timeout = TimeSpan.FromSeconds(10);

            var response = await http.GetAsync(GitHubReleasesApi);

            // 404 = no releases published yet; 403 = rate-limited.
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (statusText is not null)
                    {
                        statusText.Foreground = new Avalonia.Media.SolidColorBrush(
                            Avalonia.Media.Color.Parse("#6A6560"));
                        statusText.Text = "No releases published yet.";
                    }
                    if (btn is not null) { btn.Content = "Check for Updates"; btn.IsEnabled = true; }
                });
                return;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (statusText is not null)
                    {
                        statusText.Foreground = new Avalonia.Media.SolidColorBrush(
                            Avalonia.Media.Color.Parse("#B05050"));
                        statusText.Text = "GitHub rate limit reached. Try again later.";
                    }
                    if (btn is not null) { btn.Content = "Check for Updates"; btn.IsEnabled = true; }
                });
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (statusText is not null)
                    {
                        statusText.Foreground = new Avalonia.Media.SolidColorBrush(
                            Avalonia.Media.Color.Parse("#B05050"));
                        statusText.Text = $"GitHub returned {(int)response.StatusCode}. Try again later.";
                    }
                    if (btn is not null) { btn.Content = "Check for Updates"; btn.IsEnabled = true; }
                });
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString() ?? "";
            var latestVersion = tagName.TrimStart('v');
            _latestReleaseUrl = root.TryGetProperty("html_url", out var urlProp)
                ? urlProp.GetString()
                : GitHubReleasesPage;

            var isNewer = IsNewerVersion(latestVersion, AppVersion);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (statusText is not null)
                {
                    statusText.Foreground = isNewer
                        ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3A9B4B"))
                        : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#6A6560"));
                    statusText.Text = isNewer
                        ? $"v{latestVersion} is available!"
                        : "You're up to date.";
                }
                if (openBtn is not null)
                {
                    openBtn.IsVisible = isNewer;
                }
                if (btn is not null)
                {
                    btn.Content = "Check for Updates";
                    btn.IsEnabled = true;
                }
            });
        }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (statusText is not null)
                {
                    statusText.Foreground = new Avalonia.Media.SolidColorBrush(
                        Avalonia.Media.Color.Parse("#B05050"));
                    statusText.Text = "Could not reach GitHub. Check your connection.";
                }
                if (btn is not null)
                {
                    btn.Content = "Check for Updates";
                    btn.IsEnabled = true;
                }
            });
        }
    }

    /// <summary>Returns true when <paramref name="latest"/> is newer than <paramref name="current"/>.</summary>
    private static bool IsNewerVersion(string latest, string current)
    {
        if (Version.TryParse(NormaliseVersion(latest), out var l) &&
            Version.TryParse(NormaliseVersion(current), out var c))
            return l > c;
        return false;
    }

    private static string NormaliseVersion(string v)
    {
        // Ensure at least Major.Minor.Patch so Version.Parse doesn't fail.
        var parts = v.Split('.');
        while (parts.Length < 3)
            parts = [.. parts, "0"];
        return string.Join('.', parts.Take(4));
    }

    // ── Button handlers ──────────────────────────────────────────────────────

    private void OpenReleases_Click(object? sender, RoutedEventArgs e)
        => OpenUrl(_latestReleaseUrl ?? GitHubReleasesPage);

    private void GitHub_Click(object? sender, RoutedEventArgs e)
        => OpenUrl("https://github.com/piyushdoorwar/lumyn-media-player");

    private void Releases_Click(object? sender, RoutedEventArgs e)
        => OpenUrl("https://piyushdoorwar.github.io/lumyn-media-player/releases/");

    private void Issues_Click(object? sender, RoutedEventArgs e)
        => OpenUrl("https://github.com/piyushdoorwar/lumyn-media-player/issues");

    private static void OpenUrl(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                System.Diagnostics.Process.Start("open", url);
            else
                System.Diagnostics.Process.Start("xdg-open", url);
        }
        catch { /* best-effort */ }
    }
}
