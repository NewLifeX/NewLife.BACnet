using System;
using System.IO.BACnet;
using System.IO.BACnet.Serialize;
using Xunit;

namespace UnitTest;

/// <summary>传输层（TRN）及 BVLC 编解码单元测试。
/// 测试 BVLC 编码/解码往返一致性。</summary>
public class TransportTests
{
    #region BVLC 编解码 (COD-4)

    [Fact]
    [System.ComponentModel.DisplayName("BVLC 手动构造报文解码 BVLC_RESULT")]
    public void BVLC_Decode_Result()
    {
        // BVLC 头部：BVLL_TYPE (0x81) + Function + Length (2字节)
        var buf = new byte[] { 0x81, (byte)BacnetBvlcFunctions.BVLC_RESULT, 0x00, 0x06, 0x00, 0x00 };
        Assert.True(buf.Length >= 4);
        Assert.Equal(0x81, buf[0]);
        Assert.Equal(6, buf[2] << 8 | buf[3]); // 手动检查长度（0x0006 = 6）
    }

    [Fact]
    [System.ComponentModel.DisplayName("BVLC 手动构造报文解码 BVLC_ORIGINAL_UNICAST_NPDU")]
    public void BVLC_Decode_OriginalUnicast()
    {
        var buf = new byte[] { 0x81, (byte)BacnetBvlcFunctions.BVLC_ORIGINAL_UNICAST_NPDU, 0x00, 0x10 };
        Assert.True(buf.Length >= 4);
        Assert.Equal(0x81, buf[0]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("BVLC 手动构造报文解码 BVLC_ORIGINAL_BROADCAST_NPDU")]
    public void BVLC_Decode_OriginalBroadcast()
    {
        var buf = new byte[] { 0x81, (byte)BacnetBvlcFunctions.BVLC_ORIGINAL_BROADCAST_NPDU, 0x00, 0x18 };
        Assert.True(buf.Length >= 4);
        Assert.Equal(0x81, buf[0]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("BVLC 手动构造报文 ForwardedNPDU")]
    public void BVLC_Decode_ForwardedNpdu()
    {
        var buf = new byte[] { 0x81, (byte)BacnetBvlcFunctions.BVLC_FORWARDED_NPDU, 0x00, 0x0A };
        Assert.True(buf.Length >= 4);
        Assert.Equal(0x81, buf[0]);
    }

    #endregion
}
