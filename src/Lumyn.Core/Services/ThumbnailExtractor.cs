using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Lumyn.Core.Services;

/// <summary>
/// Manages a secondary silent mpv instance that pre-generates video frame thumbnails
/// for seek-bar preview. All output (video/audio) is suppressed; screenshots use the
/// software path so no VO is required.
/// </summary>
public sealed class ThumbnailExtractor : IDisposable
{
    public const int FrameCount = 40;

    private readonly IntPtr _mpv;
    private readonly byte[]?[] _cache = new byte[]?[FrameCount];
    private readonly string _tempFilePath;
    private CancellationTokenSource? _cts;
    private Task? _currentTask;
    private bool _disposed;

    public ThumbnailExtractor()
    {
        _tempFilePath = Path.Combine(
            Path.GetTempPath(),
            $"lumyn_thumb_{Environment.ProcessId}.jpg");

        try
        {
            _mpv = MpvNative.mpv_create();
            if (_mpv == IntPtr.Zero) return;

            MpvNative.mpv_set_option_string(_mpv, "terminal",       "no");
            MpvNative.mpv_set_option_string(_mpv, "idle",           "yes");
            MpvNative.mpv_set_option_string(_mpv, "pause",          "yes");
            MpvNative.mpv_set_option_string(_mpv, "vo",             "null");
            MpvNative.mpv_set_option_string(_mpv, "ao",             "null");
            MpvNative.mpv_set_option_string(_mpv, "screenshot-sw",  "yes");
            MpvNative.mpv_set_option_string(_mpv, "osd-level",      "0");
            MpvNative.mpv_initialize(_mpv);
        }
        catch { /* thumbnails unavailable — fail silently */ }
    }

    public bool IsAvailable => _mpv != IntPtr.Zero;

    /// <summary>Returns cached JPEG bytes for a given frame index, or null if not yet ready.</summary>
    public byte[]? GetAt(int index)
        => (uint)index < (uint)FrameCount ? _cache[index] : null;

    /// <summary>Returns the nearest cached JPEG bytes for a normalized progress value (0–1).</summary>
    public byte[]? GetNearest(double progress)
    {
        var idx = (int)Math.Round(Math.Clamp(progress, 0, 1) * (FrameCount - 1));
        return _cache[idx];
    }

    /// <summary>
    /// Cancels any in-progress generation, clears the cache, then begins background
    /// generation of <see cref="FrameCount"/> evenly-spaced thumbnails.
    /// </summary>
    public void StartGeneration(string filePath, TimeSpan duration)
    {
        if (_mpv == IntPtr.Zero || duration <= TimeSpan.Zero) return;

        var oldCts  = _cts;
        var oldTask = _currentTask;

        oldCts?.Cancel();
        _cts = new CancellationTokenSource();
        Array.Clear(_cache, 0, FrameCount);

        var newCts  = _cts;
        var durSec  = duration.TotalSeconds;

        _currentTask = Task.Run(async () =>
        {
            // Wait for the old generation to fully exit before reusing _mpv.
            if (oldTask is not null)
                try { await oldTask.ConfigureAwait(false); } catch { }

            oldCts?.Dispose();

            if (!newCts.IsCancellationRequested)
                DoGenerate(filePath, durSec, newCts.Token);
        });
    }

    /// <summary>Cancels generation and clears the cache without disposing the service.</summary>
    public void Cancel()
    {
        _cts?.Cancel();
        Array.Clear(_cache, 0, FrameCount);
    }

    // ── Private generation loop ──────────────────────────────────────────────

    private void DoGenerate(string filePath, double durationSec, CancellationToken ct)
    {
        RunCommand("loadfile", filePath, "replace");

        // Pump the mpv event loop until duration is known (file fully loaded).
        if (!PollUntil(() => ReadDouble("duration") > 0, 5_000, ct))
            return;

        for (int i = 0; i < FrameCount; i++)
        {
            if (ct.IsCancellationRequested) break;

            var targetSec = (i + 0.5) / FrameCount * durationSec;
            var targetStr = targetSec.ToString("F3", CultureInfo.InvariantCulture);

            // Seek is synchronous — mpv_command blocks until seek+decode complete.
            RunCommand("seek", targetStr, "absolute", "keyframes");

            // Delete any leftover temp file from the previous iteration.
            TryDeleteTemp();

            // screenshot-to-file is also synchronous: blocks until the file is written.
            if (RunCommand("screenshot-to-file", _tempFilePath, "video") >= 0
                && File.Exists(_tempFilePath))
            {
                try   { _cache[i] = File.ReadAllBytes(_tempFilePath); }
                catch { }
                finally { TryDeleteTemp(); }
            }
        }
    }

    private bool PollUntil(Func<bool> condition, int timeoutMs, CancellationToken ct)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline && !ct.IsCancellationRequested)
        {
            if (condition()) return true;
            // mpv_wait_event drives mpv's internal event loop so properties update.
            MpvNative.mpv_wait_event(_mpv, 0.05);
        }
        return condition();
    }

    private void TryDeleteTemp()
    {
        try { if (File.Exists(_tempFilePath)) File.Delete(_tempFilePath); }
        catch { }
    }

    // ── mpv helpers ──────────────────────────────────────────────────────────

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
        _cts?.Dispose();
        if (_mpv != IntPtr.Zero)
            MpvNative.mpv_terminate_destroy(_mpv);
        TryDeleteTemp();
    }
}
