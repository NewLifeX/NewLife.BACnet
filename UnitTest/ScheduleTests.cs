using System;
using System.Collections.Generic;
using System.IO.BACnet;
using System.IO.BACnet.Serialize;
using Xunit;

namespace UnitTest;

/// <summary>Schedule/Calendar（Phase 5）数据类型与编解码单元测试。
/// 测试 BacnetDailySchedule、BacnetSpecialEvent、BacnetTimeValue 的 encode→decode 往返一致性。</summary>
public class ScheduleTests
{
    #region BacnetTimeValue

    [Fact]
    [System.ComponentModel.DisplayName("BacnetTimeValue 编码→解码往返")]
    public void TimeValue_RoundTrip()
    {
        var tv = new BacnetTimeValue
        {
            Time = new DateTime(1, 1, 1, 8, 30, 0),
            Value = new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, 25.5f),
        };

        var buf = new EncodeBuffer();
        tv.Encode(buf);

        var decoded = new BacnetTimeValue();
        var consumed = decoded.Decode(buf.buffer, 0, (UInt32)buf.offset);

        Assert.True(consumed > 0);
        Assert.Equal(tv.Time.Hour, decoded.Time.Hour);
        Assert.Equal(tv.Time.Minute, decoded.Time.Minute);
        Assert.Equal(tv.Time.Second, decoded.Time.Second);
        Assert.Equal(25.5f, (Single)decoded.Value.Value);
    }

    [Fact]
    [System.ComponentModel.DisplayName("BacnetTimeValue 整数数值编码→解码往返")]
    public void TimeValue_IntegerValue_RoundTrip()
    {
        var tv = new BacnetTimeValue
        {
            Time = new DateTime(1, 1, 1, 0, 0, 0),
            Value = new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_UNSIGNED_INT, 100u),
        };

        var buf = new EncodeBuffer();
        tv.Encode(buf);

        var decoded = new BacnetTimeValue();
        var consumed = decoded.Decode(buf.buffer, 0, (UInt32)buf.offset);

        Assert.True(consumed > 0);
        Assert.Equal(0, decoded.Time.Hour);
        Assert.Equal(100u, (UInt32)decoded.Value.Value);
    }

    #endregion

    #region BacnetDailySchedule

    [Fact]
    [System.ComponentModel.DisplayName("BacnetDailySchedule 编码→解码往返")]
    public void DailySchedule_RoundTrip()
    {
        var schedule = new BacnetDailySchedule
        {
            Values = new List<BacnetTimeValue>
            {
                new()
                {
                    Time = new DateTime(1, 1, 1, 8, 0, 0),
                    Value = new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, 22.0f),
                },
                new()
                {
                    Time = new DateTime(1, 1, 1, 18, 0, 0),
                    Value = new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, 26.0f),
                },
            },
        };

        var buf = new EncodeBuffer();
        schedule.Encode(buf);

        var decoded = new BacnetDailySchedule();
        var consumed = decoded.Decode(buf.buffer, 0, (UInt32)buf.offset);

        Assert.True(consumed > 0);
        Assert.NotNull(decoded.Values);
        Assert.Equal(2, decoded.Values.Count);
        Assert.Equal(8, decoded.Values[0].Time.Hour);
        Assert.Equal(22.0f, (Single)decoded.Values[0].Value.Value);
        Assert.Equal(18, decoded.Values[1].Time.Hour);
        Assert.Equal(26.0f, (Single)decoded.Values[1].Value.Value);
    }

    [Fact]
    [System.ComponentModel.DisplayName("BacnetDailySchedule 空日程表编码→解码")]
    public void DailySchedule_Empty_RoundTrip()
    {
        var schedule = new BacnetDailySchedule
        {
            Values = new List<BacnetTimeValue>(),
        };

        var buf = new EncodeBuffer();
        schedule.Encode(buf);

        var decoded = new BacnetDailySchedule();
        var consumed = decoded.Decode(buf.buffer, 0, (UInt32)buf.offset);

        Assert.True(consumed >= 0);
        Assert.NotNull(decoded.Values);
        Assert.Empty(decoded.Values);
    }

    #endregion

    #region BacnetSpecialEvent

    [Fact]
    [System.ComponentModel.DisplayName("BacnetSpecialEvent 编码→解码往返（含 EventType）")]
    public void SpecialEvent_WithEventType_RoundTrip()
    {
        var calEntry = new BACnetCalendarEntry
        {
            Entries = new List<object>
            {
                new BacnetDate(125, 12, 25), // 2025-12-25
            },
        };

        var ev = new BacnetSpecialEvent
        {
            CalendarEntry = calEntry,
            ListOfTimeValues = new List<BacnetTimeValue>
            {
                new()
                {
                    Time = new DateTime(1, 1, 1, 9, 0, 0),
                    Value = new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, 30.0f),
                },
            },
            EventPriority = 10,
            EventType = 0, // BACnetSpecialEventType.STATUS_ACTIVE
        };

        var buf = new EncodeBuffer();
        ev.Encode(buf);

        var decoded = new BacnetSpecialEvent();
        var consumed = decoded.Decode(buf.buffer, 0, (UInt32)buf.offset);

        Assert.True(consumed > 0);
        Assert.NotNull(decoded.CalendarEntry.Entries);
        Assert.Single(decoded.CalendarEntry.Entries);
        Assert.NotNull(decoded.ListOfTimeValues);
        Assert.Single(decoded.ListOfTimeValues);
        Assert.Equal(10u, decoded.EventPriority);
        Assert.True(decoded.EventType.HasValue);
        Assert.Equal(0u, decoded.EventType.Value);
    }

    [Fact]
    [System.ComponentModel.DisplayName("BacnetSpecialEvent 编码→解码往返（不含 EventType）")]
    public void SpecialEvent_WithoutEventType_RoundTrip()
    {
        var ev = new BacnetSpecialEvent
        {
            CalendarEntry = new BACnetCalendarEntry(),
            ListOfTimeValues = new List<BacnetTimeValue>(),
            EventPriority = 5,
            EventType = null,
        };

        var buf = new EncodeBuffer();
        ev.Encode(buf);

        var decoded = new BacnetSpecialEvent();
        var consumed = decoded.Decode(buf.buffer, 0, (UInt32)buf.offset);

        Assert.True(consumed > 0);
        Assert.Equal(5u, decoded.EventPriority);
        Assert.False(decoded.EventType.HasValue);
    }

    #endregion

    #region ASN1 编解码集成

    [Fact]
    [System.ComponentModel.DisplayName("BacnetDailySchedule 通过 ASN1 上下文编码→解码")]
    public void DailySchedule_ThroughASN1_ContextDecode()
    {
        var schedule = new BacnetDailySchedule
        {
            Values = new List<BacnetTimeValue>
            {
                new()
                {
                    Time = new DateTime(1, 1, 1, 12, 0, 0),
                    Value = new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, 20.0f),
                },
            },
        };

        // 模拟 BACnet 上下文编码：opening tag + data + closing tag
        var buf = new EncodeBuffer();
        ASN1.encode_opening_tag(buf, 0);
        schedule.Encode(buf);
        ASN1.encode_closing_tag(buf, 0);

        // 通过 bacapp_decode_context_application_data 解码
        // 先读取 opening tag
        var offset = 0;
        offset += ASN1.decode_tag_number(buf.buffer, offset, out _);

        var len = ASN1.bacapp_decode_context_application_data(
            default, buf.buffer, offset, buf.offset,
            BacnetObjectTypes.OBJECT_SCHEDULE,
            BacnetPropertyIds.PROP_WEEKLY_SCHEDULE,
            out var decodedValue);

        Assert.True(len > 0);
        Assert.Equal(BacnetApplicationTags.BACNET_APPLICATION_TAG_WEEKLY_SCHEDULE, decodedValue.Tag);
        Assert.IsType<BacnetDailySchedule>(decodedValue.Value);
        var decodedSchedule = (BacnetDailySchedule)decodedValue.Value;
        Assert.Single(decodedSchedule.Values);
        Assert.Equal(20.0f, (Single)decodedSchedule.Values[0].Value.Value);
    }

    [Fact]
    [System.ComponentModel.DisplayName("BacnetSpecialEvent 通过 ASN1 上下文编码→解码")]
    public void SpecialEvent_ThroughASN1_ContextDecode()
    {
        var ev = new BacnetSpecialEvent
        {
            CalendarEntry = new BACnetCalendarEntry
            {
                Entries = new List<object>
                {
                    new BacnetDate(125, 6, 15),
                },
            },
            ListOfTimeValues = new List<BacnetTimeValue>
            {
                new()
                {
                    Time = new DateTime(1, 1, 1, 10, 0, 0),
                    Value = new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_UNSIGNED_INT, 1u),
                },
            },
            EventPriority = 8,
            EventType = null,
        };

        var buf = new EncodeBuffer();
        ASN1.encode_opening_tag(buf, 0);
        ev.Encode(buf);
        ASN1.encode_closing_tag(buf, 0);

        var offset = 0;
        offset += ASN1.decode_tag_number(buf.buffer, offset, out _);

        var len = ASN1.bacapp_decode_context_application_data(
            default, buf.buffer, offset, buf.offset,
            BacnetObjectTypes.OBJECT_SCHEDULE,
            BacnetPropertyIds.PROP_EXCEPTION_SCHEDULE,
            out var decodedValue);

        Assert.True(len > 0);
        Assert.Equal(BacnetApplicationTags.BACNET_APPLICATION_TAG_SPECIAL_EVENT, decodedValue.Tag);
        Assert.IsType<BacnetSpecialEvent>(decodedValue.Value);
        var decodedEv = (BacnetSpecialEvent)decodedValue.Value;
        Assert.Equal(8u, decodedEv.EventPriority);
        Assert.NotNull(decodedEv.CalendarEntry.Entries);
        Assert.Single(decodedEv.CalendarEntry.Entries);
    }

    [Fact]
    [System.ComponentModel.DisplayName("BacnetDailySchedule 通过 SCHEDULE_DEFAULT 属性解码")]
    public void DailySchedule_ThroughScheduleDefault_ContextDecode()
    {
        var schedule = new BacnetDailySchedule
        {
            Values = new List<BacnetTimeValue>
            {
                new()
                {
                    Time = new DateTime(1, 1, 1, 0, 0, 0),
                    Value = new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_NULL, null),
                },
            },
        };

        var buf = new EncodeBuffer();
        ASN1.encode_opening_tag(buf, 0);
        schedule.Encode(buf);
        ASN1.encode_closing_tag(buf, 0);

        var offset = 0;
        offset += ASN1.decode_tag_number(buf.buffer, offset, out _);

        var len = ASN1.bacapp_decode_context_application_data(
            default, buf.buffer, offset, buf.offset,
            BacnetObjectTypes.OBJECT_SCHEDULE,
            BacnetPropertyIds.PROP_SCHEDULE_DEFAULT,
            out var decodedValue);

        Assert.True(len > 0);
        Assert.Equal(BacnetApplicationTags.BACNET_APPLICATION_TAG_WEEKLY_SCHEDULE, decodedValue.Tag);
        Assert.IsType<BacnetDailySchedule>(decodedValue.Value);
    }

    #endregion
}
