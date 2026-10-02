using System.Runtime.InteropServices;
using System.Text;

namespace Lumyn.Core.Services;

/// <summary>Duration / title / artist read from a media file without playing it.</summary>
public readonly record struct QueueProbeResult(TimeSpan? Duration, string? Title, string? Artist);

/// <summary>
/// A secondary silent mpv instance used to read lightweight metadata (duration,
/// title, artist) for queued files so the queue can show them YouTube-Music style.
/// Modeled on <see cref="ThumbnailExtractor"/>: no video/audio output, paused.
/// Calls are synchronous and meant to run on a background thread; one file at a time.
/// </summary>
public sealed class QueueMetadataProbe : IDisposable
{
    private readonly IntPtr _mpv;
    private bool _disposed;

    public QueueMetadataProbe()
    {
        try
        {
            _mpv = MpvNative.mpv_create();
            if (_mpv == IntPtr.Zero) return;

            MpvNative.mpv_set_option_string(_mpv, "terminal", "no");
            MpvNative.mpv_set_option_string(_mpv, "idle",     "yes");
            MpvNative.mpv_set_option_string(_mpv, "pause",    "yes");
            MpvNative.mpv_set_option_string(_mpv, "vo",       "null");
            MpvNative.mpv_set_option_string(_mpv, "ao",       "null");
            MpvNative.mpv_set_option_string(_mpv, "osd-level", "0");
            MpvNative.mpv_initialize(_mpv);
        }
        catch { /* metadata probing unavailable — fail silently */ }
    }

    public bool IsAvailable => _mpv != IntPtr.Zero;

    /// <summary>
    /// Loads <paramref name="filePath"/> just long enough to read its duration and
    /// title/artist tags. Returns an empty result if mpv is unavailable, the file
    /// can't be read, or the operation is cancelled.
    /// </summary>
    public QueueProbeResult Probe(string filePath, CancellationToken ct)
    {
        if (_mpv == IntPtr.Zero || ct.IsCancellationRequested) return default;

        // stop/loadfile schedule work on mpv's playback thread. A positive
        // duration can still belong to the previous file, so it is not a
        // readiness signal. Start idle and discard events from earlier probes.
        if (RunCommand("stop") < 0 ||
            !PollUntil(() => GetString("idle-active") == "yes", 5_000, ct))
            return default;
        while (ReadEvent(0) != MpvEventId.None)
            if (ct.IsCancellationRequested) return default;

        try
        {
            if (ct.IsCancellationRequested || RunCommand("loadfile", filePath, "replace") < 0 ||
                !WaitForFileLoaded(ct))
                return default;

            var durSec = ReadDouble("duration");
            TimeSpan? duration = double.IsFinite(durSec) && durSec > 0
                ? TimeSpan.FromSeconds(durSec) : null;

            var title  = NullIfBlank(GetString("media-title"));
            var artist = NullIfBlank(GetString("metadata/by-key/Artist"))
                         ?? NullIfBlank(GetString("metadata/by-key/artist"))
                         ?? NullIfBlank(GetString("metadata/by-key/album_artist"));

            return ct.IsCancellationRequested ? default : new QueueProbeResult(duration, title, artist);
        }
        finally
        {
            // Also unload after cancellation, a failed load, or a timeout.
            RunCommand("stop");
        }
    }

    private bool WaitForFileLoaded(CancellationToken ct)
    {
        var deadline = Environment.TickCount64 + 5_000;
        var started = false;
        while (Environment.TickCount64 < deadline && !ct.IsCancellationRequested)
        {
            switch (ReadEvent(0.05))
            {
                case MpvEventId.StartFile:
                    started = true;
                    break;
                case MpvEventId.FileLoaded when started:
                    return true;
                // A previous stop's EndFile can arrive after idle becomes true.
                case MpvEventId.EndFile when started:
                case MpvEventId.Shutdown:
                    return false;
            }
        }
        return false;
    }

    private MpvEventId ReadEvent(double timeout) =>
        Marshal.PtrToStructure<MpvNative.MpvEvent>(MpvNative.mpv_wait_event(_mpv, timeout)).event_id;

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ── mpv helpers (same shapes as ThumbnailExtractor) ──────────────────────

    private bool PollUntil(Func<bool> condition, int timeoutMs, CancellationToken ct)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline && !ct.IsCancellationRequested)
        {
            if (condition()) return true;
            MpvNative.mpv_wait_event(_mpv, 0.05);
        }
        return !ct.IsCancellationRequested && condition();
    }

    private double ReadDouble(string name)
    {
        if (_mpv == IntPtr.Zero) return double.NaN;
        double val = double.NaN;
        unsafe
        {
            MpvNative.mpv_get_property(_mpv, name, MpvFormat.Double, (IntPtr)(&val));
        }
        return val;
    }

    private string? GetString(string name)
    {
        if (_mpv == IntPtr.Zero) return null;
        var ptr = MpvNative.mpv_get_property_string(_mpv, name);
        if (ptr == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUTF8(ptr); }
        finally { MpvNative.mpv_free(ptr); }
    }

    private int RunCommand(params string[] args)
    {
        if (_mpv == IntPtr.Zero) return -1;
        var ptrs = new IntPtr[args.Length + 1];
        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                var bytes = Encoding.UTF8.GetBytes(args[i]);
                ptrs[i] = Marshal.AllocHGlobal(bytes.Length + 1);
                Marshal.Copy(bytes, 0, ptrs[i], bytes.Length);
                Marshal.WriteByte(ptrs[i], bytes.Length, 0);
            }
            ptrs[^1] = IntPtr.Zero;
            unsafe
            {
                fixed (IntPtr* p = ptrs)
                    return MpvNative.mpv_command(_mpv, (IntPtr)p);
            }
        }
        finally
        {
            foreach (var ptr in ptrs)
                if (ptr != IntPtr.Zero)
                    Marshal.FreeHGlobal(ptr);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_mpv != IntPtr.Zero)
            MpvNative.mpv_terminate_destroy(_mpv);
    }
}
