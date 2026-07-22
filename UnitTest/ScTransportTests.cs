using System;
using System.IO.BACnet;
using System.IO.BACnet.Serialize;
using Xunit;

namespace UnitTest;

/// <summary>BACnet/SC 安全通信（SEC）单元测试。
/// 测试 BVLC/SC 帧编解码、WebSocket 传输层基础功能。</summary>
public class ScTransportTests
{
    #region BacnetBvlcScFunctions 枚举 (SEC 基础)

    [Fact]
    [System.ComponentModel.DisplayName("SC 功能码枚举值正确")]
    public void ScFunctions_EnumValues()
    {
        Assert.Equal(0x00, (Byte)BacnetBvlcScFunctions.BVLC_SC_ANNOUNCE_HUB_FUNCTION);
        Assert.Equal(0x01, (Byte)BacnetBvlcScFunctions.BVLC_SC_HUB_CONNECT);
        Assert.Equal(0x02, (Byte)BacnetBvlcScFunctions.BVLC_SC_HUB_DISCONNECT);
        Assert.Equal(0x03, (Byte)BacnetBvlcScFunctions.BVLC_SC_ROUTING_TABLE_ADVERTISEMENT);
        Assert.Equal(0x04, (Byte)BacnetBvlcScFunctions.BVLC_SC_HUB_FUNCTION);
        Assert.Equal(0x05, (Byte)BacnetBvlcScFunctions.BVLC_SC_PEER_TO_PEER_FUNCTION);
    }

    #endregion

    #region BVLCSC 编解码 (SEC-1)

    [Fact]
    [System.ComponentModel.DisplayName("BVLCSC Encode 正确设置 BVLL 头")]
    public void BVLCSC_Encode_Header()
    {
        var buffer = new Byte[20];
        var msgLength = 20;
        var headerLen = BVLCSC.Encode(buffer, BacnetBvlcScFunctions.BVLC_SC_HUB_FUNCTION, msgLength);

        Assert.Equal(4, headerLen);
        Assert.Equal(0x83, buffer[0]); // BVLL_TYPE_BACNET_SC
        Assert.Equal((Byte)BacnetBvlcScFunctions.BVLC_SC_HUB_FUNCTION, buffer[1]);
        Assert.Equal(0x00, buffer[2]); // Length high byte
        Assert.Equal(20, buffer[3]);   // Length low byte
    }

    [Fact]
    [System.ComponentModel.DisplayName("BVLCSC Decode 正确解析 BVLL 头")]
    public void BVLCSC_Decode_Header()
    {
        // 帧长度声明 16，实际缓冲区至少 16 字节
        var buffer = new Byte[16];
        buffer[0] = 0x83;
        buffer[1] = 0x04;
        buffer[2] = 0x00;
        buffer[3] = 0x10; // length = 16
        var headerLen = BVLCSC.Decode(buffer, 0, out var function, out var msgLength);

        Assert.Equal(4, headerLen);
        Assert.Equal(BacnetBvlcScFunctions.BVLC_SC_HUB_FUNCTION, function);
        Assert.Equal(16, msgLength);
    }

    [Fact]
    [System.ComponentModel.DisplayName("BVLCSC Decode 非 SC 类型返回 -1")]
    public void BVLCSC_Decode_InvalidType()
    {
        var buffer = new Byte[] { 0x81, 0x04, 0x00, 0x04 }; // IPv4 BVLL 类型
        var headerLen = BVLCSC.Decode(buffer, 0, out var function, out var msgLength);

        Assert.Equal(-1, headerLen);
    }

    [Fact]
    [System.ComponentModel.DisplayName("BVLCSC Decode 过短缓冲区返回 -1")]
    public void BVLCSC_Decode_ShortBuffer()
    {
        var buffer = new Byte[] { 0x83, 0x04 }; // 只有 2 字节
        var headerLen = BVLCSC.Decode(buffer, 0, out var function, out var msgLength);

        Assert.Equal(-1, headerLen);
    }

    [Fact]
    [System.ComponentModel.DisplayName("BVLCSC CreateHubFunctionFrame 正确构建帧")]
    public void BVLCSC_CreateHubFunctionFrame()
    {
        var npdu = new Byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var frame = BVLCSC.CreateHubFunctionFrame(npdu, npdu.Length);

        Assert.Equal(4 + 5, frame.Length);
        Assert.Equal(0x83, frame[0]); // BVLL type
        Assert.Equal((Byte)BacnetBvlcScFunctions.BVLC_SC_HUB_FUNCTION, frame[1]); // function
        Assert.Equal(0x00, frame[2]); // length high
        Assert.Equal(9, frame[3]);    // length low (4 header + 5 payload)
        // Check payload copied correctly
        for (var i = 0; i < npdu.Length; i++)
            Assert.Equal(npdu[i], frame[4 + i]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("BVLCSC CreatePeerToPeerFunctionFrame 正确构建帧")]
    public void BVLCSC_CreatePeerToPeerFrame()
    {
        var npdu = new Byte[] { 0x0A, 0x0B, 0x0C };
        var frame = BVLCSC.CreatePeerToPeerFunctionFrame(npdu, npdu.Length);

        Assert.Equal(4 + 3, frame.Length);
        Assert.Equal(0x83, frame[0]);
        Assert.Equal((Byte)BacnetBvlcScFunctions.BVLC_SC_PEER_TO_PEER_FUNCTION, frame[1]);
        Assert.Equal(7, (frame[2] << 8) | frame[3]);
        // Check payload copied correctly
        for (var i = 0; i < npdu.Length; i++)
            Assert.Equal(npdu[i], frame[4 + i]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("BVLCSC CreateHubConnectFrame 正确构建连接帧")]
    public void BVLCSC_CreateHubConnectFrame()
    {
        var uri = "wss://hub.example.com/bacnet";
        var frame = BVLCSC.CreateHubConnectFrame(uri);

        var uriBytes = System.Text.Encoding.UTF8.GetBytes(uri);
        Assert.Equal(4 + uriBytes.Length, frame.Length);
        Assert.Equal(0x83, frame[0]);
        Assert.Equal((Byte)BacnetBvlcScFunctions.BVLC_SC_HUB_CONNECT, frame[1]);
        // Verify URI payload
        var decodedUri = System.Text.Encoding.UTF8.GetString(frame, 4, frame.Length - 4);
        Assert.Equal(uri, decodedUri);
    }

    [Fact]
    [System.ComponentModel.DisplayName("BVLCSC CreateHubDisconnectFrame 正确构建断开帧")]
    public void BVLCSC_CreateHubDisconnectFrame()
    {
        var frame = BVLCSC.CreateHubDisconnectFrame();

        Assert.Equal(4, frame.Length);
        Assert.Equal(0x83, frame[0]);
        Assert.Equal((Byte)BacnetBvlcScFunctions.BVLC_SC_HUB_DISCONNECT, frame[1]);
        Assert.Equal(4, (frame[2] << 8) | frame[3]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("BVLCSC ExtractPayload 正确提取负载")]
    public void BVLCSC_ExtractPayload()
    {
        var frame = new Byte[] { 0x83, 0x04, 0x00, 0x09, 0x01, 0x02, 0x03, 0x04, 0x05 };
        var payload = BVLCSC.ExtractPayload(frame, 4, out var payloadLength);

        Assert.Equal(5, payloadLength);
        Assert.Equal(5, payload.Length);
        Assert.Equal(new Byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 }, payload);
    }

    [Fact]
    [System.ComponentModel.DisplayName("BVLCSC Encode/Decode 往返一致性")]
    public void BVLCSC_Encode_Decode_RoundTrip()
    {
        // Encode
        var buffer = new Byte[50];
        var originalFunc = BacnetBvlcScFunctions.BVLC_SC_ANNOUNCE_HUB_FUNCTION;
        var originalLength = 50;
        BVLCSC.Encode(buffer, originalFunc, originalLength);

        // Decode
        var headerLen = BVLCSC.Decode(buffer, 0, out var decodedFunc, out var decodedLength);

        Assert.Equal(4, headerLen);
        Assert.Equal(originalFunc, decodedFunc);
        Assert.Equal(originalLength, decodedLength);
    }

    #endregion

    #region BacnetScTransport 基础功能 (SEC-1)

    [Fact]
    [System.ComponentModel.DisplayName("BacnetScTransport 构造正确设置属性")]
    public void ScTransport_Constructor()
    {
        var transport = new BacnetScTransport("wss://hub.example.com:47810/bacnet", false);

        Assert.Equal("wss://hub.example.com:47810/bacnet", transport.Uri.ToString());
        Assert.False(transport.IsHub);
        Assert.Equal(BacnetAddressTypes.IP, transport.Type);
        Assert.Equal(4, transport.HeaderLength);
        Assert.Equal(BacnetMaxAdpu.MAX_APDU1476, transport.MaxAdpuLength);
    }

    [Fact]
    [System.ComponentModel.DisplayName("BacnetScTransport 广播地址返回 net=0xFFFF")]
    public void ScTransport_BroadcastAddress()
    {
        var transport = new BacnetScTransport("wss://hub.example.com:47810/bacnet", false);
        var broadcast = transport.GetBroadcastAddress();

        Assert.Equal(BacnetAddressTypes.IP, broadcast.type);
        Assert.Equal(0xFFFF, broadcast.net);
        Assert.NotNull(broadcast.adr);
    }

    [Fact]
    [System.ComponentModel.DisplayName("ScTransportState 枚举值正确")]
    public void ScTransportState_EnumValues()
    {
        Assert.Equal(0, (Int32)BacnetScTransport.ScTransportState.Disconnected);
        Assert.Equal(1, (Int32)BacnetScTransport.ScTransportState.Connecting);
        Assert.Equal(2, (Int32)BacnetScTransport.ScTransportState.Connected);
        Assert.Equal(3, (Int32)BacnetScTransport.ScTransportState.Faulted);
    }

    [Fact]
    [System.ComponentModel.DisplayName("BacnetScTransport 初始状态为 Disconnected")]
    public void ScTransport_InitialState()
    {
        var transport = new BacnetScTransport("wss://hub.example.com/bacnet", false);
        Assert.Equal(BacnetScTransport.ScTransportState.Disconnected, transport.State);
        Assert.False(transport.IsConnected);
    }

    [Fact]
    [System.ComponentModel.DisplayName("BacnetScTransport ToString 返回 URI")]
    public void ScTransport_ToString()
    {
        var transport = new BacnetScTransport("wss://hub.example.com:47810/bacnet", false);
        var str = transport.ToString();

        Assert.Contains("SC:", str);
        Assert.Contains("wss://hub.example.com:47810/bacnet", str);
    }

    #endregion

    #region BVLCSC 边界条件

    [Fact]
    [System.ComponentModel.DisplayName("BVLCSC CreateHubFunctionFrame 空负载")]
    public void BVLCSC_EmptyPayload()
    {
        var frame = BVLCSC.CreateHubFunctionFrame([], 0);

        Assert.Equal(4, frame.Length);
        Assert.Equal(0x83, frame[0]);
        Assert.Equal(4, (frame[2] << 8) | frame[3]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("BVLCSC ExtractPayload 空帧返回空数组")]
    public void BVLCSC_ExtractPayload_Empty()
    {
        var frame = new Byte[] { 0x83, 0x04, 0x00, 0x04 };
        var payload = BVLCSC.ExtractPayload(frame, 4, out var payloadLength);

        Assert.Equal(0, payloadLength);
        Assert.Empty(payload);
    }

    [Fact]
    [System.ComponentModel.DisplayName("BVLCSC 大负载编解码")]
    public void BVLCSC_LargePayload()
    {
        var npdu = new Byte[4096];
        var rng = new Random(42);
        rng.NextBytes(npdu);

        var frame = BVLCSC.CreateHubFunctionFrame(npdu, npdu.Length);
        Assert.Equal(4 + 4096, frame.Length);

        var headerLen = BVLCSC.Decode(frame, 0, out var function, out var msgLength);
        Assert.Equal(4, headerLen);
        Assert.Equal(BacnetBvlcScFunctions.BVLC_SC_HUB_FUNCTION, function);
        Assert.Equal(4 + 4096, msgLength);

        var payload = BVLCSC.ExtractPayload(frame, headerLen, out var pl);
        Assert.Equal(4096, pl);
        Assert.Equal(npdu, payload);
    }

    #endregion
}
