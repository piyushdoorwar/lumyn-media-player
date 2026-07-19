using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Lumyn.Core.Services;

/// <summary>
/// Prevents the OS from dimming the screen, locking, or sleeping while media is playing.
///
/// - Windows : SetThreadExecutionState (kernel32)
/// - Linux   : org.freedesktop.ScreenSaver.Inhibit via dbus-send (D-Bus session bus)
/// </summary>
public sealed partial class ScreenInhibitor : IDisposable
{
    private bool _inhibited;

    // ── Linux ─────────────────────────────────────────────────────────────────
    private uint _linuxCookie;

    // ── Windows P/Invoke ──────────────────────────────────────────────────────
    [LibraryImport("kernel32.dll")]
    private static partial uint SetThreadExecutionState(uint esFlags);

    private const uint ES_CONTINUOUS       = 0x80000000u;
    private const uint ES_SYSTEM_REQUIRED  = 0x00000001u;
    private const uint ES_DISPLAY_REQUIRED = 0x00000002u;

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Start inhibiting screen sleep/lock. Safe to call repeatedly — no-ops if already active.</summary>
    public void Inhibit()
    {
        if (_inhibited) return;
        _inhibited = true;

        try
        {
            if      (OperatingSystem.IsWindows()) InhibitWindows();
            else if (OperatingSystem.IsLinux())   InhibitLinux();
        }
        catch { /* best-effort — never crash the player */ }
    }

    /// <summary>Release the inhibition. Safe to call when not inhibited.</summary>
    public void Uninhibit()
    {
        if (!_inhibited) return;
        _inhibited = false;

        try
        {
            if      (OperatingSystem.IsWindows()) UninhibitWindows();
            else if (OperatingSystem.IsLinux())   UninhibitLinux();
        }
        catch { /* best-effort */ }
    }

    public void Dispose() => Uninhibit();

    // ── Windows ──────────────────────────────────────────────────────────────

    private static void InhibitWindows()
        => SetThreadExecutionState(ES_CONTINUOUS | ES_DISPLAY_REQUIRED | ES_SYSTEM_REQUIRED);

    private static void UninhibitWindows()
        => SetThreadExecutionState(ES_CONTINUOUS);

    // ── Linux (org.freedesktop.ScreenSaver via dbus-send) ────────────────────
    // dbus-send uses type:value tokens (no spaces), avoiding argv-splitting issues.

    private void InhibitLinux()
    {
        // dbus-send --session --print-reply \
        //   --dest=org.freedesktop.ScreenSaver /org/freedesktop/ScreenSaver \
        //   org.freedesktop.ScreenSaver.Inhibit string:Lumyn string:"Playing media"
        // Output: ... uint32 <cookie>
        var output = RunProcess("dbus-send",
        [
            "--session", "--print-reply",
            "--dest=org.freedesktop.ScreenSaver",
            "/org/freedesktop/ScreenSaver",
            "org.freedesktop.ScreenSaver.Inhibit",
            "string:Lumyn",
            "string:Playing media"
        ]);

        if (output is null) return;

        var match = Regex.Match(output, @"uint32\s+(\d+)");
        if (match.Success && uint.TryParse(match.Groups[1].Value, out var cookie))
            _linuxCookie = cookie;
    }

    private void UninhibitLinux()
    {
        if (_linuxCookie == 0) return;

        RunProcess("dbus-send",
        [
            "--session",
            "--dest=org.freedesktop.ScreenSaver",
            "/org/freedesktop/ScreenSaver",
            "org.freedesktop.ScreenSaver.UnInhibit",
            $"uint32:{_linuxCookie}"   // single token, no spaces
        ]);

        _linuxCookie = 0;
    }

    private static string? RunProcess(string exe, string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            using var proc = Process.Start(psi);
            return proc?.StandardOutput.ReadToEnd();
        }
        catch { return null; }
    }
}
