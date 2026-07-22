using System.IO.BACnet.Serialize;

namespace System.IO.BACnet;

/// <summary>BACnetSpecialEvent - 特殊事件，包含日历条目、时间值列表和优先级。</summary>
public struct BacnetSpecialEvent : ASN1.IEncode, ASN1.IDecode
{
    /// <summary>日历条目</summary>
    public BACnetCalendarEntry CalendarEntry { get; set; }
    /// <summary>时间值列表</summary>
    public List<BacnetTimeValue> ListOfTimeValues { get; set; }
    /// <summary>事件优先级</summary>
    public UInt32 EventPriority { get; set; }
    /// <summary>事件类型（可选）</summary>
    public UInt32? EventType { get; set; }

    public void Encode(EncodeBuffer buffer)
    {
        /* Tag 0: calendarEntry */
        ASN1.encode_opening_tag(buffer, 0);
        CalendarEntry.Encode(buffer);
        ASN1.encode_closing_tag(buffer, 0);

        /* Tag 1: listOfTimeValues */
        ASN1.encode_opening_tag(buffer, 1);
        if (ListOfTimeValues != null)
        {
            foreach (var tv in ListOfTimeValues)
                tv.Encode(buffer);
        }
        ASN1.encode_closing_tag(buffer, 1);

        /* Tag 2: eventPriority */
        ASN1.encode_context_unsigned(buffer, 2, EventPriority);

        /* Tag 3: eventType (optional) */
        if (EventType.HasValue)
            ASN1.encode_context_enumerated(buffer, 3, EventType.Value);
    }

    public int Decode(byte[] buffer, int offset, uint count)
    {
        var len = 0;

        /* Tag 0: calendarEntry (opening) */
        if (!ASN1.decode_is_context_tag(buffer, offset + len, 0))
            return -1;
        len++; // opening tag

        // Manually decode calendar entries until closing tag 0
        CalendarEntry = new BACnetCalendarEntry
        {
            Entries = new List<object>()
        };
        while (len < count && !ASN1.IS_CLOSING_TAG(buffer[offset + len]))
        {
            var tagLen = ASN1.decode_tag_number(buffer, offset + len, out var entryTag);
            switch (entryTag)
            {
                case 0:
                    var bdt = new BacnetDate();
                    tagLen += bdt.Decode(buffer, offset + len + tagLen, count - (uint)(len + tagLen));
                    CalendarEntry.Entries.Add(bdt);
                    len += tagLen;
                    break;
                case 1:
                    var bdr = new BacnetDateRange();
                    tagLen += bdr.Decode(buffer, offset + len + tagLen, count - (uint)(len + tagLen));
                    CalendarEntry.Entries.Add(bdr);
                    len += tagLen;
                    len++; // closing tag
                    break;
                case 2:
                    var bwd = new BacnetweekNDay();
                    tagLen += bwd.Decode(buffer, offset + len + tagLen, count - (uint)(len + tagLen));
                    CalendarEntry.Entries.Add(bwd);
                    len += tagLen;
                    break;
                default:
                    return -1;
            }
        }
        if (len < count && ASN1.decode_is_closing_tag_number(buffer, offset + len, 0))
            len++; // closing tag
        else
            return -1;

        /* Tag 1: listOfTimeValues */
        if (!ASN1.decode_is_context_tag(buffer, offset + len, 1))
            return -1;
        len++; // opening tag
        ListOfTimeValues = new List<BacnetTimeValue>();
        while (len < count && !ASN1.IS_CLOSING_TAG(buffer[offset + len]))
        {
            var tv = new BacnetTimeValue();
            var consumed = tv.Decode(buffer, offset + len, count - (uint)len);
            if (consumed <= 0) break;
            ListOfTimeValues.Add(tv);
            len += consumed;
        }
        if (len < count && ASN1.decode_is_closing_tag_number(buffer, offset + len, 1))
            len++; // closing tag

        /* Tag 2: eventPriority */
        if (!ASN1.decode_is_context_tag(buffer, offset + len, 2))
            return -1;
        len += ASN1.decode_tag_number_and_value(buffer, offset + len, out _, out var lenValue);
        len += ASN1.decode_unsigned(buffer, offset + len, lenValue, out var eventPriority);
        EventPriority = eventPriority;

        /* Tag 3: eventType (optional) */
        if (len < count && ASN1.decode_is_context_tag(buffer, offset + len, 3))
        {
            len += ASN1.decode_tag_number_and_value(buffer, offset + len, out _, out lenValue);
            len += ASN1.decode_enumerated(buffer, offset + len, lenValue, out var eventType);
            EventType = eventType;
        }

        return len;
    }
}
