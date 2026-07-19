using System.Runtime.InteropServices;
using Lumyn.Core.Services;

namespace Lumyn.Test;

public sealed class MpvInteropLayoutTests
{
    [Fact]
    public void NodeTypesMatchNative64BitLayout()
    {
        if (IntPtr.Size != 8)
            return;

        Assert.Equal(16, Marshal.SizeOf<MpvNative.MpvNode>());
        Assert.Equal(24, Marshal.SizeOf<MpvNative.MpvNodeList>());
        Assert.Equal(0, Marshal.OffsetOf<MpvNative.MpvNode>(nameof(MpvNative.MpvNode.value)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<MpvNative.MpvNode>(nameof(MpvNative.MpvNode.format)).ToInt32());
    }

    [Fact]
    public void NodeFormatsMatchLibmpvApiValues()
    {
        Assert.Equal(6, (int)MpvFormat.Node);
        Assert.Equal(7, (int)MpvFormat.NodeArray);
        Assert.Equal(8, (int)MpvFormat.NodeMap);
    }
}
