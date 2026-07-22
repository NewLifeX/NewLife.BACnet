using System.IO.BACnet.Serialize;

namespace System.IO.BACnet;

/// <summary>BACnetDailySchedule - 每日日程表，由一系列 BACnetTimeValue 对组成。</summary>
public struct BacnetDailySchedule : ASN1.IEncode, ASN1.IDecode
{
    /// <summary>时间-值对列表</summary>
    public List<BacnetTimeValue> Values { get; set; }

    public void Encode(EncodeBuffer buffer)
    {
        if (Values == null) return;

        foreach (var tv in Values)
        {
            tv.Encode(buffer);
        }
    }

    public int Decode(byte[] buffer, int offset, uint count)
    {
        var len = 0;
        Values = new List<BacnetTimeValue>();

        while (len < count)
        {
            var tv = new BacnetTimeValue();
            var consumed = tv.Decode(buffer, offset + len, count - (uint)len);
            if (consumed <= 0) break;
            Values.Add(tv);
            len += consumed;

            // 检查是否到达 closing tag
            if (len < count && ASN1.IS_CLOSING_TAG(buffer[offset + len]))
                break;
        }

        return len;
    }
}

/// <summary>BACnetTimeValue - 时间-值对，用于每日日程表。</summary>
public struct BacnetTimeValue : ASN1.IEncode, ASN1.IDecode
{
    /// <summary>时间</summary>
    public DateTime Time { get; set; }
    /// <summary>值</summary>
    public BacnetValue Value { get; set; }

    public void Encode(EncodeBuffer buffer)
    {
        /* Tag 0: Time */
        ASN1.encode_context_time(buffer, 0, Time);
        /* Tag 1: Value (abstract syntax) */
        ASN1.encode_opening_tag(buffer, 1);
        ASN1.bacapp_encode_application_data(buffer, Value);
        ASN1.encode_closing_tag(buffer, 1);
    }

    public int Decode(byte[] buffer, int offset, uint count)
    {
        var len = 0;

        /* Tag 0: Time */
        if (!ASN1.decode_is_context_tag(buffer, offset + len, 0))
            return -1;
        len += ASN1.decode_tag_number_and_value(buffer, offset + len, out _, out var lenValue);
        len += ASN1.decode_bacnet_time(buffer, offset + len, out var time);
        Time = time;

        /* Tag 1: Value */
        if (!ASN1.decode_is_context_tag(buffer, offset + len, 1))
            return -1;
        len++; // opening tag
        len += ASN1.bacapp_decode_application_data(default, buffer, offset + len, (int)(count - (uint)len),
            BacnetObjectTypes.MAX_BACNET_OBJECT_TYPE, BacnetPropertyIds.MAX_BACNET_PROPERTY_ID, out var decodedValue);
        Value = decodedValue;
        if (len < count && ASN1.decode_is_closing_tag_number(buffer, offset + len, 1))
            len++; // closing tag

        return len;
    }
}
