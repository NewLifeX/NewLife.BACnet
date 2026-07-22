using NewLife.Log;

namespace System.IO.BACnet.Serialize;

/// <summary>BACnet/SC BVLC 帧编解码。
/// BACnet/SC 使用独立的 BVLL 类型 0x83，通过 WebSocket 传输。</summary>
/// <remarks>
/// BACnet/SC 帧结构：
/// ```
/// +--------+--------+--------+--------+-------------------+
/// |  BVLL  |  SC    |  Length (2)    |  Payload          |
/// |  Type  |  Fn    |  big-endian    |  (variable)       |
/// |  0x83  |  1byte |                |                   |
/// +--------+--------+--------+--------+-------------------+
/// ```
/// Hub Function / Peer-to-Peer Function 的 Payload 包含 NPDU + APDU。
/// </remarks>
public class BVLCSC
{
    /// <summary>BACnet/SC BVLL 类型标识</summary>
    public const Byte BVLL_TYPE_BACNET_SC = 0x83;

    /// <summary>最小 BVLC 头长度（Type + Function + Length2）</summary>
    public const Byte BVLC_HEADER_LENGTH = 4;

    /// <summary>SC 最大 APDU 长度（WebSocket 无 1472 限制，保守使用 1476）</summary>
    public const BacnetMaxAdpu BVLC_MAX_APDU = BacnetMaxAdpu.MAX_APDU1476;

    /// <summary>日志</summary>
    public ILog Log { get; set; } = XTrace.Log;

    /// <summary>编码 BVLC 头到缓冲区</summary>
    /// <param name="buffer">输出缓冲区</param>
    /// <param name="function">SC 功能码</param>
    /// <param name="msgLength">整帧长度（含头部）</param>
    /// <returns>头部长度（始终 4）</returns>
    public static Int32 Encode(Byte[] buffer, BacnetBvlcScFunctions function, Int32 msgLength)
    {
        buffer[0] = BVLL_TYPE_BACNET_SC;
        buffer[1] = (Byte)function;
        buffer[2] = (Byte)((msgLength & 0xFF00) >> 8);
        buffer[3] = (Byte)((msgLength & 0x00FF) >> 0);
        return BVLC_HEADER_LENGTH;
    }

    /// <summary>解码 BVLC 头</summary>
    /// <param name="buffer">输入缓冲区</param>
    /// <param name="offset">起始偏移</param>
    /// <param name="function">输出：SC 功能码</param>
    /// <param name="msgLength">输出：整帧长度</param>
    /// <returns>头部长度（4），无效返回 -1</returns>
    public static Int32 Decode(Byte[] buffer, Int32 offset, out BacnetBvlcScFunctions function, out Int32 msgLength)
    {
        function = BacnetBvlcScFunctions.BVLC_SC_ANNOUNCE_HUB_FUNCTION;
        msgLength = 0;

        if (buffer.Length - offset < BVLC_HEADER_LENGTH)
            return -1;

        if (buffer[offset] != BVLL_TYPE_BACNET_SC)
            return -1;

        function = (BacnetBvlcScFunctions)buffer[offset + 1];
        msgLength = (buffer[offset + 2] << 8) | (buffer[offset + 3] << 0);

        if (buffer.Length - offset < msgLength)
            return -1;

        return BVLC_HEADER_LENGTH;
    }

    /// <summary>创建 SC Hub Function 帧（数据经过 Hub 转发）</summary>
    /// <param name="npduData">NPDU + APDU 数据</param>
    /// <param name="npduLength">数据长度</param>
    /// <returns>完整的 SC 帧（含 BVLC 头）</returns>
    public static Byte[] CreateHubFunctionFrame(Byte[] npduData, Int32 npduLength)
    {
        var totalLength = BVLC_HEADER_LENGTH + npduLength;
        var frame = new Byte[totalLength];
        Encode(frame, BacnetBvlcScFunctions.BVLC_SC_HUB_FUNCTION, totalLength);
        Array.Copy(npduData, 0, frame, BVLC_HEADER_LENGTH, npduLength);
        return frame;
    }

    /// <summary>创建 SC Peer-to-Peer Function 帧（直连）</summary>
    /// <param name="npduData">NPDU + APDU 数据</param>
    /// <param name="npduLength">数据长度</param>
    /// <returns>完整的 SC 帧（含 BVLC 头）</returns>
    public static Byte[] CreatePeerToPeerFunctionFrame(Byte[] npduData, Int32 npduLength)
    {
        var totalLength = BVLC_HEADER_LENGTH + npduLength;
        var frame = new Byte[totalLength];
        Encode(frame, BacnetBvlcScFunctions.BVLC_SC_PEER_TO_PEER_FUNCTION, totalLength);
        Array.Copy(npduData, 0, frame, BVLC_HEADER_LENGTH, npduLength);
        return frame;
    }

    /// <summary>创建 SC Hub Connect 帧</summary>
    /// <param name="nodeUri">Node 的 WebSocket URI</param>
    /// <returns>完整的 SC 帧</returns>
    public static Byte[] CreateHubConnectFrame(String nodeUri)
    {
        var uriBytes = System.Text.Encoding.UTF8.GetBytes(nodeUri);
        var totalLength = BVLC_HEADER_LENGTH + uriBytes.Length;
        var frame = new Byte[totalLength];
        Encode(frame, BacnetBvlcScFunctions.BVLC_SC_HUB_CONNECT, totalLength);
        Array.Copy(uriBytes, 0, frame, BVLC_HEADER_LENGTH, uriBytes.Length);
        return frame;
    }

    /// <summary>创建 SC Hub Disconnect 帧</summary>
    /// <returns>完整的 SC 帧</returns>
    public static Byte[] CreateHubDisconnectFrame()
    {
        var frame = new Byte[BVLC_HEADER_LENGTH];
        Encode(frame, BacnetBvlcScFunctions.BVLC_SC_HUB_DISCONNECT, BVLC_HEADER_LENGTH);
        return frame;
    }

    /// <summary>从 SC 帧中提取负载数据（跳过 BVLC 头）</summary>
    /// <param name="frame">完整 SC 帧</param>
    /// <param name="headerLength">BVLC 头长度</param>
    /// <param name="payloadLength">输出：负载数据长度</param>
    /// <returns>负载数据字节数组</returns>
    public static Byte[] ExtractPayload(Byte[] frame, Int32 headerLength, out Int32 payloadLength)
    {
        payloadLength = frame.Length - headerLength;
        if (payloadLength <= 0)
            return [];

        var payload = new Byte[payloadLength];
        Array.Copy(frame, headerLength, payload, 0, payloadLength);
        return payload;
    }
}
