using System;
using System.IO.BACnet;
using System.IO.BACnet.Serialize;
using Xunit;

namespace UnitTest;

/// <summary>ASN1 编解码单元测试。完全无网络依赖，验证 encode→decode 往返一致性。</summary>
public class ASN1Tests
{
    #region 辅助
    /// <summary>对 buffer 前 maxLen 字节进行解码，返回标签号、值长度和偏移量</summary>
    private static (Byte tagNumber, UInt32 lenValue, Int32 consumed) DecodeTag(Byte[] buf, Int32 offset = 0)
    {
        var consumed = ASN1.decode_tag_number_and_value(buf, offset, out Byte tagNumber, out UInt32 lenValue);
        return (tagNumber, lenValue, consumed);
    }
    #endregion

    #region 布尔值
    [Fact]
    [System.ComponentModel.DisplayName("encode_application_boolean true → decode_tag + 值为 1")]
    public void Boolean_Encode_True()
    {
        var buf = new EncodeBuffer();
        ASN1.encode_application_boolean(buf, true);
        Assert.True(buf.offset > 0);
        var (tagNumber, lenValue, _) = DecodeTag(buf.buffer);
        Assert.Equal((Byte)BacnetApplicationTags.BACNET_APPLICATION_TAG_BOOLEAN, tagNumber);
        // Boolean 编码：lenValue 本身即布尔值（1=true，0=false）
        Assert.Equal(1u, lenValue);
    }

    [Fact]
    [System.ComponentModel.DisplayName("encode_application_boolean false → decode_tag lenValue=0")]
    public void Boolean_Encode_False()
    {
        var buf = new EncodeBuffer();
        ASN1.encode_application_boolean(buf, false);
        var (_, lenValue, _) = DecodeTag(buf.buffer);
        Assert.Equal(0u, lenValue);
    }
    #endregion

    #region 无符号整数
    [Fact]
    [System.ComponentModel.DisplayName("encode_application_unsigned 小值单字节往返")]
    public void Unsigned_SmallValue_RoundTrip()
    {
        var buf = new EncodeBuffer();
        ASN1.encode_application_unsigned(buf, 42u);
        var (tagNumber, lenValue, tagLen) = DecodeTag(buf.buffer);
        Assert.Equal((Byte)BacnetApplicationTags.BACNET_APPLICATION_TAG_UNSIGNED_INT, tagNumber);
        var consumed = ASN1.decode_unsigned(buf.buffer, tagLen, lenValue, out var decoded);
        Assert.Equal(42u, decoded);
    }

    [Fact]
    [System.ComponentModel.DisplayName("encode_application_unsigned 大值 65536 往返")]
    public void Unsigned_LargeValue_RoundTrip()
    {
        var buf = new EncodeBuffer();
        ASN1.encode_application_unsigned(buf, 65536u);
        var (tagNumber, lenValue, tagLen) = DecodeTag(buf.buffer);
        Assert.Equal((Byte)BacnetApplicationTags.BACNET_APPLICATION_TAG_UNSIGNED_INT, tagNumber);
        ASN1.decode_unsigned(buf.buffer, tagLen, lenValue, out var decoded);
        Assert.Equal(65536u, decoded);
    }

    [Fact]
    [System.ComponentModel.DisplayName("encode_application_unsigned 零值往返")]
    public void Unsigned_Zero_RoundTrip()
    {
        var buf = new EncodeBuffer();
        ASN1.encode_application_unsigned(buf, 0u);
        var (_, lenValue, tagLen) = DecodeTag(buf.buffer);
        ASN1.decode_unsigned(buf.buffer, tagLen, lenValue, out var decoded);
        Assert.Equal(0u, decoded);
    }
    #endregion

    #region 有符号整数
    [Fact]
    [System.ComponentModel.DisplayName("encode_application_signed 正数往返")]
    public void Signed_Positive_RoundTrip()
    {
        var buf = new EncodeBuffer();
        ASN1.encode_application_signed(buf, 100);
        var (tagNumber, lenValue, tagLen) = DecodeTag(buf.buffer);
        Assert.Equal((Byte)BacnetApplicationTags.BACNET_APPLICATION_TAG_SIGNED_INT, tagNumber);
        ASN1.decode_signed(buf.buffer, tagLen, lenValue, out var decoded);
        Assert.Equal(100, decoded);
    }

    [Fact]
    [System.ComponentModel.DisplayName("encode_application_signed 负数往返")]
    public void Signed_Negative_RoundTrip()
    {
        var buf = new EncodeBuffer();
        ASN1.encode_application_signed(buf, -50);
        var (_, lenValue, tagLen) = DecodeTag(buf.buffer);
        ASN1.decode_signed(buf.buffer, tagLen, lenValue, out var decoded);
        Assert.Equal(-50, decoded);
    }
    #endregion

    #region 单精度浮点
    [Fact]
    [System.ComponentModel.DisplayName("encode_application_real 浮点往返精度正确")]
    public void Real_RoundTrip()
    {
        var buf = new EncodeBuffer();
        ASN1.encode_application_real(buf, 3.14f);
        var (tagNumber, lenValue, tagLen) = DecodeTag(buf.buffer);
        Assert.Equal((Byte)BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, tagNumber);
        Assert.Equal(4u, lenValue);
        ASN1.decode_real(buf.buffer, tagLen, out var decoded);
        Assert.Equal(3.14f, decoded);
    }

    [Fact]
    [System.ComponentModel.DisplayName("encode_application_real 零值往返")]
    public void Real_Zero_RoundTrip()
    {
        var buf = new EncodeBuffer();
        ASN1.encode_application_real(buf, 0f);
        var (_, _, tagLen) = DecodeTag(buf.buffer);
        ASN1.decode_real(buf.buffer, tagLen, out var decoded);
        Assert.Equal(0f, decoded);
    }

    [Fact]
    [System.ComponentModel.DisplayName("encode_application_real 负值往返")]
    public void Real_Negative_RoundTrip()
    {
        var buf = new EncodeBuffer();
        ASN1.encode_application_real(buf, -273.15f);
        var (_, _, tagLen) = DecodeTag(buf.buffer);
        ASN1.decode_real(buf.buffer, tagLen, out var decoded);
        Assert.Equal(-273.15f, decoded, precision: 3);
    }
    #endregion

    #region 双精度浮点
    [Fact]
    [System.ComponentModel.DisplayName("encode_application_double 双精度往返")]
    public void Double_RoundTrip()
    {
        var buf = new EncodeBuffer();
        ASN1.encode_application_double(buf, 3.141592653589793);
        var (tagNumber, lenValue, tagLen) = DecodeTag(buf.buffer);
        Assert.Equal((Byte)BacnetApplicationTags.BACNET_APPLICATION_TAG_DOUBLE, tagNumber);
        Assert.Equal(8u, lenValue);
        ASN1.decode_double(buf.buffer, tagLen, out var decoded);
        Assert.Equal(3.141592653589793, decoded);
    }
    #endregion

    #region 字符串
    [Fact]
    [System.ComponentModel.DisplayName("encode_application_character_string ASCII 字符串往返")]
    public void CharacterString_Ascii_RoundTrip()
    {
        var buf = new EncodeBuffer();
        ASN1.encode_application_character_string(buf, "Hello");
        var (tagNumber, lenValue, tagLen) = DecodeTag(buf.buffer);
        Assert.Equal((Byte)BacnetApplicationTags.BACNET_APPLICATION_TAG_CHARACTER_STRING, tagNumber);
        var len = ASN1.decode_character_string(buf.buffer, tagLen, buf.offset - tagLen, lenValue, out var decoded);
        Assert.True(len > 0);
        Assert.Equal("Hello", decoded);
    }

    [Fact]
    [System.ComponentModel.DisplayName("encode_application_character_string 中文字符串往返")]
    public void CharacterString_Chinese_RoundTrip()
    {
        var buf = new EncodeBuffer();
        ASN1.encode_application_character_string(buf, "楼宇自控");
        var (_, lenValue, tagLen) = DecodeTag(buf.buffer);
        ASN1.decode_character_string(buf.buffer, tagLen, buf.offset - tagLen, lenValue, out var decoded);
        Assert.Equal("楼宇自控", decoded);
    }

    [Fact]
    [System.ComponentModel.DisplayName("encode_application_character_string 空字符串往返")]
    public void CharacterString_Empty_RoundTrip()
    {
        var buf = new EncodeBuffer();
        ASN1.encode_application_character_string(buf, "");
        var (_, lenValue, tagLen) = DecodeTag(buf.buffer);
        ASN1.decode_character_string(buf.buffer, tagLen, buf.offset - tagLen, lenValue, out var decoded);
        Assert.Equal("", decoded);
    }
    #endregion

    #region 对象标识符
    [Fact]
    [System.ComponentModel.DisplayName("encode_application_object_id ObjectId 往返")]
    public void ObjectId_RoundTrip()
    {
        var buf = new EncodeBuffer();
        ASN1.encode_application_object_id(buf, BacnetObjectTypes.OBJECT_ANALOG_VALUE, 5);
        var (tagNumber, _, tagLen) = DecodeTag(buf.buffer);
        Assert.Equal((Byte)BacnetApplicationTags.BACNET_APPLICATION_TAG_OBJECT_ID, tagNumber);
        ASN1.decode_object_id(buf.buffer, tagLen, out BacnetObjectTypes objType, out var instance);
        Assert.Equal(BacnetObjectTypes.OBJECT_ANALOG_VALUE, objType);
        Assert.Equal(5u, instance);
    }

    [Fact]
    [System.ComponentModel.DisplayName("encode_application_object_id DEVICE:666 往返")]
    public void ObjectId_Device_RoundTrip()
    {
        var buf = new EncodeBuffer();
        ASN1.encode_application_object_id(buf, BacnetObjectTypes.OBJECT_DEVICE, 666);
        var (_, _, tagLen) = DecodeTag(buf.buffer);
        ASN1.decode_object_id(buf.buffer, tagLen, out BacnetObjectTypes objType, out var instance);
        Assert.Equal(BacnetObjectTypes.OBJECT_DEVICE, objType);
        Assert.Equal(666u, instance);
    }
    #endregion

    #region 枚举
    [Fact]
    [System.ComponentModel.DisplayName("encode_application_enumerated 枚举往返")]
    public void Enumerated_RoundTrip()
    {
        var buf = new EncodeBuffer();
        ASN1.encode_application_enumerated(buf, 7u);
        var (tagNumber, lenValue, tagLen) = DecodeTag(buf.buffer);
        Assert.Equal((Byte)BacnetApplicationTags.BACNET_APPLICATION_TAG_ENUMERATED, tagNumber);
        ASN1.decode_unsigned(buf.buffer, tagLen, lenValue, out var decoded);
        Assert.Equal(7u, decoded);
    }
    #endregion

    #region 上下文标签
    [Fact]
    [System.ComponentModel.DisplayName("encode_context_unsigned 上下文标签往返")]
    public void ContextUnsigned_RoundTrip()
    {
        var buf = new EncodeBuffer();
        ASN1.encode_context_unsigned(buf, 2, 999u);
        var (tagNumber, lenValue, tagLen) = DecodeTag(buf.buffer);
        Assert.Equal(2, tagNumber);
        ASN1.decode_unsigned(buf.buffer, tagLen, lenValue, out var decoded);
        Assert.Equal(999u, decoded);
    }

    [Fact]
    [System.ComponentModel.DisplayName("encode_context_real 上下文 real 往返")]
    public void ContextReal_RoundTrip()
    {
        var buf = new EncodeBuffer();
        ASN1.encode_context_real(buf, 3, 1.5f);
        var (tagNumber, lenValue, tagLen) = DecodeTag(buf.buffer);
        Assert.Equal(3, tagNumber);
        Assert.Equal(4u, lenValue);
        ASN1.decode_real(buf.buffer, tagLen, out var decoded);
        Assert.Equal(1.5f, decoded);
    }
    #endregion
}
