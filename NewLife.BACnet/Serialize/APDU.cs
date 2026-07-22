namespace System.IO.BACnet.Serialize;

/// <summary>Application Protocol Data Unit，应用协议数据单元。处理 BACnet APDU 的编码/解码，支持确认/未确认/错误/分片等 10+ PDU 类型。</summary>
public class APDU
{
    /// <summary>获取缓冲区中 APDU 的 PDU 类型</summary>
    /// <param name="buffer">数据缓冲区</param>
    /// <param name="offset">起始偏移</param>
    /// <returns>PDU 类型枚举值</returns>
    public static BacnetPduTypes GetDecodedType(byte[] buffer, int offset)
    {
        return (BacnetPduTypes)buffer[offset];
    }

    /// <summary>设置缓冲区中 APDU 的 PDU 类型</summary>
    /// <param name="buffer">数据缓冲区</param>
    /// <param name="offset">起始偏移</param>
    /// <param name="type">要设置的 PDU 类型</param>
    public static void SetDecodedType(byte[] buffer, int offset, BacnetPduTypes type)
    {
        buffer[offset] = (byte)type;
    }

    /// <summary>从 APDU 报文中提取 invoke-id</summary>
    /// <param name="buffer">数据缓冲区</param>
    /// <param name="offset">起始偏移</param>
    /// <returns>invoke-id（-1 表示当前 PDU 类型不含 invoke-id）</returns>
    /// <remarks>SimpleAck/ComplexAck/Error/Reject/Abort 的 invoke-id 在 offset+1，ConfirmedServiceRequest 在 offset+2。</remarks>
    public static int GetDecodedInvokeId(byte[] buffer, int offset)
    {
        var type = GetDecodedType(buffer, offset);
        switch (type & BacnetPduTypes.PDU_TYPE_MASK)
        {
            case BacnetPduTypes.PDU_TYPE_SIMPLE_ACK:
            case BacnetPduTypes.PDU_TYPE_COMPLEX_ACK:
            case BacnetPduTypes.PDU_TYPE_ERROR:
            case BacnetPduTypes.PDU_TYPE_REJECT:
            case BacnetPduTypes.PDU_TYPE_ABORT:
                return buffer[offset + 1];
            case BacnetPduTypes.PDU_TYPE_CONFIRMED_SERVICE_REQUEST:
                return buffer[offset + 2];
            default:
                return -1;
        }
    }

    /// <summary>编码确认服务请求 APDU</summary>
    /// <param name="buffer">编码缓冲区</param>
    /// <param name="type">PDU 类型（含分片标志）</param>
    /// <param name="service">确认服务类型</param>
    /// <param name="maxSegments">最大分片数</param>
    /// <param name="maxAdpu">最大 APDU 长度</param>
    /// <param name="invokeId">调用标识符</param>
    /// <param name="sequenceNumber">分片序号（分片消息时有效）</param>
    /// <param name="proposedWindowSize">建议窗口大小（分片消息时有效）</param>
    public static void EncodeConfirmedServiceRequest(EncodeBuffer buffer, BacnetPduTypes type, BacnetConfirmedServices service, BacnetMaxSegments maxSegments,
        BacnetMaxAdpu maxAdpu, byte invokeId, byte sequenceNumber = 0, byte proposedWindowSize = 0)
    {
        buffer.buffer[buffer.offset++] = (byte)type;
        buffer.buffer[buffer.offset++] = (byte)((byte)maxSegments | (byte)maxAdpu);
        buffer.buffer[buffer.offset++] = invokeId;

        if ((type & BacnetPduTypes.SEGMENTED_MESSAGE) > 0)
        {
            buffer.buffer[buffer.offset++] = sequenceNumber;
            buffer.buffer[buffer.offset++] = proposedWindowSize;
        }
        buffer.buffer[buffer.offset++] = (byte)service;
    }

    /// <summary>解码确认服务请求 APDU</summary>
    /// <param name="buffer">数据缓冲区</param>
    /// <param name="offset">起始偏移</param>
    /// <param name="type">解码后的 PDU 类型</param>
    /// <param name="service">解码后的确认服务类型</param>
    /// <param name="maxSegments">解码后的最大分片数</param>
    /// <param name="maxAdpu">解码后的最大 APDU 长度</param>
    /// <param name="invokeId">解码后的调用标识符</param>
    /// <param name="sequenceNumber">解码后的分片序号</param>
    /// <param name="proposedWindowNumber">解码后的建议窗口大小</param>
    /// <returns>消耗的字节数</returns>
    public static int DecodeConfirmedServiceRequest(byte[] buffer, int offset, out BacnetPduTypes type, out BacnetConfirmedServices service,
        out BacnetMaxSegments maxSegments, out BacnetMaxAdpu maxAdpu, out byte invokeId, out byte sequenceNumber, out byte proposedWindowNumber)
    {
        var orgOffset = offset;

        type = (BacnetPduTypes)buffer[offset++];
        maxSegments = (BacnetMaxSegments)(buffer[offset] & 0xF0);
        maxAdpu = (BacnetMaxAdpu)(buffer[offset++] & 0x0F);
        invokeId = buffer[offset++];

        sequenceNumber = 0;
        proposedWindowNumber = 0;
        if ((type & BacnetPduTypes.SEGMENTED_MESSAGE) > 0)
        {
            sequenceNumber = buffer[offset++];
            proposedWindowNumber = buffer[offset++];
        }
        service = (BacnetConfirmedServices)buffer[offset++];

        return offset - orgOffset;
    }

    /// <summary>编码未确认服务请求 APDU</summary>
    /// <param name="buffer">编码缓冲区</param>
    /// <param name="type">PDU 类型</param>
    /// <param name="service">未确认服务类型</param>
    public static void EncodeUnconfirmedServiceRequest(EncodeBuffer buffer, BacnetPduTypes type, BacnetUnconfirmedServices service)
    {
        buffer.buffer[buffer.offset++] = (byte)type;
        buffer.buffer[buffer.offset++] = (byte)service;
    }

    /// <summary>解码未确认服务请求 APDU</summary>
    /// <param name="buffer">数据缓冲区</param>
    /// <param name="offset">起始偏移</param>
    /// <param name="type">解码后的 PDU 类型</param>
    /// <param name="service">解码后的未确认服务类型</param>
    /// <returns>消耗的字节数</returns>
    public static int DecodeUnconfirmedServiceRequest(byte[] buffer, int offset, out BacnetPduTypes type, out BacnetUnconfirmedServices service)
    {
        var orgOffset = offset;

        type = (BacnetPduTypes)buffer[offset++];
        service = (BacnetUnconfirmedServices)buffer[offset++];

        return offset - orgOffset;
    }

    /// <summary>编码简单确认 APDU</summary>
    /// <param name="buffer">编码缓冲区</param>
    /// <param name="type">PDU 类型</param>
    /// <param name="service">确认的服务类型</param>
    /// <param name="invokeId">调用标识符</param>
    public static void EncodeSimpleAck(EncodeBuffer buffer, BacnetPduTypes type, BacnetConfirmedServices service, byte invokeId)
    {
        buffer.buffer[buffer.offset++] = (byte)type;
        buffer.buffer[buffer.offset++] = invokeId;
        buffer.buffer[buffer.offset++] = (byte)service;
    }

    /// <summary>解码简单确认 APDU</summary>
    /// <param name="buffer">数据缓冲区</param>
    /// <param name="offset">起始偏移</param>
    /// <param name="type">解码后的 PDU 类型</param>
    /// <param name="service">解码后的服务类型</param>
    /// <param name="invokeId">解码后的调用标识符</param>
    /// <returns>消耗的字节数</returns>
    public static int DecodeSimpleAck(byte[] buffer, int offset, out BacnetPduTypes type, out BacnetConfirmedServices service, out byte invokeId)
    {
        var orgOffset = offset;

        type = (BacnetPduTypes)buffer[offset++];
        invokeId = buffer[offset++];
        service = (BacnetConfirmedServices)buffer[offset++];

        return offset - orgOffset;
    }

    /// <summary>编码复杂确认 APDU（支持分片）</summary>
    /// <param name="buffer">编码缓冲区</param>
    /// <param name="type">PDU 类型（含分片标志）</param>
    /// <param name="service">确认的服务类型</param>
    /// <param name="invokeId">调用标识符</param>
    /// <param name="sequenceNumber">分片序号（分片消息时有效）</param>
    /// <param name="proposedWindowNumber">建议窗口大小（分片消息时有效）</param>
    /// <returns>编码后的 APDU 头长度（不含负载）</returns>
    public static int EncodeComplexAck(EncodeBuffer buffer, BacnetPduTypes type, BacnetConfirmedServices service, byte invokeId, byte sequenceNumber = 0, byte proposedWindowNumber = 0)
    {
        var len = 3;
        buffer.buffer[buffer.offset++] = (byte)type;
        buffer.buffer[buffer.offset++] = invokeId;
        if ((type & BacnetPduTypes.SEGMENTED_MESSAGE) > 0)
        {
            buffer.buffer[buffer.offset++] = sequenceNumber;
            buffer.buffer[buffer.offset++] = proposedWindowNumber;
            len += 2;
        }
        buffer.buffer[buffer.offset++] = (byte)service;
        return len;
    }

    /// <summary>解码复杂确认 APDU（支持分片）</summary>
    /// <param name="buffer">数据缓冲区</param>
    /// <param name="offset">起始偏移</param>
    /// <param name="type">解码后的 PDU 类型</param>
    /// <param name="service">解码后的服务类型</param>
    /// <param name="invokeId">解码后的调用标识符</param>
    /// <param name="sequenceNumber">解码后的分片序号</param>
    /// <param name="proposedWindowNumber">解码后的建议窗口大小</param>
    /// <returns>消耗的字节数</returns>
    public static int DecodeComplexAck(byte[] buffer, int offset, out BacnetPduTypes type, out BacnetConfirmedServices service, out byte invokeId,
        out byte sequenceNumber, out byte proposedWindowNumber)
    {
        var orgOffset = offset;

        type = (BacnetPduTypes)buffer[offset++];
        invokeId = buffer[offset++];

        sequenceNumber = 0;
        proposedWindowNumber = 0;
        if ((type & BacnetPduTypes.SEGMENTED_MESSAGE) > 0)
        {
            sequenceNumber = buffer[offset++];
            proposedWindowNumber = buffer[offset++];
        }
        service = (BacnetConfirmedServices)buffer[offset++];

        return offset - orgOffset;
    }

    /// <summary>编码分片确认 APDU</summary>
    /// <param name="buffer">编码缓冲区</param>
    /// <param name="type">PDU 类型</param>
    /// <param name="originalInvokeId">原始 invoke-id</param>
    /// <param name="sequenceNumber">分片序号</param>
    /// <param name="actualWindowSize">实际窗口大小</param>
    public static void EncodeSegmentAck(EncodeBuffer buffer, BacnetPduTypes type, byte originalInvokeId, byte sequenceNumber, byte actualWindowSize)
    {
        buffer.buffer[buffer.offset++] = (byte)type;
        buffer.buffer[buffer.offset++] = originalInvokeId;
        buffer.buffer[buffer.offset++] = sequenceNumber;
        buffer.buffer[buffer.offset++] = actualWindowSize;
    }

    /// <summary>解码分片确认 APDU</summary>
    /// <param name="buffer">数据缓冲区</param>
    /// <param name="offset">起始偏移</param>
    /// <param name="type">解码后的 PDU 类型</param>
    /// <param name="originalInvokeId">解码后的原始 invoke-id</param>
    /// <param name="sequenceNumber">解码后的分片序号</param>
    /// <param name="actualWindowSize">解码后的实际窗口大小</param>
    /// <returns>消耗的字节数</returns>
    public static int DecodeSegmentAck(byte[] buffer, int offset, out BacnetPduTypes type, out byte originalInvokeId, out byte sequenceNumber, out byte actualWindowSize)
    {
        var orgOffset = offset;

        type = (BacnetPduTypes)buffer[offset++];
        originalInvokeId = buffer[offset++];
        sequenceNumber = buffer[offset++];
        actualWindowSize = buffer[offset++];

        return offset - orgOffset;
    }

    /// <summary>编码错误 APDU</summary>
    /// <param name="buffer">编码缓冲区</param>
    /// <param name="type">PDU 类型</param>
    /// <param name="service">产生错误的确认服务类型</param>
    /// <param name="invokeId">原始调用标识符</param>
    public static void EncodeError(EncodeBuffer buffer, BacnetPduTypes type, BacnetConfirmedServices service, byte invokeId)
    {
        buffer.buffer[buffer.offset++] = (byte)type;
        buffer.buffer[buffer.offset++] = invokeId;
        buffer.buffer[buffer.offset++] = (byte)service;
    }

    /// <summary>解码错误 APDU</summary>
    /// <param name="buffer">数据缓冲区</param>
    /// <param name="offset">起始偏移</param>
    /// <param name="type">解码后的 PDU 类型</param>
    /// <param name="service">解码后的服务类型</param>
    /// <param name="invokeId">解码后的调用标识符</param>
    /// <returns>消耗的字节数</returns>
    public static int DecodeError(byte[] buffer, int offset, out BacnetPduTypes type, out BacnetConfirmedServices service, out byte invokeId)
    {
        var orgOffset = offset;

        type = (BacnetPduTypes)buffer[offset++];
        invokeId = buffer[offset++];
        service = (BacnetConfirmedServices)buffer[offset++];

        return offset - orgOffset;
    }

    /// <summary>编码中止 APDU</summary>
    /// <param name="buffer">编码缓冲区</param>
    /// <param name="type">PDU 类型</param>
    /// <param name="invokeId">调用标识符</param>
    /// <param name="reason">中止原因</param>
    public static void EncodeAbort(EncodeBuffer buffer, BacnetPduTypes type, byte invokeId, BacnetAbortReason reason)
    {
        EncodeAbortOrReject(buffer, type, invokeId, reason);
    }

    /// <summary>编码拒绝 APDU</summary>
    /// <param name="buffer">编码缓冲区</param>
    /// <param name="type">PDU 类型</param>
    /// <param name="invokeId">调用标识符</param>
    /// <param name="reason">拒绝原因</param>
    public static void EncodeReject(EncodeBuffer buffer, BacnetPduTypes type, byte invokeId, BacnetRejectReason reason)
    {
        EncodeAbortOrReject(buffer, type, invokeId, reason);
    }

    private static void EncodeAbortOrReject(EncodeBuffer buffer, BacnetPduTypes type, byte invokeId, dynamic reason)
    {
        buffer.buffer[buffer.offset++] = (byte)type;
        buffer.buffer[buffer.offset++] = invokeId;
        buffer.buffer[buffer.offset++] = (byte)reason;
    }

    /// <summary>解码中止 APDU</summary>
    /// <param name="buffer">数据缓冲区</param>
    /// <param name="offset">起始偏移</param>
    /// <param name="type">解码后的 PDU 类型</param>
    /// <param name="invokeId">解码后的调用标识符</param>
    /// <param name="reason">解码后的中止原因</param>
    /// <returns>消耗的字节数</returns>
    public static int DecodeAbort(byte[] buffer, int offset, out BacnetPduTypes type,
        out byte invokeId, out BacnetAbortReason reason)
    {
        return DecodeAbortOrReject(buffer, offset, out type, out invokeId, out reason);
    }

    /// <summary>解码拒绝 APDU</summary>
    /// <param name="buffer">数据缓冲区</param>
    /// <param name="offset">起始偏移</param>
    /// <param name="type">解码后的 PDU 类型</param>
    /// <param name="invokeId">解码后的调用标识符</param>
    /// <param name="reason">解码后的拒绝原因</param>
    /// <returns>消耗的字节数</returns>
    public static int DecodeReject(byte[] buffer, int offset, out BacnetPduTypes type,
        out byte invokeId, out BacnetRejectReason reason)
    {
        return DecodeAbortOrReject(buffer, offset, out type, out invokeId, out reason);
    }

    private static int DecodeAbortOrReject<TReason>(byte[] buffer, int offset,
        out BacnetPduTypes type, out byte invokeId, out TReason reason)
    {
        var orgOffset = offset;

        type = (BacnetPduTypes)buffer[offset++];
        invokeId = buffer[offset++];
        reason = (TReason)(dynamic)buffer[offset++];

        return offset - orgOffset;
    }
}
