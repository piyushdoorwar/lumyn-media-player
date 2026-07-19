using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

[assembly: InternalsVisibleTo("Lumyn.Test")]

namespace Lumyn.Core.Services;

/// <summary>
/// Manages a secondary silent mpv instance that pre-generates video frame thumbnails
/// for seek-bar preview in two phases:
///   Phase 1 – 20 evenly-spaced keyframe captures (available within seconds).
///   Phase 2 – fills to 12 frames/min density in the background while the video plays.
/// All output (video/audio) is suppressed; screenshots use the software path.
/// </summary>
public sealed class ThumbnailExtractor : IDisposable
{
    public const int Phase1Count        = 20;
    public const double Phase2PerMinute = 12.0;

    private readonly record struct ThumbEntry(double Progress, byte[] Jpeg);

    private IntPtr _mpv;
    private readonly string _tempDirectory;
    private readonly string _tempFilePath;
    private bool _disposed;
    private CancellationTokenSource? _cts;
    private Task? _currentTask;

    // Immutable sorted snapshot replaced atomically on every insert.
    // Single background writer; UI thread reads snapshot reference only.
    private volatile ThumbEntry[] _frames = [];

    public ThumbnailExtractor()
    {
        _tempDirectory = Path.Combine(
            Path.GetTempPath(), "Lumyn",
            $"thumb-{Environment.ProcessId}-{Guid.NewGuid():N}");
        _tempFilePath = Path.Combine(_tempDirectory, "frame.jpg");

        try
        {
            Directory.CreateDirectory(_tempDirectory);
            _mpv = MpvNative.mpv_create();
            if (_mpv == IntPtr.Zero) return;

            MpvNative.mpv_set_option_string(_mpv, "terminal",      "no");
            MpvNative.mpv_set_option_string(_mpv, "idle",          "yes");
            MpvNative.mpv_set_option_string(_mpv, "pause",         "yes");
            MpvNative.mpv_set_option_string(_mpv, "vo",            "null");
            MpvNative.mpv_set_option_string(_mpv, "ao",            "null");
            MpvNative.mpv_set_option_string(_mpv, "screenshot-sw", "yes");
            MpvNative.mpv_set_option_string(_mpv, "osd-level",     "0");
            if (MpvNative.mpv_initialize(_mpv) < 0)
            {
                MpvNative.mpv_destroy(_mpv);
                _mpv = IntPtr.Zero;
            }
        }
        catch
        {
            if (_mpv != IntPtr.Zero)
            {
                try { MpvNative.mpv_destroy(_mpv); } catch { }
                _mpv = IntPtr.Zero;
            }
        }
    }

    public bool IsAvailable => _mpv != IntPtr.Zero;

    /// <summary>
    /// Returns the JPEG bytes of the closest captured frame to <paramref name="progress"/> (0–1),
    /// or null if no frames have been captured yet.
    /// </summary>
    public byte[]? GetNearest(double progress)
    {
        var frames = _frames; // atomic snapshot
        if (frames.Length == 0) return null;
        return frames[FindNearest(frames, Math.Clamp(progress, 0.0, 1.0))].Jpeg;
    }

    /// <summary>
    /// Cancels any running generation, resets the frame cache, and starts a two-phase
    /// background generation for <paramref name="filePath"/>.
    /// </summary>
    public void StartGeneration(string filePath, TimeSpan duration)
    {
        if (_mpv == IntPtr.Zero || duration <= TimeSpan.Zero) return;

        var oldCts  = _cts;
        var oldTask = _currentTask;

        oldCts?.Cancel();
        _cts    = new CancellationTokenSource();
        _frames = [];

        var newCts = _cts;
        var durSec = duration.TotalSeconds;

        _currentTask = Task.Run(async () =>
        {
            if (oldTask is not null)
                try { await oldTask.ConfigureAwait(false); } catch { }
            oldCts?.Dispose();
            if (!newCts.IsCancellationRequested)
                DoGenerate(filePath, durSec, newCts.Token);
        });
    }

    /// <summary>Cancels generation and clears all cached frames without disposing the service.</summary>
    public void Cancel()
    {
        _cts?.Cancel();
        _frames = [];
    }

    // ── Generation loop ──────────────────────────────────────────────────────

    private void DoGenerate(string filePath, double durationSec, CancellationToken ct)
    {
        RunCommand("loadfile", filePath, "replace");

        if (!PollUntil(() => ReadDouble("duration") > 0, 5_000, ct))
            return;

        // Phase 1: 20 evenly-spaced frames — fast initial coverage.
        for (var i = 0; i < Phase1Count && !ct.IsCancellationRequested; i++)
            CaptureAt((i + 0.5) / Phase1Count, durationSec);

        if (ct.IsCancellationRequested) return;

        // Phase 2: fill to 12 frames/min, inserting only where a gap exists.
        var targetCount  = Math.Max(Phase1Count,
            (int)Math.Ceiling(Phase2PerMinute * durationSec / 60.0));
        var halfInterval = 0.5 / targetCount;

        for (var i = 0; i < targetCount && !ct.IsCancellationRequested; i++)
        {
            var p = (i + 0.5) / targetCount;
            if (HasFrameNear(_frames, p, halfInterval)) continue;
            CaptureAt(p, durationSec);
            // Yield CPU between Phase 2 captures so playback is not starved.
            // Cancellation wakes this immediately so stopping the video is instant.
            ct.WaitHandle.WaitOne(50);
        }
    }

    private void CaptureAt(double progress, double durationSec)
    {
        var targetStr = (progress * durationSec).ToString("F3", CultureInfo.InvariantCulture);
        RunCommand("seek", targetStr, "absolute", "keyframes");
        TryDeleteTemp();

        if (RunCommand("screenshot-to-file", _tempFilePath, "video") < 0
            || !File.Exists(_tempFilePath))
            return;

        try
        {
            var jpeg       = File.ReadAllBytes(_tempFilePath);
            var actualSec  = ReadDouble("time-pos");
            // Store at actual landed position so GetNearest returns the correct frame
            // for wherever mpv's keyframe seek landed.
            var actualProg = actualSec >= 0
                ? Math.Clamp(actualSec / durationSec, 0.0, 1.0)
                : progress;
            InsertFrame(new ThumbEntry(actualProg, jpeg));
        }
        catch { }
        finally { TryDeleteTemp(); }
    }

    // ── Frame array helpers (single writer, lock-free reads) ─────────────────

    /// <summary>Direct frame insertion for unit tests — bypasses mpv.</summary>
    internal void InsertForTest(double progress, byte[] jpeg)
        => InsertFrame(new ThumbEntry(Math.Clamp(progress, 0.0, 1.0), jpeg));

    private void InsertFrame(ThumbEntry entry)
    {
        // Single background writer — no CAS needed; volatile write ensures UI sees it.
        _frames = InsertSorted(_frames, entry);
    }

    private static ThumbEntry[] InsertSorted(ThumbEntry[] src, ThumbEntry entry)
    {
        // Binary search for insertion point.
        int lo = 0, hi = src.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (src[mid].Progress < entry.Progress) lo = mid + 1;
            else hi = mid;
        }
        var dst = new ThumbEntry[src.Length + 1];
        Array.Copy(src, 0,  dst, 0,      lo);
        dst[lo] = entry;
        Array.Copy(src, lo, dst, lo + 1, src.Length - lo);
        return dst;
    }

    private static int FindNearest(ThumbEntry[] frames, double progress)
    {
        int lo = 0, hi = frames.Length - 1;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (frames[mid].Progress < progress) lo = mid + 1;
            else hi = mid;
        }
        if (lo > 0)
        {
            var dLo = progress - frames[lo - 1].Progress;
            var dHi = frames[lo].Progress - progress;
            if (dLo < dHi) return lo - 1;
        }
        return lo;
    }

    private static bool HasFrameNear(ThumbEntry[] frames, double progress, double tolerance)
    {
        if (frames.Length == 0) return false;
        var idx = FindNearest(frames, progress);
        return Math.Abs(frames[idx].Progress - progress) <= tolerance;
    }

    // ── mpv helpers ──────────────────────────────────────────────────────────

    private bool PollUntil(Func<bool> condition, int timeoutMs, CancellationToken ct)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline && !ct.IsCancellationRequested)
        {
            if (condition()) return true;
            MpvNative.mpv_wait_event(_mpv, 0.05);
        }
        return condition();
    }

    private void TryDeleteTemp()
    {
        try { if (File.Exists(_tempFilePath)) File.Delete(_tempFilePath); }
        catch { }
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

    // ── IDisposable ──────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        try { _currentTask?.GetAwaiter().GetResult(); } catch { }
        _cts?.Dispose();
        if (_mpv != IntPtr.Zero)
        {
            MpvNative.mpv_terminate_destroy(_mpv);
            _mpv = IntPtr.Zero;
        }
        TryDeleteTemp();
        try { if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory); }
        catch { }
    }
}
