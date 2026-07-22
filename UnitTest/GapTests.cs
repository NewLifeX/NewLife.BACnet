using System;
using System.IO.BACnet;
using System.IO.BACnet.Serialize;
using Xunit;

namespace UnitTest;

/// <summary>竞品差距补齐（GAP）编解码单元测试。
/// 测试 Who-Has / I-Have、时间同步、设备通信控制的 encode→decode 往返一致性。</summary>
public class GapTests
{
    #region Who-Has / I-Have (GAP-1)

    [Fact]
    [System.ComponentModel.DisplayName("Who-Has 按对象 ID 编码→解码往返")]
    public void WhoHas_ByObjectId_RoundTrip()
    {
        var lowLimit = -1;
        var highLimit = -1;
        var objId = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, 100);

        var buf = new EncodeBuffer();
        Services.EncodeWhoHasBroadcast(buf, lowLimit, highLimit, objId, null);

        var len = Services.DecodeWhoHasBroadcast(buf.buffer, 0, buf.offset,
            out var decodedLow, out var decodedHigh, out var decodedObjId, out var decodedName);

        Assert.True(len > 0);
        Assert.Equal(lowLimit, decodedLow);
        Assert.Equal(highLimit, decodedHigh);
        Assert.NotNull(decodedObjId);
        Assert.Equal(objId.type, decodedObjId.Value.type);
        Assert.Equal(objId.instance, decodedObjId.Value.instance);
        Assert.Null(decodedName);
    }

    [Fact]
    [System.ComponentModel.DisplayName("Who-Has 按对象名称编码→解码往返")]
    public void WhoHas_ByName_RoundTrip()
    {
        var lowLimit = -1;
        var highLimit = -1;
        var objName = "TemperatureSensor";

        var buf = new EncodeBuffer();
        Services.EncodeWhoHasBroadcast(buf, lowLimit, highLimit, null, objName);

        var len = Services.DecodeWhoHasBroadcast(buf.buffer, 0, buf.offset,
            out var decodedLow, out var decodedHigh, out var decodedObjId, out var decodedName);

        Assert.True(len > 0);
        Assert.Null(decodedObjId);
        Assert.Equal(objName, decodedName);
    }

    [Fact]
    [System.ComponentModel.DisplayName("Who-Has 含设备范围限制编码→解码往返")]
    public void WhoHas_WithLimits_RoundTrip()
    {
        var lowLimit = 0;
        var highLimit = 1000;
        var objName = "Sensor";

        var buf = new EncodeBuffer();
        Services.EncodeWhoHasBroadcast(buf, lowLimit, highLimit, null, objName);

        var len = Services.DecodeWhoHasBroadcast(buf.buffer, 0, buf.offset,
            out var decodedLow, out var decodedHigh, out var decodedObjId, out var decodedName);

        Assert.True(len > 0);
        Assert.Equal(lowLimit, decodedLow);
        Assert.Equal(highLimit, decodedHigh);
        Assert.Equal(objName, decodedName);
    }

    [Fact]
    [System.ComponentModel.DisplayName("I-Have 编码→解码往返")]
    public void IHave_RoundTrip()
    {
        var deviceId = new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, 1234);
        var objId = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 5);
        var objName = "AI-05-Temperature";

        var buf = new EncodeBuffer();
        Services.EncodeIhaveBroadcast(buf, deviceId, objId, objName);

        var len = Services.DecodeIhaveBroadcast(buf.buffer, 0, buf.offset,
            out var decodedDeviceId, out var decodedObjId, out var decodedName);

        Assert.True(len > 0);
        Assert.Equal(deviceId.type, decodedDeviceId.type);
        Assert.Equal(deviceId.instance, decodedDeviceId.instance);
        Assert.Equal(objId.type, decodedObjId.type);
        Assert.Equal(objId.instance, decodedObjId.instance);
        Assert.Equal(objName, decodedName);
    }

    [Fact]
    [System.ComponentModel.DisplayName("I-Have 空对象名称编码→解码往返")]
    public void IHave_EmptyName_RoundTrip()
    {
        var deviceId = new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, 1);
        var objId = new BacnetObjectId(BacnetObjectTypes.OBJECT_BINARY_VALUE, 10);
        var objName = "";

        var buf = new EncodeBuffer();
        Services.EncodeIhaveBroadcast(buf, deviceId, objId, objName);

        var len = Services.DecodeIhaveBroadcast(buf.buffer, 0, buf.offset,
            out var decodedDeviceId, out var decodedObjId, out var decodedName);

        Assert.True(len > 0);
        Assert.Equal(objName, decodedName);
    }

    #endregion

    #region 时间同步 (GAP-2)

    [Fact]
    [System.ComponentModel.DisplayName("TimeSync 编码→解码往返")]
    public void TimeSync_RoundTrip()
    {
        var now = new DateTime(2025, 6, 15, 14, 30, 0, DateTimeKind.Unspecified);

        var buf = new EncodeBuffer();
        Services.EncodeTimeSync(buf, now);

        var len = Services.DecodeTimeSync(buf.buffer, 0, buf.offset, out var decoded);

        Assert.True(len > 0);
        Assert.Equal(now.Year, decoded.Year);
        Assert.Equal(now.Month, decoded.Month);
        Assert.Equal(now.Day, decoded.Day);
        Assert.Equal(now.Hour, decoded.Hour);
        Assert.Equal(now.Minute, decoded.Minute);
        Assert.Equal(now.Second, decoded.Second);
    }

    [Fact]
    [System.ComponentModel.DisplayName("TimeSync 午夜时间编码→解码往返")]
    public void TimeSync_Midnight_RoundTrip()
    {
        var midnight = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Unspecified);

        var buf = new EncodeBuffer();
        Services.EncodeTimeSync(buf, midnight);

        var len = Services.DecodeTimeSync(buf.buffer, 0, buf.offset, out var decoded);

        Assert.True(len > 0);
        Assert.Equal(midnight.Year, decoded.Year);
        Assert.Equal(midnight.Month, decoded.Month);
        Assert.Equal(midnight.Day, decoded.Day);
        Assert.Equal(0, decoded.Hour);
        Assert.Equal(0, decoded.Minute);
        Assert.Equal(0, decoded.Second);
    }

    [Fact]
    [System.ComponentModel.DisplayName("TimeSync 无效数据解码返回 -1")]
    public void TimeSync_InvalidData_ReturnsMinusOne()
    {
        var buf = new byte[] { 0x00, 0x01, 0x02 };
        var len = Services.DecodeTimeSync(buf, 0, buf.Length, out _);
        Assert.True(len < 0);
    }

    #endregion

    #region 设备通信控制 DCC (GAP-3)

    [Fact]
    [System.ComponentModel.DisplayName("DCC 编码→解码往返（含时间+密码）")]
    public void DCC_WithDurationAndPassword_RoundTrip()
    {
        var timeDuration = 60u;
        var enableDisable = 1u; // ENABLE
        var password = "admin";

        var buf = new EncodeBuffer();
        Services.EncodeDeviceCommunicationControl(buf, timeDuration, enableDisable, password);

        var len = Services.DecodeDeviceCommunicationControl(buf.buffer, 0, buf.offset,
            out var decodedTime, out var decodedEnable, out var decodedPassword);

        Assert.True(len > 0);
        Assert.Equal(timeDuration, decodedTime);
        Assert.Equal(enableDisable, decodedEnable);
        Assert.Equal(password, decodedPassword);
    }

    [Fact]
    [System.ComponentModel.DisplayName("DCC 编码→解码往返（不含密码）")]
    public void DCC_WithoutPassword_RoundTrip()
    {
        var timeDuration = 0u;
        var enableDisable = 0u; // DISABLE

        var buf = new EncodeBuffer();
        Services.EncodeDeviceCommunicationControl(buf, timeDuration, enableDisable, null);

        var len = Services.DecodeDeviceCommunicationControl(buf.buffer, 0, buf.offset,
            out var decodedTime, out var decodedEnable, out var decodedPassword);

        Assert.True(len > 0);
        Assert.Equal(0u, decodedTime); // 未指定则为 0
        Assert.Equal(enableDisable, decodedEnable);
    }

    [Fact]
    [System.ComponentModel.DisplayName("DCC 编码→解码往返（含密码无时间）")]
    public void DCC_WithPasswordNoDuration_RoundTrip()
    {
        var timeDuration = 0u;
        var enableDisable = 2u; // DISABLE_INITIATION
        var password = "secret!";

        var buf = new EncodeBuffer();
        Services.EncodeDeviceCommunicationControl(buf, timeDuration, enableDisable, password);

        var len = Services.DecodeDeviceCommunicationControl(buf.buffer, 0, buf.offset,
            out var decodedTime, out var decodedEnable, out var decodedPassword);

        Assert.True(len > 0);
        Assert.Equal(0u, decodedTime);
        Assert.Equal(enableDisable, decodedEnable);
        Assert.Equal(password, decodedPassword);
    }

    [Fact]
    [System.ComponentModel.DisplayName("DCC 无效数据解码返回 -1")]
    public void DCC_InvalidData_ReturnsMinusOne()
    {
        var buf = new byte[] { 0x00 };
        var len = Services.DecodeDeviceCommunicationControl(buf, 0, buf.Length, out _, out _, out _);
        Assert.True(len < 0);
    }

    #endregion
}
