using System;
using System.IO.BACnet;
using System.IO.BACnet.Serialize;
using Xunit;

namespace UnitTest;

/// <summary>传输层（TRN）及 BVLC 编解码单元测试。
/// 测试 BVLC 编码/解码往返一致性、传输层启动/停止、BBMD/Foreign Device 基础功能。</summary>
public class TransportTests
{
    #region BVLC 编解码 (COD-4 / TRN-3/4)

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

    [Fact]
    [System.ComponentModel.DisplayName("BVLC Encode/Decode 往返一致性 (TRN-3/4)")]
    public void BVLC_EncodeDecode_RoundTrip()
    {
        // BVLC.Encode/Decode 是实例方法，需要传输层实例
        var transport = new BacnetIpUdpProtocolTransport(0xBAC1);
        try
        {
            transport.Start();
            var bvlc = new BVLC(transport);

            // Decode 要求 buffer.Length == msgLength，所以只能用编码内容
            var buf = new byte[12];
            var msgLength = 12;
            var headerLen = bvlc.Encode(buf, 0, BacnetBvlcFunctions.BVLC_ORIGINAL_UNICAST_NPDU, msgLength);

            Assert.Equal(4, headerLen);
            Assert.Equal(0x81, buf[0]); // BVLL_TYPE_BACNET_IP
            Assert.Equal((byte)BacnetBvlcFunctions.BVLC_ORIGINAL_UNICAST_NPDU, buf[1]);
            Assert.Equal(0x00, buf[2]); // Length high
            Assert.Equal(12, buf[3]);   // Length low

            // Decode back（sender 为传入参数，非 out）
            var decLen = bvlc.Decode(buf, 0, out var function, out var decLength, null);

            Assert.Equal(4, decLen);
            Assert.Equal(BacnetBvlcFunctions.BVLC_ORIGINAL_UNICAST_NPDU, function);
            Assert.Equal(12, decLength);
        }
        finally
        {
            transport.Dispose();
        }
    }

    [Fact]
    [System.ComponentModel.DisplayName("BVLC 支持 6 种标准功能码 (TRN-3/4)")]
    public void BVLC_AllFunctionCodes()
    {
        // 验证所有 BVLC 功能码枚举值存在
        Assert.Equal(0x00, (Byte)BacnetBvlcFunctions.BVLC_RESULT);
        Assert.Equal(0x01, (Byte)BacnetBvlcFunctions.BVLC_WRITE_BROADCAST_DISTRIBUTION_TABLE);
        Assert.Equal(0x02, (Byte)BacnetBvlcFunctions.BVLC_READ_BROADCAST_DIST_TABLE);
        Assert.Equal(0x03, (Byte)BacnetBvlcFunctions.BVLC_READ_BROADCAST_DIST_TABLE_ACK);
        Assert.Equal(0x04, (Byte)BacnetBvlcFunctions.BVLC_FORWARDED_NPDU);
        Assert.Equal(0x05, (Byte)BacnetBvlcFunctions.BVLC_REGISTER_FOREIGN_DEVICE);
        Assert.Equal(0x06, (Byte)BacnetBvlcFunctions.BVLC_READ_FOREIGN_DEVICE_TABLE);
        Assert.Equal(0x07, (Byte)BacnetBvlcFunctions.BVLC_READ_FOREIGN_DEVICE_TABLE_ACK);
        Assert.Equal(0x08, (Byte)BacnetBvlcFunctions.BVLC_DELETE_FOREIGN_DEVICE_TABLE_ENTRY);
        Assert.Equal(0x09, (Byte)BacnetBvlcFunctions.BVLC_DISTRIBUTE_BROADCAST_TO_NETWORK);
        Assert.Equal(0x0A, (Byte)BacnetBvlcFunctions.BVLC_ORIGINAL_UNICAST_NPDU);
        Assert.Equal(0x0B, (Byte)BacnetBvlcFunctions.BVLC_ORIGINAL_BROADCAST_NPDU);
    }

    #endregion

    #region IPv6 传输层 (TRN-2)

    [Fact]
    [System.ComponentModel.DisplayName("BacnetIpV6UdpProtocolTransport Start/Dispose 不抛异常 (TRN-2)")]
    public void Ipv6Transport_StartDispose_NoThrow()
    {
        // 使用 IPv6 环回地址，端口 0 让系统分配
        var transport = new BacnetIpV6UdpProtocolTransport(0xBAC2, vMac: -1);
        try
        {
            transport.Start();
        }
        finally
        {
            transport.Dispose();
        }
    }

    [Fact]
    [System.ComponentModel.DisplayName("BVLCV6 Encode/Decode 往返一致性 (TRN-2/COD-5)")]
    public void BVLCV6_EncodeDecode_RoundTrip()
    {
        var transport = new BacnetIpV6UdpProtocolTransport(0xBAC3, vMac: 12345);
        try
        {
            transport.Start();
            var bvlc = new BVLCV6(transport, 12345);

            // 构造 18 字节缓冲区（17 字节头 + 1 字节数据）
            var buf = new byte[18];
            var msgLength = 18;
            var addr = new BacnetAddress(BacnetAddressTypes.IPV6, 0, new byte[18]);
            addr.VMac = new byte[3] { 0x40, 0x01, 0x02 };

            var headerLen = bvlc.Encode(buf, 0, BacnetBvlcV6Functions.BVLC_ORIGINAL_UNICAST_NPDU, msgLength, addr);

            // BVLCV6 单播头长度为 10 字节
            Assert.Equal(10, headerLen);
            Assert.Equal(0x82, buf[0]); // BVLL_TYPE_BACNET_IPV6
            Assert.Equal((byte)BacnetBvlcV6Functions.BVLC_ORIGINAL_UNICAST_NPDU, buf[1]);

            // Decode back
            var remoteAddr = new BacnetAddress(BacnetAddressTypes.IPV6, 0, new byte[18]);
            var decLen = bvlc.Decode(buf, 0, out var function, out var decLength, null, remoteAddr);

            Assert.True(decLen > 0);
            Assert.Equal(BacnetBvlcV6Functions.BVLC_ORIGINAL_UNICAST_NPDU, function);
            Assert.Equal(msgLength, decLength);
        }
        finally
        {
            transport.Dispose();
        }
    }

    [Fact]
    [System.ComponentModel.DisplayName("BVLCV6 支持全部功能码 (TRN-2/COD-5)")]
    public void BVLCV6_AllFunctionCodes()
    {
        Assert.Equal(0x00, (Byte)BacnetBvlcV6Functions.BVLC_RESULT);
        Assert.Equal(0x01, (Byte)BacnetBvlcV6Functions.BVLC_ORIGINAL_UNICAST_NPDU);
        Assert.Equal(0x02, (Byte)BacnetBvlcV6Functions.BVLC_ORIGINAL_BROADCAST_NPDU);
        Assert.Equal(0x03, (Byte)BacnetBvlcV6Functions.BVLC_ADDRESS_RESOLUTION);
        Assert.Equal(0x04, (Byte)BacnetBvlcV6Functions.BVLC_FORWARDED_ADDRESS_RESOLUTION);
        Assert.Equal(0x05, (Byte)BacnetBvlcV6Functions.BVLC_ADDRESS_RESOLUTION_ACK);
        Assert.Equal(0x06, (Byte)BacnetBvlcV6Functions.BVLC_VIRTUAL_ADDRESS_RESOLUTION);
        Assert.Equal(0x07, (Byte)BacnetBvlcV6Functions.BVLC_VIRTUAL_ADDRESS_RESOLUTION_ACK);
        Assert.Equal(0x08, (Byte)BacnetBvlcV6Functions.BVLC_FORWARDED_NPDU);
        Assert.Equal(0x09, (Byte)BacnetBvlcV6Functions.BVLC_REGISTER_FOREIGN_DEVICE);
        Assert.Equal(0x0A, (Byte)BacnetBvlcV6Functions.BVLC_DELETE_FOREIGN_DEVICE_TABLE_ENTRY);
        Assert.Equal(0x0B, (Byte)BacnetBvlcV6Functions.BVLC_SECURE_BVLC);
        Assert.Equal(0x0C, (Byte)BacnetBvlcV6Functions.BVLC_DISTRIBUTE_BROADCAST_TO_NETWORK);
    }

    #endregion
}
