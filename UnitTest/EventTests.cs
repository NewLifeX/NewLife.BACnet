using System;
using System.Collections.Generic;
using System.IO.BACnet;
using System.IO.BACnet.Serialize;
using Xunit;

namespace UnitTest;

/// <summary>事件与报警（EVT）编解码单元测试。
/// 测试 EventNotification、GetAlarmSummary、GetEventInformation、AcknowledgeAlarm 的 encode→decode 往返一致性。</summary>
public class EventTests
{
    #region 辅助

    /// <summary>创建简单的 EventNotification 测试数据</summary>
    private static BacnetEventNotificationData CreateTestEventData()
    {
        return new BacnetEventNotificationData
        {
            processIdentifier = 1,
            initiatingObjectIdentifier = new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, 100),
            eventObjectIdentifier = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 5),
            timeStamp = new BacnetGenericTime(DateTime.Now, BacnetTimestampTags.TIME_STAMP_DATETIME),
            notificationClass = 700,
            priority = 128,
            eventType = BacnetEventTypes.EVENT_OUT_OF_RANGE,
            messageText = "AI:5 out of range",
            notifyType = BacnetNotifyTypes.NOTIFY_ALARM,
            ackRequired = true,
            fromState = BacnetEventStates.EVENT_STATE_NORMAL,
            toState = BacnetEventStates.EVENT_STATE_HIGH_LIMIT,
            outOfRange_exceedingValue = 105.0f,
            outOfRange_statusFlags = BacnetBitString.Parse("1010"),
            outOfRange_deadband = 0.5f,
            outOfRange_exceededLimit = 100.0f,
        };
    }

    #endregion

    #region EventNotification 编解码 (EVT-1/2)

    [Fact]
    [System.ComponentModel.DisplayName("EncodeEventNotifyUnconfirmed → DecodeEventNotifyData 往返")]
    public void EventNotifyUnconfirmed_RoundTrip()
    {
        var data = CreateTestEventData();

        var buf = new EncodeBuffer();
        Services.EncodeEventNotifyUnconfirmed(buf, data);

        var len = Services.DecodeEventNotifyData(buf.buffer, 0, buf.offset, out var decoded);

        Assert.True(len > 0);
        Assert.Equal(data.processIdentifier, decoded.processIdentifier);
        Assert.Equal(data.initiatingObjectIdentifier.type, decoded.initiatingObjectIdentifier.type);
        Assert.Equal(data.initiatingObjectIdentifier.instance, decoded.initiatingObjectIdentifier.instance);
        Assert.Equal(data.eventObjectIdentifier.type, decoded.eventObjectIdentifier.type);
        Assert.Equal(data.eventObjectIdentifier.instance, decoded.eventObjectIdentifier.instance);
        Assert.Equal(data.notificationClass, decoded.notificationClass);
        Assert.Equal(data.priority, decoded.priority);
        Assert.Equal(data.eventType, decoded.eventType);
        Assert.Equal(data.messageText, decoded.messageText);
        Assert.Equal(data.notifyType, decoded.notifyType);
        Assert.Equal(data.ackRequired, decoded.ackRequired);
        Assert.Equal(data.fromState, decoded.fromState);
        Assert.Equal(data.toState, decoded.toState);
    }

    [Fact]
    [System.ComponentModel.DisplayName("EncodeEventNotifyConfirmed → DecodeEventNotifyData 往返（有确认）")]
    public void EventNotifyConfirmed_RoundTrip()
    {
        var data = CreateTestEventData();

        var buf = new EncodeBuffer();
        Services.EncodeEventNotifyConfirmed(buf, data);

        var len = Services.DecodeEventNotifyData(buf.buffer, 0, buf.offset, out var decoded);

        Assert.True(len > 0);
        Assert.Equal(data.processIdentifier, decoded.processIdentifier);
        Assert.Equal(data.notificationClass, decoded.notificationClass);
        Assert.Equal(data.eventType, decoded.eventType);
    }

    [Fact]
    [System.ComponentModel.DisplayName("EventNotification 带可选字段（messageText 为空）往返")]
    public void EventNotify_OptionalFields_RoundTrip()
    {
        var data = CreateTestEventData();
        data.messageText = null;

        var buf = new EncodeBuffer();
        Services.EncodeEventNotifyUnconfirmed(buf, data);

        var len = Services.DecodeEventNotifyData(buf.buffer, 0, buf.offset, out var decoded);

        Assert.True(len > 0);
        Assert.Null(decoded.messageText);
    }

    [Fact]
    [System.ComponentModel.DisplayName("EventNotification EVENT_FLOATING_LIMIT 类型往返")]
    public void EventNotify_FloatingLimit_RoundTrip()
    {
        var data = CreateTestEventData();
        data.eventType = BacnetEventTypes.EVENT_FLOATING_LIMIT;
        data.notifyType = BacnetNotifyTypes.NOTIFY_EVENT;
        data.floatingLimit_referenceValue = 50.0f;
        data.floatingLimit_statusFlags = BacnetBitString.Parse("0000");
        data.floatingLimit_setPointValue = 100.0f;
        data.floatingLimit_errorLimit = 5.0f;

        var buf = new EncodeBuffer();
        Services.EncodeEventNotifyUnconfirmed(buf, data);

        var len = Services.DecodeEventNotifyData(buf.buffer, 0, buf.offset, out var decoded);

        Assert.True(len > 0);
        Assert.Equal(data.eventType, decoded.eventType);
        Assert.Equal(data.notifyType, decoded.notifyType);
    }

    [Fact]
    [System.ComponentModel.DisplayName("EventNotification EVENT_UNSIGNED_RANGE 类型往返")]
    public void EventNotify_UnsignedRange_RoundTrip()
    {
        var data = CreateTestEventData();
        data.eventType = BacnetEventTypes.EVENT_UNSIGNED_RANGE;
        data.notifyType = BacnetNotifyTypes.NOTIFY_EVENT;
        data.unsignedRange_exceedingValue = 200u;
        data.unsignedRange_statusFlags = BacnetBitString.Parse("0000");
        data.unsignedRange_exceededLimit = 100u;

        var buf = new EncodeBuffer();
        Services.EncodeEventNotifyUnconfirmed(buf, data);

        var len = Services.DecodeEventNotifyData(buf.buffer, 0, buf.offset, out var decoded);

        Assert.True(len > 0);
        Assert.Equal(data.eventType, decoded.eventType);
        Assert.Equal(data.notifyType, decoded.notifyType);
    }

    [Fact]
    [System.ComponentModel.DisplayName("EventNotification NOTIFY_ACK_NOTIFICATION 类型（无事件值）往返")]
    public void EventNotify_AckNotification_RoundTrip()
    {
        var data = CreateTestEventData();
        data.notifyType = BacnetNotifyTypes.NOTIFY_ACK_NOTIFICATION;

        var buf = new EncodeBuffer();
        Services.EncodeEventNotifyUnconfirmed(buf, data);

        var len = Services.DecodeEventNotifyData(buf.buffer, 0, buf.offset, out var decoded);

        Assert.True(len > 0);
        Assert.Equal(data.notifyType, decoded.notifyType);
    }

    #endregion

    #region GetAlarmSummary (EVT-3)

    [Fact]
    [System.ComponentModel.DisplayName("EncodeAlarmSummary 编码正确")]
    public void EncodeAlarmSummary_EncodesCorrectly()
    {
        var objId = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 1);
        var alarmState = 1u;
        var ackTransitions = BacnetBitString.Parse("101");

        var buf = new EncodeBuffer();
        Services.EncodeAlarmSummary(buf, objId, alarmState, ackTransitions);

        // 至少编码了 ObjectId + Enumerated + BitString
        Assert.True(buf.offset > 0);
    }

    [Fact]
    [System.ComponentModel.DisplayName("DecodeAlarmSummaryOrEvent 解码 GetAlarmSummary 响应")]
    public void DecodeAlarmSummary_DecodesCorrectly()
    {
        var objId = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 1);
        var alarmState = 1u;
        var ackTransitions = BacnetBitString.Parse("101");

        var buf = new EncodeBuffer();
        Services.EncodeAlarmSummary(buf, objId, alarmState, ackTransitions);

        IList<BacnetGetEventInformationData> alarms = new List<BacnetGetEventInformationData>();
        var len = Services.DecodeAlarmSummaryOrEvent(buf.buffer, 0, buf.offset, false, ref alarms, out var moreEvent);

        Assert.True(len > 0);
        Assert.NotEmpty(alarms);
        Assert.Equal(objId.type, alarms[0].objectIdentifier.type);
        Assert.Equal(objId.instance, alarms[0].objectIdentifier.instance);
        Assert.Equal((BacnetEventStates)alarmState, alarms[0].eventState);
        Assert.False(moreEvent);
    }

    #endregion

    #region GetEventInformation (EVT-4)

    [Fact]
    [System.ComponentModel.DisplayName("EncodeGetEventInformation 编码（无 lastReceived）")]
    public void EncodeGetEventInformation_NoLastReceived()
    {
        var buf = new EncodeBuffer();
        Services.EncodeGetEventInformation(buf, false, default);

        // 无参数时不应写入任何字节
        Assert.Equal(0, buf.offset);
    }

    [Fact]
    [System.ComponentModel.DisplayName("EncodeGetEventInformation 编码（有 lastReceived）")]
    public void EncodeGetEventInformation_WithLastReceived()
    {
        var buf = new EncodeBuffer();
        var lastId = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 10);
        Services.EncodeGetEventInformation(buf, true, lastId);

        Assert.True(buf.offset > 0);
    }

    [Fact]
    [System.ComponentModel.DisplayName("EncodeGetEventInformationAcknowledge → DecodeAlarmSummaryOrEvent 往返")]
    public void GetEventInformationAck_RoundTrip()
    {
        var events = new[]
        {
            new BacnetGetEventInformationData
            {
                objectIdentifier = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 1),
                eventState = BacnetEventStates.EVENT_STATE_HIGH_LIMIT,
                acknowledgedTransitions = BacnetBitString.Parse("100"),
                eventTimeStamps = new[]
                {
                    new BacnetGenericTime(new DateTime(2025, 1, 1, 10, 0, 0), BacnetTimestampTags.TIME_STAMP_DATETIME),
                    new BacnetGenericTime(new DateTime(2025, 1, 1, 10, 5, 0), BacnetTimestampTags.TIME_STAMP_DATETIME),
                    new BacnetGenericTime(new DateTime(2025, 1, 1, 10, 10, 0), BacnetTimestampTags.TIME_STAMP_DATETIME),
                },
                notifyType = BacnetNotifyTypes.NOTIFY_ALARM,
                eventEnable = BacnetBitString.Parse("111"),
                eventPriorities = new[] { 1u, 2u, 3u },
            }
        };

        var buf = new EncodeBuffer();
        Services.EncodeGetEventInformationAcknowledge(buf, events, false);

        IList<BacnetGetEventInformationData> decoded = new List<BacnetGetEventInformationData>();
        var len = Services.DecodeAlarmSummaryOrEvent(buf.buffer, 0, buf.offset, true, ref decoded, out var moreEvent);

        Assert.True(len > 0);
        Assert.NotEmpty(decoded);
        Assert.Equal(events[0].objectIdentifier.type, decoded[0].objectIdentifier.type);
        Assert.Equal(events[0].objectIdentifier.instance, decoded[0].objectIdentifier.instance);
        Assert.Equal(events[0].eventState, decoded[0].eventState);
        Assert.Equal(events[0].notifyType, decoded[0].notifyType);
        Assert.False(moreEvent);
    }

    [Fact]
    [System.ComponentModel.DisplayName("GetEventInformationAcknowledge moreEvents=true 解码")]
    public void GetEventInformationAck_MoreEvents()
    {
        var events = new[]
        {
            new BacnetGetEventInformationData
            {
                objectIdentifier = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 1),
                eventState = BacnetEventStates.EVENT_STATE_NORMAL,
                acknowledgedTransitions = BacnetBitString.Parse("000"),
                eventTimeStamps = new BacnetGenericTime[3],
                notifyType = BacnetNotifyTypes.NOTIFY_EVENT,
                eventEnable = BacnetBitString.Parse("111"),
                eventPriorities = new[] { 1u, 1u, 1u },
            }
        };

        var buf = new EncodeBuffer();
        Services.EncodeGetEventInformationAcknowledge(buf, events, true);

        IList<BacnetGetEventInformationData> decoded = new List<BacnetGetEventInformationData>();
        Services.DecodeAlarmSummaryOrEvent(buf.buffer, 0, buf.offset, true, ref decoded, out var moreEvent);

        Assert.True(moreEvent);
    }

    #endregion

    #region AcknowledgeAlarm (EVT-5)

    [Fact]
    [System.ComponentModel.DisplayName("EncodeAlarmAcknowledge → DecodeAlarmAcknowledge 往返")]
    public void AlarmAcknowledge_RoundTrip()
    {
        var processId = 123u;
        var objId = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 5);
        var eventStateAcked = 1u;
        var ackSource = "Operator Console";
        var eventTime = new BacnetGenericTime(new DateTime(2025, 6, 15, 14, 30, 0), BacnetTimestampTags.TIME_STAMP_DATETIME);
        var ackTime = new BacnetGenericTime(new DateTime(2025, 6, 15, 14, 35, 0), BacnetTimestampTags.TIME_STAMP_DATETIME);

        var buf = new EncodeBuffer();
        Services.EncodeAlarmAcknowledge(buf, processId, objId, eventStateAcked, ackSource, eventTime, ackTime);

        var len = Services.DecodeAlarmAcknowledge(buf.buffer, 0, buf.offset,
            out var decodedProcessId,
            out var decodedObjId,
            out var decodedEventStateAcked,
            out var decodedAckSource,
            out var decodedEventTime,
            out var decodedAckTime);

        Assert.True(len > 0);
        Assert.Equal(processId, decodedProcessId);
        Assert.Equal(objId.type, decodedObjId.type);
        Assert.Equal(objId.instance, decodedObjId.instance);
        Assert.Equal(eventStateAcked, decodedEventStateAcked);
        Assert.Equal(ackSource, decodedAckSource);
    }

    #endregion
}
