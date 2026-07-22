using System;
using System.Collections.Generic;
using System.IO.BACnet;
using System.IO.BACnet.Serialize;
using Xunit;

namespace UnitTest;

/// <summary>属性操作（PRP）编解码单元测试。
/// 测试 WriteProperty 含优先级、ReadRange 请求/响应的 encode→decode 往返一致性。</summary>
public class PropertyTests
{
    #region WriteProperty with Priority (PRP-3)

    [Fact]
    [System.ComponentModel.DisplayName("EncodeWriteProperty 含优先级 DecodeWriteProperty 往返")]
    public void WriteProperty_WithPriority_RoundTrip()
    {
        var objId = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_OUTPUT, 1);
        var propertyId = (UInt32)BacnetPropertyIds.PROP_PRESENT_VALUE;
        var arrayIndex = ASN1.BACNET_ARRAY_ALL;
        var priority = 8u;
        var valueList = new List<BacnetValue>
        {
            new(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, 42.5f),
        };

        var buf = new EncodeBuffer();
        Services.EncodeWriteProperty(buf, objId, propertyId, arrayIndex, priority, valueList);

        var len = Services.DecodeWriteProperty(default(BacnetAddress), buf.buffer, 0, buf.offset,
            out var decodedObjId,
            out var decodedValue);

        Assert.True(len > 0);
        Assert.Equal(objId.type, decodedObjId.type);
        Assert.Equal(objId.instance, decodedObjId.instance);
        Assert.Equal(propertyId, decodedValue.property.propertyIdentifier);
    }

    [Fact]
    [System.ComponentModel.DisplayName("EncodeWriteProperty 无优先级 priority=0 往返")]
    public void WriteProperty_NoPriority_RoundTrip()
    {
        var objId = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_OUTPUT, 1);
        var propertyId = (UInt32)BacnetPropertyIds.PROP_PRESENT_VALUE;
        var priority = ASN1.BACNET_NO_PRIORITY;

        var buf = new EncodeBuffer();
        Services.EncodeWriteProperty(buf, objId, propertyId, ASN1.BACNET_ARRAY_ALL, priority,
            new List<BacnetValue> { new(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, 100.0f) });

        var len = Services.DecodeWriteProperty(default(BacnetAddress), buf.buffer, 0, buf.offset,
            out var decodedObjId,
            out var decodedValue);

        Assert.True(len > 0);
        Assert.Equal(objId.type, decodedObjId.type);
        Assert.Equal(objId.instance, decodedObjId.instance);
    }

    [Fact]
    [System.ComponentModel.DisplayName("EncodeWriteProperty 含数组索引 DecodeWriteProperty 往返")]
    public void WriteProperty_WithArrayIndex_RoundTrip()
    {
        var objId = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 2);
        var propertyId = (UInt32)BacnetPropertyIds.PROP_PRIORITY_ARRAY;
        var arrayIndex = 5u;

        var buf = new EncodeBuffer();
        Services.EncodeWriteProperty(buf, objId, propertyId, arrayIndex, ASN1.BACNET_NO_PRIORITY,
            new List<BacnetValue> { new(BacnetApplicationTags.BACNET_APPLICATION_TAG_NULL, null) });

        var len = Services.DecodeWriteProperty(default(BacnetAddress), buf.buffer, 0, buf.offset,
            out var decodedObjId,
            out var decodedValue);

        Assert.True(len > 0);
        Assert.Equal(objId.type, decodedObjId.type);
        Assert.Equal(objId.instance, decodedObjId.instance);
        Assert.Equal(arrayIndex, decodedValue.property.propertyArrayIndex);
    }

    #endregion

    #region ReadRange 请求编解码 (PRP-6)

    [Fact]
    [System.ComponentModel.DisplayName("EncodeReadRange RR_BY_POSITION 往返")]
    public void ReadRange_ByPosition_RoundTrip()
    {
        var objId = new BacnetObjectId(BacnetObjectTypes.OBJECT_TRENDLOG, 1);
        var propertyId = (UInt32)BacnetPropertyIds.PROP_LOG_BUFFER;
        var position = 10u;
        var count = 5;

        var buf = new EncodeBuffer();
        Services.EncodeReadRange(buf, objId, propertyId, ASN1.BACNET_ARRAY_ALL,
            BacnetReadRangeRequestTypes.RR_BY_POSITION, position, DateTime.Now, count);

        var len = Services.DecodeReadRange(buf.buffer, 0, buf.offset,
            out var decodedObjId,
            out var decodedProperty,
            out var decodedRequestType,
            out var decodedPosition,
            out var decodedTime,
            out var decodedCount);

        Assert.True(len > 0);
        Assert.Equal(objId.type, decodedObjId.type);
        Assert.Equal(objId.instance, decodedObjId.instance);
        Assert.Equal(propertyId, decodedProperty.propertyIdentifier);
        Assert.Equal(BacnetReadRangeRequestTypes.RR_BY_POSITION, decodedRequestType);
        Assert.Equal(position, decodedPosition);
        Assert.Equal(count, decodedCount);
    }

    [Fact]
    [System.ComponentModel.DisplayName("EncodeReadRange RR_BY_SEQUENCE 往返")]
    public void ReadRange_BySequence_RoundTrip()
    {
        var objId = new BacnetObjectId(BacnetObjectTypes.OBJECT_TRENDLOG, 1);
        var propertyId = (UInt32)BacnetPropertyIds.PROP_LOG_BUFFER;
        var position = 100u;
        var count = 20;

        var buf = new EncodeBuffer();
        Services.EncodeReadRange(buf, objId, propertyId, ASN1.BACNET_ARRAY_ALL,
            BacnetReadRangeRequestTypes.RR_BY_SEQUENCE, position, DateTime.Now, count);

        var len = Services.DecodeReadRange(buf.buffer, 0, buf.offset,
            out var decodedObjId,
            out var decodedProperty,
            out var decodedRequestType,
            out var decodedPosition,
            out _,
            out var decodedCount);

        Assert.True(len > 0);
        Assert.Equal(BacnetReadRangeRequestTypes.RR_BY_SEQUENCE, decodedRequestType);
        Assert.Equal(position, decodedPosition);
        Assert.Equal(count, decodedCount);
    }

    [Fact]
    [System.ComponentModel.DisplayName("EncodeReadRange RR_BY_TIME 往返")]
    public void ReadRange_ByTime_RoundTrip()
    {
        var objId = new BacnetObjectId(BacnetObjectTypes.OBJECT_TRENDLOG, 1);
        var propertyId = (UInt32)BacnetPropertyIds.PROP_LOG_BUFFER;
        var time = new DateTime(2025, 6, 15, 8, 30, 0);
        var count = 50;

        var buf = new EncodeBuffer();
        Services.EncodeReadRange(buf, objId, propertyId, ASN1.BACNET_ARRAY_ALL,
            BacnetReadRangeRequestTypes.RR_BY_TIME, 0, time, count);

        var len = Services.DecodeReadRange(buf.buffer, 0, buf.offset,
            out var decodedObjId,
            out var decodedProperty,
            out var decodedRequestType,
            out _,
            out var decodedTime,
            out var decodedCount);

        Assert.True(len > 0);
        Assert.Equal(BacnetReadRangeRequestTypes.RR_BY_TIME, decodedRequestType);
        Assert.Equal(count, decodedCount);
        Assert.Equal(time.Hour, decodedTime.Hour);
        Assert.Equal(time.Minute, decodedTime.Minute);
        Assert.Equal(time.Second, decodedTime.Second);
    }

    [Fact]
    [System.ComponentModel.DisplayName("EncodeReadRange RR_READ_ALL 无范围参数 往返")]
    public void ReadRange_ReadAll_RoundTrip()
    {
        var objId = new BacnetObjectId(BacnetObjectTypes.OBJECT_TRENDLOG, 1);
        var propertyId = (UInt32)BacnetPropertyIds.PROP_LOG_BUFFER;

        var buf = new EncodeBuffer();
        Services.EncodeReadRange(buf, objId, propertyId, ASN1.BACNET_ARRAY_ALL,
            BacnetReadRangeRequestTypes.RR_READ_ALL, 0, DateTime.Now, -1);

        var len = Services.DecodeReadRange(buf.buffer, 0, buf.offset,
            out var decodedObjId,
            out var decodedProperty,
            out var decodedRequestType,
            out _,
            out _,
            out var decodedCount);

        Assert.True(len > 0);
        Assert.Equal(BacnetReadRangeRequestTypes.RR_READ_ALL, decodedRequestType);
        Assert.Equal(-1, decodedCount);
    }

    #endregion

    #region ReadRange 响应编解码

    [Fact]
    [System.ComponentModel.DisplayName("EncodeReadRangeAcknowledge DecodeReadRangeAcknowledge 往返")]
    public void ReadRangeAcknowledge_RoundTrip()
    {
        var objId = new BacnetObjectId(BacnetObjectTypes.OBJECT_TRENDLOG, 1);
        var propertyId = (UInt32)BacnetPropertyIds.PROP_LOG_BUFFER;
        var resultFlags = BacnetBitString.Parse("100");
        var itemCount = 3u;
        var firstSequence = 100u;

        var appData = new byte[12];
        BitConverter.GetBytes(25.0f).CopyTo(appData, 0);
        BitConverter.GetBytes(26.5f).CopyTo(appData, 4);
        BitConverter.GetBytes(28.0f).CopyTo(appData, 8);

        var buf = new EncodeBuffer();
        Services.EncodeReadRangeAcknowledge(buf, objId, propertyId, ASN1.BACNET_ARRAY_ALL,
            resultFlags, itemCount, appData, BacnetReadRangeRequestTypes.RR_BY_POSITION, firstSequence);

        var len = Services.DecodeReadRangeAcknowledge(buf.buffer, 0, buf.offset, out var rangeBuffer);

        Assert.True(len > 0);
        Assert.NotNull(rangeBuffer);
        Assert.True(rangeBuffer.Length >= appData.Length);
    }

    #endregion
}
