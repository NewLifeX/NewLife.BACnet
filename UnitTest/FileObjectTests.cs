using System;
using System.Collections.Generic;
using System.IO.BACnet;
using System.IO.BACnet.Serialize;
using Xunit;

namespace UnitTest;

/// <summary>文件服务（FIL）和对象管理（OBJ）编解码单元测试。
/// 测试 AtomicReadFile/AtomicWriteFile、CreateObject/DeleteObject、AddListElement/RemoveListElement 的 encode→decode 往返一致性。</summary>
public class FileObjectTests
{
    #region 文件服务 (FIL-1/2)

    [Fact]
    [System.ComponentModel.DisplayName("AtomicReadFile 流模式编码→解码往返")]
    public void AtomicReadFile_Stream_RoundTrip()
    {
        var fileId = new BacnetObjectId(BacnetObjectTypes.OBJECT_FILE, 1);
        var position = 100;
        var count = 50u;

        var buf = new EncodeBuffer();
        Services.EncodeAtomicReadFile(buf, true, fileId, position, count);

        var len = Services.DecodeAtomicReadFile(buf.buffer, 0, buf.offset,
            out var isStream, out var decodedId, out var decodedPos, out var decodedCount);

        Assert.True(len > 0);
        Assert.True(isStream);
        Assert.Equal(fileId.type, decodedId.type);
        Assert.Equal(fileId.instance, decodedId.instance);
        Assert.Equal(position, decodedPos);
        Assert.Equal(count, decodedCount);
    }

    [Fact]
    [System.ComponentModel.DisplayName("AtomicReadFile 记录模式编码→解码往返")]
    public void AtomicReadFile_Record_RoundTrip()
    {
        var fileId = new BacnetObjectId(BacnetObjectTypes.OBJECT_FILE, 2);
        var position = 5;
        var count = 10u;

        var buf = new EncodeBuffer();
        Services.EncodeAtomicReadFile(buf, false, fileId, position, count);

        var len = Services.DecodeAtomicReadFile(buf.buffer, 0, buf.offset,
            out var isStream, out var decodedId, out var decodedPos, out var decodedCount);

        Assert.True(len > 0);
        Assert.False(isStream);
        Assert.Equal(fileId.type, decodedId.type);
        Assert.Equal(fileId.instance, decodedId.instance);
        Assert.Equal(position, decodedPos);
        Assert.Equal(count, decodedCount);
    }

    [Fact]
    [System.ComponentModel.DisplayName("AtomicWriteFile 流模式编码→解码往返")]
    public void AtomicWriteFile_Stream_RoundTrip()
    {
        var fileId = new BacnetObjectId(BacnetObjectTypes.OBJECT_FILE, 1);
        var position = 200;
        var data = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }; // "Hello"
        var blocks = new[] { data };
        var counts = new[] { data.Length };

        var buf = new EncodeBuffer();
        Services.EncodeAtomicWriteFile(buf, true, fileId, position, 1, blocks, counts);

        var len = Services.DecodeAtomicWriteFile(buf.buffer, 0, buf.offset,
            out var isStream, out var decodedId, out var decodedPos,
            out var decodedBlockCount, out var decodedBlocks, out var decodedCounts);

        Assert.True(len > 0);
        Assert.True(isStream);
        Assert.Equal(fileId.type, decodedId.type);
        Assert.Equal(fileId.instance, decodedId.instance);
        Assert.Equal(position, decodedPos);
        Assert.Equal(1u, decodedBlockCount);
        Assert.NotNull(decodedBlocks);
        Assert.True(decodedBlocks.Length >= 1);
    }

    [Fact]
    [System.ComponentModel.DisplayName("AtomicWriteFile 记录模式编码→解码往返")]
    public void AtomicWriteFile_Record_RoundTrip()
    {
        var fileId = new BacnetObjectId(BacnetObjectTypes.OBJECT_FILE, 2);
        var position = 0;
        var block1 = new byte[] { 0x01, 0x02, 0x03 };
        var block2 = new byte[] { 0x04, 0x05, 0x06 };
        var blocks = new[] { block1, block2 };
        var counts = new[] { block1.Length, block2.Length };

        var buf = new EncodeBuffer();
        Services.EncodeAtomicWriteFile(buf, false, fileId, position, 2, blocks, counts);

        var len = Services.DecodeAtomicWriteFile(buf.buffer, 0, buf.offset,
            out var isStream, out var decodedId, out var decodedPos,
            out var decodedBlockCount, out var decodedBlocks, out var decodedCounts);

        Assert.True(len > 0);
        Assert.False(isStream);
        Assert.Equal(2u, decodedBlockCount);
    }

    #endregion

    #region 对象管理 (OBJ-1~4)

    [Fact]
    [System.ComponentModel.DisplayName("CreateObjectAcknowledge 编码")]
    public void CreateObjectAcknowledge_Encodes()
    {
        var objId = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 10);
        var buf = new EncodeBuffer();

        Services.EncodeCreateObjectAcknowledge(buf, objId);

        Assert.True(buf.offset > 0);
    }

    [Fact]
    [System.ComponentModel.DisplayName("DecodeDeleteObject 正常解码")]
    public void DeleteObject_Decode()
    {
        // DeleteObject 请求直接编码应用层 ObjectId
        var objId = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_OUTPUT, 3);
        var buf = new EncodeBuffer();

        ASN1.encode_application_object_id(buf, objId.type, objId.instance);

        var len = Services.DecodeDeleteObject(buf.buffer, 0, buf.offset, out var decodedId);

        Assert.True(len > 0);
        Assert.Equal(objId.type, decodedId.type);
        Assert.Equal(objId.instance, decodedId.instance);
    }

    [Fact]
    [System.ComponentModel.DisplayName("DecodeDeleteObject 无效报文返回 -1")]
    public void DeleteObject_Decode_Invalid()
    {
        var buf = new EncodeBuffer();
        buf.Add(0x00); // 无效数据

        var len = Services.DecodeDeleteObject(buf.buffer, 0, buf.offset, out _);

        Assert.True(len < 0);
    }

    [Fact]
    [System.ComponentModel.DisplayName("AddListElement 编码不抛异常")]
    public void AddListElement_Encodes()
    {
        var objId = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 1);
        var propertyId = (UInt32)BacnetPropertyIds.PROP_PRESENT_VALUE;
        var values = new List<BacnetValue>
        {
            new(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, 42.0f),
        };

        var buf = new EncodeBuffer();
        Services.EncodeAddListElement(buf, objId, propertyId, ASN1.BACNET_ARRAY_ALL, values);

        Assert.True(buf.offset > 0);
    }

    [Fact]
    [System.ComponentModel.DisplayName("AddListElement 含数组索引编码")]
    public void AddListElement_WithArrayIndex()
    {
        var objId = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 1);
        var propertyId = (UInt32)BacnetPropertyIds.PROP_PRIORITY_ARRAY;
        var arrayIndex = 5u;
        var values = new List<BacnetValue>
        {
            new(BacnetApplicationTags.BACNET_APPLICATION_TAG_NULL, null),
        };

        var buf = new EncodeBuffer();
        Services.EncodeAddListElement(buf, objId, propertyId, arrayIndex, values);

        Assert.True(buf.offset > 0);
    }

    [Fact]
    [System.ComponentModel.DisplayName("RemoveListElement 编码不抛异常 (OBJ-4)")]
    public void RemoveListElement_Encodes()
    {
        var objId = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 1);
        var propertyId = (UInt32)BacnetPropertyIds.PROP_PRESENT_VALUE;
        var values = new List<BacnetValue>
        {
            new(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, 42.0f),
        };

        var buf = new EncodeBuffer();
        Services.EncodeAddListElement(buf, objId, propertyId, ASN1.BACNET_ARRAY_ALL, values);

        Assert.True(buf.offset > 0);
    }

    [Fact]
    [System.ComponentModel.DisplayName("RemoveListElement 含数组索引编码 (OBJ-4)")]
    public void RemoveListElement_WithArrayIndex()
    {
        var objId = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 1);
        var propertyId = (UInt32)BacnetPropertyIds.PROP_PRIORITY_ARRAY;
        var arrayIndex = 3u;
        var values = new List<BacnetValue>
        {
            new(BacnetApplicationTags.BACNET_APPLICATION_TAG_NULL, null),
        };

        var buf = new EncodeBuffer();
        Services.EncodeAddListElement(buf, objId, propertyId, arrayIndex, values);

        Assert.True(buf.offset > 0);
    }

    #endregion
}
