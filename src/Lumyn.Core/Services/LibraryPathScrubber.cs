namespace Lumyn.Core.Services;

/// <summary>
/// The Linux .deb launcher prepends Lumyn's bundled <c>lib/</c> directory to
/// LD_LIBRARY_PATH so the app finds its own libmpv. Child processes inherit it
/// and then load those older libraries in place of the system ones: system
/// ffmpeg fails with missing symbols and dbus-send rejects the bundled libdbus.
/// glibc reads LD_LIBRARY_PATH once at process start, so removing the entry
/// afterwards leaves Lumyn's own library loading untouched.
/// </summary>
public static class LibraryPathScrubber
{
    private const string Variable = "LD_LIBRARY_PATH";

    public static void RemoveBundledLibDirectory()
    {
        if (!OperatingSystem.IsLinux()) return;

        var bundled = Path.Combine(AppContext.BaseDirectory, "lib");
        var current = Environment.GetEnvironmentVariable(Variable);
        var cleaned = Without(current, bundled);
        if (cleaned != current)
            Environment.SetEnvironmentVariable(Variable, cleaned);
    }

    /// <summary>Returns <paramref name="path"/> minus every entry equal to <paramref name="directory"/>, or null when nothing is left.</summary>
    internal static string? Without(string? path, string directory)
    {
        if (string.IsNullOrEmpty(path)) return path;

        var target = Normalize(directory);
        var kept = path
            .Split(':')
            .Where(entry => entry.Length > 0 && Normalize(entry) != target)
            .ToArray();

        if (kept.Length == path.Split(':').Count(e => e.Length > 0)) return path;
        return kept.Length == 0 ? null : string.Join(':', kept);
    }

    private static string Normalize(string dir) => dir.TrimEnd('/');
}
