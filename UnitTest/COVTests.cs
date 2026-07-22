using System;
using System.Collections.Generic;
using System.IO.BACnet;
using System.IO.BACnet.Serialize;
using Xunit;

namespace UnitTest;

/// <summary>COV (Change of Value) 变更通知单元测试。
/// 测试 COV 订阅请求的编解码往返一致性。</summary>
public class COVTests
{
    #region SubscribeCOV

    [Fact]
    [System.ComponentModel.DisplayName("SubscribeCOV 编码→解码往返")]
    public void SubscribeCOV_RoundTrip()
    {
        var subscribeId = 123u;
        var objId = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, 1);
        var cancel = false;
        var issueConfirmed = true;
        var lifetime = 300u;

        var buf = new EncodeBuffer();
        Services.EncodeSubscribeCOV(buf, subscribeId, objId, cancel, issueConfirmed, lifetime);

        var len = Services.DecodeSubscribeCOV(buf.buffer, 0, buf.offset,
            out var decodedSubscriberId,
            out var decodedObjId,
            out var decodedCancel,
            out var decodedConfirmed,
            out var decodedLifetime);

        Assert.True(len > 0);
        Assert.Equal(subscribeId, decodedSubscriberId);
        Assert.Equal(objId.type, decodedObjId.type);
        Assert.Equal(objId.instance, decodedObjId.instance);
        Assert.Equal(cancel, decodedCancel);
        Assert.Equal(issueConfirmed, decodedConfirmed);
        Assert.Equal(lifetime, decodedLifetime);
    }

    [Fact]
    [System.ComponentModel.DisplayName("SubscribeCOV 取消订阅编码→解码往返")]
    public void SubscribeCOV_Cancel_RoundTrip()
    {
        var subscribeId = 456u;
        var objId = new BacnetObjectId(BacnetObjectTypes.OBJECT_BINARY_VALUE, 2);
        var cancel = true;
        var lifetime = 0u;

        var buf = new EncodeBuffer();
        Services.EncodeSubscribeCOV(buf, subscribeId, objId, cancel, false, lifetime);

        var len = Services.DecodeSubscribeCOV(buf.buffer, 0, buf.offset,
            out var decodedSubscriberId,
            out var decodedObjId,
            out var decodedCancel,
            out _,
            out var decodedLifetime);

        Assert.True(len > 0);
        Assert.Equal(subscribeId, decodedSubscriberId);
        Assert.Equal(cancel, decodedCancel);
        Assert.Equal(lifetime, decodedLifetime);
    }

    [Fact]
    [System.ComponentModel.DisplayName("SubscribeCOVProperty 编码→解码往返")]
    public void SubscribeCOVProperty_RoundTrip()
    {
        var subscribeId = 789u;
        var objId = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 3);
        var property = new BacnetPropertyReference((UInt32)BacnetPropertyIds.PROP_PRESENT_VALUE, ASN1.BACNET_ARRAY_ALL);
        var cancel = false;
        var issueConfirmed = false;
        var lifetime = 600u;
        var covIncrement = 0.5f;

        var buf = new EncodeBuffer();
        Services.EncodeSubscribeProperty(buf, subscribeId, objId, cancel, issueConfirmed, lifetime, property, true, covIncrement);

        var len = Services.DecodeSubscribeProperty(buf.buffer, 0, buf.offset,
            out var decodedSubscriberId,
            out var decodedObjId,
            out var decodedProperty,
            out var decodedCancel,
            out var decodedConfirmed,
            out var decodedLifetime,
            out var decodedIncrement);

        Assert.True(len > 0);
        Assert.Equal(subscribeId, decodedSubscriberId);
        Assert.Equal(objId.type, decodedObjId.type);
        Assert.Equal(objId.instance, decodedObjId.instance);
        Assert.Equal(property.propertyIdentifier, decodedProperty.propertyIdentifier);
        Assert.Equal(cancel, decodedCancel);
        Assert.Equal(issueConfirmed, decodedConfirmed);
        Assert.Equal(lifetime, decodedLifetime);
        Assert.Equal(covIncrement, decodedIncrement, 4);
    }

    #endregion

    #region COV Notification

    [Fact]
    [System.ComponentModel.DisplayName("COV 未确认通知编码→解码往返")]
    public void COVNotifyUnconfirmed_RoundTrip()
    {
        var subscriberId = 111u;
        var initDeviceId = 222u;
        var objId = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_OUTPUT, 5);
        var timeRemaining = 300u;
        var values = new List<BacnetPropertyValue>
        {
            new()
            {
                property = new BacnetPropertyReference((UInt32)BacnetPropertyIds.PROP_PRESENT_VALUE, ASN1.BACNET_ARRAY_ALL),
                value = new List<BacnetValue> { new(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, 25.5f) },
                priority = 0,
            }
        };

        var buf = new EncodeBuffer();
        Services.EncodeCOVNotifyUnconfirmed(buf, subscriberId, initDeviceId, objId, timeRemaining, values);

        System.UInt32 decodedSubscriberId2;
        BacnetObjectId decodedInitDeviceId2;
        BacnetObjectId decodedObjId2;
        System.UInt32 decodedTimeRemaining2;
        System.Collections.Generic.ICollection<BacnetPropertyValue> decodedValues2;
        var len = Services.DecodeCOVNotifyUnconfirmed(default(BacnetAddress), buf.buffer, 0, buf.offset,
            out decodedSubscriberId2,
            out decodedInitDeviceId2,
            out decodedObjId2,
            out decodedTimeRemaining2,
            out decodedValues2);

        Assert.True(len > 0);
        Assert.True(subscriberId == decodedSubscriberId2);
        Assert.True(initDeviceId == decodedInitDeviceId2.instance);
        Assert.True(objId.type == decodedObjId2.type);
        Assert.True(objId.instance == decodedObjId2.instance);
        Assert.True(timeRemaining == decodedTimeRemaining2);
        Assert.NotNull(decodedValues2);
    }

    [Fact]
    [System.ComponentModel.DisplayName("COV-4 确认通知编码→解码往返（含优先级）")]
    public void ConfirmedCOVNotification_RoundTrip()
    {
        var subscriberId = 333u;
        var initDeviceId = 444u;
        var objId = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, 10);
        var timeRemaining = 600u;
        var values = new List<BacnetPropertyValue>
        {
            new()
            {
                property = new BacnetPropertyReference((UInt32)BacnetPropertyIds.PROP_PRESENT_VALUE, ASN1.BACNET_ARRAY_ALL),
                value = new List<BacnetValue> { new(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, 33.3f) },
                priority = (Byte)8,
            },
            new()
            {
                property = new BacnetPropertyReference((UInt32)BacnetPropertyIds.PROP_STATUS_FLAGS, ASN1.BACNET_ARRAY_ALL),
                value = new List<BacnetValue>
                {
                    new(BacnetApplicationTags.BACNET_APPLICATION_TAG_BIT_STRING, BacnetBitString.Parse("0100"))
                },
                priority = (Byte)ASN1.BACNET_NO_PRIORITY,
            }
        };

        // 使用确认通知编码
        var buf = new EncodeBuffer();
        Services.EncodeCOVNotifyConfirmed(buf, subscriberId, initDeviceId, objId, timeRemaining, values);

        // 确认/未确认通知的载荷格式相同，使用同一解码函数
        var len = Services.DecodeCOVNotifyUnconfirmed(default(BacnetAddress), buf.buffer, 0, buf.offset,
            out var decodedSubscriberId,
            out var decodedInitDeviceId,
            out var decodedObjId,
            out var decodedTimeRemaining,
            out var decodedValues);

        Assert.True(len > 0);
        Assert.Equal(subscriberId, decodedSubscriberId);
        Assert.Equal(initDeviceId, decodedInitDeviceId.instance);
        Assert.Equal(objId.type, decodedObjId.type);
        Assert.Equal(objId.instance, decodedObjId.instance);
        Assert.Equal(timeRemaining, decodedTimeRemaining);
        Assert.NotNull(decodedValues);

        // 验证第一个值（含优先级）
        var list = new List<BacnetPropertyValue>(decodedValues);
        Assert.Equal(2, list.Count);
        Assert.Equal((UInt32)BacnetPropertyIds.PROP_PRESENT_VALUE, list[0].property.propertyIdentifier);
        Assert.Equal((Byte)8, list[0].priority);

        // 验证第二个值（无优先级）
        Assert.Equal((UInt32)BacnetPropertyIds.PROP_STATUS_FLAGS, list[1].property.propertyIdentifier);
        Assert.Equal((Byte)ASN1.BACNET_NO_PRIORITY, list[1].priority);
    }

    [Fact]
    [System.ComponentModel.DisplayName("COV-5 多种数据类型值通知编码→解码往返")]
    public void COVNotify_MultipleTypes_RoundTrip()
    {
        var subscriberId = 555u;
        var initDeviceId = 666u;
        var objId = new BacnetObjectId(BacnetObjectTypes.OBJECT_BINARY_INPUT, 7);
        var timeRemaining = 0u;

        var values = new List<BacnetPropertyValue>
        {
            new()
            {
                property = new BacnetPropertyReference((UInt32)BacnetPropertyIds.PROP_PRESENT_VALUE, ASN1.BACNET_ARRAY_ALL),
                value = new List<BacnetValue>
                {
                    new(BacnetApplicationTags.BACNET_APPLICATION_TAG_ENUMERATED, (UInt32)0) // BINARY_INACTIVE
                },
                priority = (Byte)ASN1.BACNET_NO_PRIORITY,
            },
            new()
            {
                property = new BacnetPropertyReference((UInt32)BacnetPropertyIds.PROP_STATUS_FLAGS, ASN1.BACNET_ARRAY_ALL),
                value = new List<BacnetValue>
                {
                    new(BacnetApplicationTags.BACNET_APPLICATION_TAG_BIT_STRING, BacnetBitString.Parse("1000"))
                },
                priority = (Byte)ASN1.BACNET_NO_PRIORITY,
            },
            new()
            {
                property = new BacnetPropertyReference((UInt32)BacnetPropertyIds.PROP_CHANGE_OF_STATE_TIME, ASN1.BACNET_ARRAY_ALL),
                value = new List<BacnetValue>
                {
                    new(BacnetApplicationTags.BACNET_APPLICATION_TAG_OCTET_STRING, new Byte[] { 0x01, 0x02, 0x03, 0x04 })
                },
                priority = (Byte)ASN1.BACNET_NO_PRIORITY,
            }
        };

        // 用未确认通知编码（服务端发送 COV 时的典型路径）
        var buf = new EncodeBuffer();
        Services.EncodeCOVNotifyUnconfirmed(buf, subscriberId, initDeviceId, objId, timeRemaining, values);

        var len = Services.DecodeCOVNotifyUnconfirmed(default(BacnetAddress), buf.buffer, 0, buf.offset,
            out var decodedSubscriberId,
            out var decodedInitDeviceId,
            out var decodedObjId,
            out var decodedTimeRemaining,
            out var decodedValues);

        Assert.True(len > 0);
        Assert.Equal(subscriberId, decodedSubscriberId);
        Assert.Equal(initDeviceId, decodedInitDeviceId.instance);
        Assert.Equal(objId.type, decodedObjId.type);
        Assert.Equal(objId.instance, decodedObjId.instance);
        Assert.Equal(timeRemaining, decodedTimeRemaining);
        Assert.NotNull(decodedValues);

        var list = new List<BacnetPropertyValue>(decodedValues);
        Assert.Equal(3, list.Count);

        // 验证第一个值（枚举类型）
        Assert.Equal((UInt32)BacnetPropertyIds.PROP_PRESENT_VALUE, list[0].property.propertyIdentifier);
        Assert.Equal((UInt32)0, (UInt32)list[0].value[0].Value);

        // 验证第二个值（位串类型）
        Assert.Equal((UInt32)BacnetPropertyIds.PROP_STATUS_FLAGS, list[1].property.propertyIdentifier);

        // 验证第三个值（字节串类型）
        Assert.Equal((UInt32)BacnetPropertyIds.PROP_CHANGE_OF_STATE_TIME, list[2].property.propertyIdentifier);
    }

    #endregion
}
