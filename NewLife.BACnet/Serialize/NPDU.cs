namespace System.IO.BACnet.Serialize;

/// <summary>Network Protocol Data Unit，网络协议数据单元。处理 BACnet NPDU 的编码/解码，含源/目标地址和跳数管理。</summary>
public class NPDU
{
    /// <summary>BACnet 协议版本号（固定为 1）</summary>
    public const byte BACNET_PROTOCOL_VERSION = 1;

    /// <summary>解码 NPDU 头部中的控制字节</summary>
    /// <param name="buffer">数据缓冲区</param>
    /// <param name="offset">起始偏移</param>
    /// <returns>控制标志位组合</returns>
    public static BacnetNpduControls DecodeFunction(byte[] buffer, int offset)
    {
        if (buffer[offset + 0] != BACNET_PROTOCOL_VERSION) return 0;
        return (BacnetNpduControls)buffer[offset + 1];
    }

    /// <summary>解码完整 NPDU 报文</summary>
    /// <param name="buffer">数据缓冲区</param>
    /// <param name="offset">起始偏移</param>
    /// <param name="function">解码后的控制标志</param>
    /// <param name="destination">解码后的目标地址（可为 null）</param>
    /// <param name="source">解码后的源地址（可为 null）</param>
    /// <param name="hopCount">解码后的跳数</param>
    /// <param name="networkMsgType">网络层消息类型（仅 NetworkLayerMessage 时有效）</param>
    /// <param name="vendorId">厂商 ID（仅网络层消息类型 >= 0x80 时有效）</param>
    /// <returns>消耗的字节数，-1 表示协议版本不匹配</returns>
    public static int Decode(byte[] buffer, int offset, out BacnetNpduControls function, out BacnetAddress destination,
        out BacnetAddress source, out byte hopCount, out BacnetNetworkMessageTypes networkMsgType, out ushort vendorId)
    {
        var orgOffset = offset;

        offset++;
        function = (BacnetNpduControls)buffer[offset++];

        destination = null;
        if ((function & BacnetNpduControls.DestinationSpecified) == BacnetNpduControls.DestinationSpecified)
        {
            destination = new BacnetAddress(BacnetAddressTypes.None, (ushort)((buffer[offset++] << 8) | (buffer[offset++] << 0)), null);
            int adrLen = buffer[offset++];
            if (adrLen > 0)
            {
                destination.adr = new byte[adrLen];
                for (var i = 0; i < destination.adr.Length; i++)
                    destination.adr[i] = buffer[offset++];
            }
        }

        source = null;
        if ((function & BacnetNpduControls.SourceSpecified) == BacnetNpduControls.SourceSpecified)
        {
            source = new BacnetAddress(BacnetAddressTypes.None, (ushort)((buffer[offset++] << 8) | (buffer[offset++] << 0)), null);
            int adrLen = buffer[offset++];
            if (adrLen > 0)
            {
                source.adr = new byte[adrLen];
                for (var i = 0; i < source.adr.Length; i++)
                    source.adr[i] = buffer[offset++];
            }
        }

        hopCount = 0;
        if ((function & BacnetNpduControls.DestinationSpecified) == BacnetNpduControls.DestinationSpecified)
        {
            hopCount = buffer[offset++];
        }

        networkMsgType = BacnetNetworkMessageTypes.NETWORK_MESSAGE_WHO_IS_ROUTER_TO_NETWORK;
        vendorId = 0;
        if (function.HasFlag(BacnetNpduControls.NetworkLayerMessage))
        {
            networkMsgType = (BacnetNetworkMessageTypes)buffer[offset++];
            if ((byte)networkMsgType >= 0x80)
            {
                vendorId = (ushort)((buffer[offset++] << 8) | (buffer[offset++] << 0));
            }
            //DAL - this originally made no sense as the higher level code would just ignore network messages
            //                else if (networkMsgType == BacnetNetworkMessageTypes.NETWORK_MESSAGE_WHO_IS_ROUTER_TO_NETWORK)
            //                    offset += 2;  // Don't care about destination network adress
        }

        if (buffer[orgOffset + 0] != BACNET_PROTOCOL_VERSION)
            return -1;

        return offset - orgOffset;
    }

    /// <summary>编码 NPDU 报文（含网络层消息类型和厂商 ID）</summary>
    /// <param name="buffer">编码缓冲区</param>
    /// <param name="function">控制标志</param>
    /// <param name="destination">目标地址（可为 null）</param>
    /// <param name="source">源地址（可为 null）</param>
    /// <param name="hopCount">跳数</param>
    /// <param name="networkMsgType">网络层消息类型</param>
    /// <param name="vendorId">厂商 ID（networkMsgType >= 0x80 时写入）</param>
    public static void Encode(EncodeBuffer buffer, BacnetNpduControls function, BacnetAddress destination,
        BacnetAddress source, byte hopCount, BacnetNetworkMessageTypes networkMsgType, ushort vendorId)
    {
        Encode(buffer, function, destination, source, hopCount);

        if (function.HasFlag(BacnetNpduControls.NetworkLayerMessage)) // sure it is, otherwise the other Encode is used
        {
            buffer.buffer[buffer.offset++] = (byte)networkMsgType;
            if ((byte)networkMsgType >= 0x80) // who used this ??? sure nobody !
            {
                buffer.buffer[buffer.offset++] = (byte)((vendorId & 0xFF00) >> 8);
                buffer.buffer[buffer.offset++] = (byte)((vendorId & 0x00FF) >> 0);
            }
        }
    }

    /// <summary>编码 NPDU 报文（基础版本，不含网络层消息类型）</summary>
    /// <param name="buffer">编码缓冲区</param>
    /// <param name="function">控制标志</param>
    /// <param name="destination">目标地址。net=0 或 null 时不写入</param>
    /// <param name="source">源地址。net=0 或 0xFFFF 时不写入</param>
    /// <param name="hopCount">跳数，默认 0xFF</param>
    public static void Encode(EncodeBuffer buffer, BacnetNpduControls function, BacnetAddress destination,
        BacnetAddress source = null, byte hopCount = 0xFF)
    {
        // Modif FC
        var hasDestination = destination != null && destination.net > 0; // && destination.net != 0xFFFF;
        var hasSource = source != null && source.net > 0 && source.net != 0xFFFF;

        buffer.buffer[buffer.offset++] = BACNET_PROTOCOL_VERSION;
        buffer.buffer[buffer.offset++] = (byte)(function | (hasDestination ? BacnetNpduControls.DestinationSpecified : 0) | (hasSource ? BacnetNpduControls.SourceSpecified : 0));

        if (hasDestination)
        {
            buffer.buffer[buffer.offset++] = (byte)((destination.net & 0xFF00) >> 8);
            buffer.buffer[buffer.offset++] = (byte)((destination.net & 0x00FF) >> 0);

            if (destination.net == 0xFFFF)                  //patch by F. Chaxel
                buffer.buffer[buffer.offset++] = 0;
            else
            {
                buffer.buffer[buffer.offset++] = (byte)destination.adr.Length;
                if (destination.adr.Length > 0)
                {
                    foreach (var t in destination.adr)
                        buffer.buffer[buffer.offset++] = t;
                }
            }
        }

        if (hasSource)
        {
            buffer.buffer[buffer.offset++] = (byte)((source.net & 0xFF00) >> 8);
            buffer.buffer[buffer.offset++] = (byte)((source.net & 0x00FF) >> 0);
            buffer.buffer[buffer.offset++] = (byte)source.adr.Length;
            if (source.adr.Length > 0)
            {
                foreach (var t in source.adr)
                    buffer.buffer[buffer.offset++] = t;
            }
        }

        if (hasDestination)
        {
            buffer.buffer[buffer.offset++] = hopCount;
        }
    }
}
