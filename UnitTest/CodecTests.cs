using System;
using System.IO.BACnet;
using System.IO.BACnet.Serialize;
using Xunit;

namespace UnitTest;

/// <summary>协议编解码（COD）单元测试。
/// 测试 APDU、NPDU、EncodeBuffer 的 encode→decode 往返一致性。</summary>
public class CodecTests
{
    #region EncodeBuffer 基础操作 (COD-8)

    [Fact]
    [System.ComponentModel.DisplayName("EncodeBuffer 默认构造可扩展")]
    public void EncodeBuffer_Default_Expandable()
    {
        var buf = new EncodeBuffer();
        Assert.True(buf.expandable);
        Assert.NotNull(buf.buffer);
        Assert.True(buf.max_offset > 0);
        Assert.Equal(0, buf.offset);
    }

    [Fact]
    [System.ComponentModel.DisplayName("EncodeBuffer Add 单字节")]
    public void EncodeBuffer_AddByte()
    {
        var buf = new EncodeBuffer();
        buf.Add(0xAB);
        Assert.Equal(1, buf.offset);
        Assert.Equal(0xAB, buf.buffer[0]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("EncodeBuffer Add 多字节")]
    public void EncodeBuffer_AddBytes()
    {
        var buf = new EncodeBuffer();
        buf.Add(new byte[] { 0x01, 0x02, 0x03 }, 3);
        Assert.Equal(3, buf.offset);
        Assert.Equal(0x01, buf.buffer[0]);
        Assert.Equal(0x02, buf.buffer[1]);
        Assert.Equal(0x03, buf.buffer[2]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("EncodeBuffer ToArray 截取有效数据")]
    public void EncodeBuffer_ToArray()
    {
        var buf = new EncodeBuffer();
        buf.Add(0x11);
        buf.Add(0x22);
        buf.Add(0x33);
        var arr = buf.ToArray();
        Assert.Equal(3, arr.Length);
        Assert.Equal(0x11, arr[0]);
        Assert.Equal(0x22, arr[1]);
        Assert.Equal(0x33, arr[2]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("EncodeBuffer 非可扩展模式超出报 NotEnoughBuffer")]
    public void EncodeBuffer_NotExpandable_Overflow()
    {
        var backing = new byte[2];
        var buf = new EncodeBuffer(backing, 0);
        buf.Add(0x01);
        buf.Add(0x02);
        buf.Add(0x03);
        Assert.True((buf.result & EncodeResult.NotEnoughBuffer) != 0);
    }

    [Fact]
    [System.ComponentModel.DisplayName("EncodeBuffer Reset 重置状态")]
    public void EncodeBuffer_Reset()
    {
        var buf = new EncodeBuffer();
        buf.Add(0x01);
        buf.Add(0x02);
        Assert.Equal(2, buf.offset);

        buf.Reset(1);
        Assert.Equal(1, buf.offset);
        Assert.Equal(0, buf.serialize_counter);
        Assert.Equal(EncodeResult.Good, buf.result);
    }

    #endregion

    #region NPDU 编解码 (COD-3)

    [Fact]
    [System.ComponentModel.DisplayName("NPDU Encode/Decode 无地址往返")]
    public void NPDU_NoAddress_RoundTrip()
    {
        var buf = new EncodeBuffer();
        var function = BacnetNpduControls.NetworkLayerMessage;

        NPDU.Encode(buf, function, null, null, 0xFF,
            BacnetNetworkMessageTypes.NETWORK_MESSAGE_WHO_IS_ROUTER_TO_NETWORK, 0);

        var len = NPDU.Decode(buf.buffer, 0, out var decodedFunction,
            out var dest, out var src, out var hopCount,
            out var networkMsgType, out var vendorId);

        Assert.True(len > 0);
        Assert.True((decodedFunction & BacnetNpduControls.NetworkLayerMessage) != 0);
    }

    [Fact]
    [System.ComponentModel.DisplayName("NPDU Encode/Decode 含目标地址往返")]
    public void NPDU_WithDestination_RoundTrip()
    {
        var buf = new EncodeBuffer();

        var function = BacnetNpduControls.PriorityNormalMessage;
        var dest = new BacnetAddress(BacnetAddressTypes.None, 100, new byte[0]);

        NPDU.Encode(buf, function, dest, null);

        var len = NPDU.Decode(buf.buffer, 0, out var decodedFunction,
            out var decodedDest, out _, out _, out _, out _);

        Assert.True(len > 0);
        Assert.NotNull(decodedDest);
        Assert.Equal((ushort)100, decodedDest.net);
    }

    [Fact]
    [System.ComponentModel.DisplayName("NPDU DecodeFunction 读取控制字节")]
    public void NPDU_DecodeFunction()
    {
        var buf = new EncodeBuffer();
        NPDU.Encode(buf, BacnetNpduControls.PriorityNormalMessage, null, null);

        var decoded = NPDU.DecodeFunction(buf.buffer, 0);
        Assert.Equal(BacnetNpduControls.PriorityNormalMessage,
                     decoded & BacnetNpduControls.PriorityNormalMessage);
    }

    #endregion

    #region APDU 编解码 (COD-2)

    [Fact]
    [System.ComponentModel.DisplayName("APDU 未确认服务请求编解码往返")]
    public void APDU_UnconfirmedServiceRequest_RoundTrip()
    {
        var buf = new EncodeBuffer();
        APDU.EncodeUnconfirmedServiceRequest(buf,
            BacnetPduTypes.PDU_TYPE_UNCONFIRMED_SERVICE_REQUEST,
            BacnetUnconfirmedServices.SERVICE_UNCONFIRMED_I_AM);

        var len = APDU.DecodeUnconfirmedServiceRequest(buf.buffer, 0,
            out var decodedType, out var decodedService);

        Assert.True(len > 0);
        Assert.Equal(BacnetPduTypes.PDU_TYPE_UNCONFIRMED_SERVICE_REQUEST,
                     decodedType & BacnetPduTypes.PDU_TYPE_MASK);
        Assert.Equal(BacnetUnconfirmedServices.SERVICE_UNCONFIRMED_I_AM, decodedService);
    }

    [Fact]
    [System.ComponentModel.DisplayName("APDU 确认服务请求编解码往返")]
    public void APDU_ConfirmedServiceRequest_RoundTrip()
    {
        var buf = new EncodeBuffer();
        APDU.EncodeConfirmedServiceRequest(buf,
            BacnetPduTypes.PDU_TYPE_CONFIRMED_SERVICE_REQUEST,
            BacnetConfirmedServices.SERVICE_CONFIRMED_READ_PROPERTY,
            BacnetMaxSegments.MAX_SEG0, BacnetMaxAdpu.MAX_APDU1476, 1, 0);

        var len = APDU.DecodeConfirmedServiceRequest(buf.buffer, 0,
            out var decodedType, out var decodedService,
            out var decodedMaxSeg, out var decodedMaxAdpu,
            out var decodedInvokeId, out var decodedSequenceNumber,
            out var decodedProposedWindow);

        Assert.True(len > 0);
        Assert.Equal(BacnetPduTypes.PDU_TYPE_CONFIRMED_SERVICE_REQUEST,
                     decodedType & BacnetPduTypes.PDU_TYPE_MASK);
        Assert.Equal(BacnetConfirmedServices.SERVICE_CONFIRMED_READ_PROPERTY, decodedService);
        Assert.Equal(1, decodedInvokeId);
    }

    [Fact]
    [System.ComponentModel.DisplayName("APDU SimpleAck 编解码往返")]
    public void APDU_SimpleAck_RoundTrip()
    {
        var buf = new EncodeBuffer();
        APDU.EncodeSimpleAck(buf, BacnetPduTypes.PDU_TYPE_SIMPLE_ACK,
            BacnetConfirmedServices.SERVICE_CONFIRMED_READ_PROPERTY, 5);

        var len = APDU.DecodeSimpleAck(buf.buffer, 0,
            out var decodedType, out var decodedService, out var decodedInvokeId);

        Assert.True(len > 0);
        Assert.Equal(BacnetPduTypes.PDU_TYPE_SIMPLE_ACK,
                     decodedType & BacnetPduTypes.PDU_TYPE_MASK);
        Assert.Equal(BacnetConfirmedServices.SERVICE_CONFIRMED_READ_PROPERTY, decodedService);
        Assert.Equal(5, decodedInvokeId);
    }

    [Fact]
    [System.ComponentModel.DisplayName("APDU Error 编解码往返")]
    public void APDU_Error_RoundTrip()
    {
        var buf = new EncodeBuffer();
        APDU.EncodeError(buf, BacnetPduTypes.PDU_TYPE_ERROR,
            BacnetConfirmedServices.SERVICE_CONFIRMED_READ_PROPERTY, 10);

        var len = APDU.DecodeError(buf.buffer, 0,
            out var decodedType, out var decodedService, out var decodedInvokeId);

        Assert.True(len > 0);
        Assert.Equal(BacnetPduTypes.PDU_TYPE_ERROR,
                     decodedType & BacnetPduTypes.PDU_TYPE_MASK);
        Assert.Equal(BacnetConfirmedServices.SERVICE_CONFIRMED_READ_PROPERTY, decodedService);
        Assert.Equal(10, decodedInvokeId);
    }

    [Fact]
    [System.ComponentModel.DisplayName("APDU Abort 编解码往返")]
    public void APDU_Abort_RoundTrip()
    {
        var buf = new EncodeBuffer();
        APDU.EncodeAbort(buf, BacnetPduTypes.PDU_TYPE_ABORT, 3,
            BacnetAbortReason.BUFFER_OVERFLOW);

        var len = APDU.DecodeAbort(buf.buffer, 0,
            out var decodedType, out var decodedInvokeId, out var decodedReason);

        Assert.True(len > 0);
        Assert.Equal(BacnetPduTypes.PDU_TYPE_ABORT,
                     decodedType & BacnetPduTypes.PDU_TYPE_MASK);
        Assert.Equal(3, decodedInvokeId);
        Assert.Equal(BacnetAbortReason.BUFFER_OVERFLOW, decodedReason);
    }

    [Fact]
    [System.ComponentModel.DisplayName("APDU Reject 编解码往返")]
    public void APDU_Reject_RoundTrip()
    {
        var buf = new EncodeBuffer();
        APDU.EncodeReject(buf, BacnetPduTypes.PDU_TYPE_REJECT, 7,
            BacnetRejectReason.RECOGNIZED_SERVICE);

        var len = APDU.DecodeReject(buf.buffer, 0,
            out var decodedType, out var decodedInvokeId, out var decodedReason);

        Assert.True(len > 0);
        Assert.Equal(BacnetPduTypes.PDU_TYPE_REJECT,
                     decodedType & BacnetPduTypes.PDU_TYPE_MASK);
        Assert.Equal(7, decodedInvokeId);
        Assert.Equal(BacnetRejectReason.RECOGNIZED_SERVICE, decodedReason);
    }

    [Fact]
    [System.ComponentModel.DisplayName("APDU SegmentAck 编解码往返")]
    public void APDU_SegmentAck_RoundTrip()
    {
        var buf = new EncodeBuffer();
        APDU.EncodeSegmentAck(buf, BacnetPduTypes.PDU_TYPE_SEGMENT_ACK, 1, 5, 4);

        var len = APDU.DecodeSegmentAck(buf.buffer, 0,
            out var decodedType, out var decodedOriginalInvokeId,
            out var decodedSequenceNumber, out var decodedActualWindowSize);

        Assert.True(len > 0);
        Assert.Equal(BacnetPduTypes.PDU_TYPE_SEGMENT_ACK,
                     decodedType & BacnetPduTypes.PDU_TYPE_MASK);
        Assert.Equal(1, decodedOriginalInvokeId);
        Assert.Equal(5, decodedSequenceNumber);
        Assert.Equal(4, decodedActualWindowSize);
    }

    [Fact]
    [System.ComponentModel.DisplayName("APDU GetDecodedType/SetDecodedType")]
    public void APDU_GetSetPduType()
    {
        var buf = new EncodeBuffer();
        APDU.EncodeSimpleAck(buf, BacnetPduTypes.PDU_TYPE_SIMPLE_ACK,
            BacnetConfirmedServices.SERVICE_CONFIRMED_READ_PROPERTY, 1);

        var pduType = APDU.GetDecodedType(buf.buffer, 0);
        Assert.Equal(BacnetPduTypes.PDU_TYPE_SIMPLE_ACK,
                     pduType & BacnetPduTypes.PDU_TYPE_MASK);

        APDU.SetDecodedType(buf.buffer, 0, BacnetPduTypes.PDU_TYPE_ERROR);
        pduType = APDU.GetDecodedType(buf.buffer, 0);
        Assert.Equal(BacnetPduTypes.PDU_TYPE_ERROR,
                     pduType & BacnetPduTypes.PDU_TYPE_MASK);
    }

    #endregion
}
