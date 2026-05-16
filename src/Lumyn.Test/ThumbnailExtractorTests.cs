using Lumyn.Core.Services;

namespace Lumyn.Test;

/// <summary>
/// Tests for ThumbnailExtractor's in-memory logic: sorted frame array,
/// nearest-frame binary search, phase constants, and cache lifecycle.
/// No libmpv required — arrays are initialised before the libmpv try-block.
/// </summary>
public sealed class ThumbnailExtractorTests : IDisposable
{
    private readonly ThumbnailExtractor _ext = new();

    // ── Phase constants ──────────────────────────────────────────────────────

    [Fact]
    public void Phase1Count_IsPositive()
        => Assert.True(ThumbnailExtractor.Phase1Count > 0);

    [Fact]
    public void Phase2PerMinute_IsPositive()
        => Assert.True(ThumbnailExtractor.Phase2PerMinute > 0);

    // ── GetNearest before any frames ─────────────────────────────────────────

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void GetNearest_BeforeGeneration_ReturnsNull(double p)
        => Assert.Null(_ext.GetNearest(p));

    [Fact]
    public void GetNearest_ClampsBelowZero_SameAsZero()
        => Assert.Equal(_ext.GetNearest(0.0), _ext.GetNearest(-1.0));

    [Fact]
    public void GetNearest_ClampsAboveOne_SameAsOne()
        => Assert.Equal(_ext.GetNearest(1.0), _ext.GetNearest(5.0));

    // ── Cancel ───────────────────────────────────────────────────────────────

    [Fact]
    public void Cancel_EmptiesFrameCache()
    {
        _ext.InsertForTest(0.5, [1]);
        _ext.Cancel();
        Assert.Null(_ext.GetNearest(0.5));
    }

    // ── Binary search correctness via InsertForTest ──────────────────────────

    [Fact]
    public void GetNearest_SingleFrame_AlwaysReturnsThatFrame()
    {
        var only = new byte[] { 42 };
        _ext.InsertForTest(0.5, only);

        Assert.Same(only, _ext.GetNearest(0.0));
        Assert.Same(only, _ext.GetNearest(0.5));
        Assert.Same(only, _ext.GetNearest(1.0));
    }

    [Fact]
    public void GetNearest_ExactProgressHit_ReturnsThatFrame()
    {
        var a = new byte[] { 1 };
        var b = new byte[] { 2 };
        var c = new byte[] { 3 };
        _ext.InsertForTest(0.1, a);
        _ext.InsertForTest(0.5, b);
        _ext.InsertForTest(0.9, c);

        Assert.Same(a, _ext.GetNearest(0.1));
        Assert.Same(b, _ext.GetNearest(0.5));
        Assert.Same(c, _ext.GetNearest(0.9));
    }

    [Fact]
    public void GetNearest_ReturnsClosestFrame()
    {
        var lo  = new byte[] { 1 };
        var hi  = new byte[] { 2 };
        _ext.InsertForTest(0.2, lo);
        _ext.InsertForTest(0.8, hi);

        // 0.49 is closer to 0.2 than 0.8 (diff 0.29 vs 0.31)
        Assert.Same(lo, _ext.GetNearest(0.49));
        // 0.51 is closer to 0.8
        Assert.Same(hi, _ext.GetNearest(0.51));
    }

    [Fact]
    public void GetNearest_FramesInsertedOutOfOrder_StillSorted()
    {
        var hi  = new byte[] { 3 };
        var lo  = new byte[] { 1 };
        var mid = new byte[] { 2 };

        // Insert in reverse order — array must self-sort.
        _ext.InsertForTest(0.9, hi);
        _ext.InsertForTest(0.1, lo);
        _ext.InsertForTest(0.5, mid);

        Assert.Same(lo,  _ext.GetNearest(0.0));
        Assert.Same(mid, _ext.GetNearest(0.5));
        Assert.Same(hi,  _ext.GetNearest(1.0));
    }

    [Fact]
    public void GetNearest_Phase2UpgradeReplacesFrame()
    {
        // Simulate Phase 2 inserting a frame closer to the target than Phase 1 had.
        var phase1Frame = new byte[] { 1 };
        var phase2Frame = new byte[] { 2 };

        _ext.InsertForTest(0.5, phase1Frame); // coarse Phase 1 frame at 0.5
        Assert.Same(phase1Frame, _ext.GetNearest(0.55));

        _ext.InsertForTest(0.54, phase2Frame); // Phase 2 adds closer frame
        // 0.55 is now closer to 0.54 (diff 0.01) than 0.5 (diff 0.05)
        Assert.Same(phase2Frame, _ext.GetNearest(0.55));
    }

    public void Dispose() => _ext.Dispose();
}
