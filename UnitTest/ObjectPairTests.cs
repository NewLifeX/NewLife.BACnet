using System.IO.BACnet;
using NewLife.BACnet.Protocols;
using Xunit;

namespace UnitTest;

/// <summary>ObjectPair 单元测试。不依赖网络，可完全离线运行。</summary>
public class ObjectPairTests
{
    #region TryParse
    [Fact]
    [System.ComponentModel.DisplayName("TryParse 格式 实例_类型 正常解析")]
    public void TryParse_Valid_InstanceType()
    {
        var ok = ObjectPair.TryParse("0_2", out var oid);
        Assert.True(ok);
        Assert.Equal(BacnetObjectTypes.OBJECT_ANALOG_VALUE, oid.type);
        Assert.Equal(0u, oid.instance);
    }

    [Fact]
    [System.ComponentModel.DisplayName("TryParse 格式 1_0 解析实例1 类型0")]
    public void TryParse_Instance1_Type0()
    {
        var ok = ObjectPair.TryParse("1_0", out var oid);
        Assert.True(ok);
        Assert.Equal(BacnetObjectTypes.OBJECT_ANALOG_INPUT, oid.type);
        Assert.Equal(1u, oid.instance);
    }

    [Fact]
    [System.ComponentModel.DisplayName("TryParse 格式 0_5 解析二进制值")]
    public void TryParse_BinaryValue()
    {
        var ok = ObjectPair.TryParse("0_5", out var oid);
        Assert.True(ok);
        Assert.Equal(BacnetObjectTypes.OBJECT_BINARY_VALUE, oid.type);
        Assert.Equal(0u, oid.instance);
    }

    [Fact]
    [System.ComponentModel.DisplayName("TryParse 格式 0_8 解析设备对象")]
    public void TryParse_DeviceObject()
    {
        var ok = ObjectPair.TryParse("0_8", out var oid);
        Assert.True(ok);
        Assert.Equal(BacnetObjectTypes.OBJECT_DEVICE, oid.type);
        Assert.Equal(0u, oid.instance);
    }

    [Fact]
    [System.ComponentModel.DisplayName("TryParse 仅有实例号无类型则类型默认0")]
    public void TryParse_OnlyInstance_TypeDefaultsZero()
    {
        var ok = ObjectPair.TryParse("5", out var oid);
        Assert.True(ok);
        Assert.Equal(BacnetObjectTypes.OBJECT_ANALOG_INPUT, oid.type); // 0 = ANALOG_INPUT
        Assert.Equal(5u, oid.instance);
    }

    [Fact]
    [System.ComponentModel.DisplayName("TryParse null 返回 false")]
    public void TryParse_Null_ReturnsFalse()
    {
        var ok = ObjectPair.TryParse(null, out _);
        Assert.False(ok);
    }

    [Fact]
    [System.ComponentModel.DisplayName("TryParse 空字符串返回 false")]
    public void TryParse_Empty_ReturnsFalse()
    {
        var ok = ObjectPair.TryParse("", out _);
        Assert.False(ok);
    }
    #endregion

    #region ToObjectId 往返
    [Fact]
    [System.ComponentModel.DisplayName("ToObjectId 输出格式 实例_类型")]
    public void ToObjectId_Format()
    {
        var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, 3);
        var str = ObjectPair.ToObjectId(oid);
        Assert.Equal("3_2", str);
    }

    [Fact]
    [System.ComponentModel.DisplayName("ToObjectId 往返解析结果一致")]
    public void ToObjectId_RoundTrip()
    {
        var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_BINARY_INPUT, 7);
        var str = ObjectPair.ToObjectId(oid);
        var ok = ObjectPair.TryParse(str, out var oid2);
        Assert.True(ok);
        Assert.Equal(oid.type, oid2.type);
        Assert.Equal(oid.instance, oid2.instance);
    }

    [Fact]
    [System.ComponentModel.DisplayName("GetKey 与 ToObjectId 输出一致")]
    public void GetKey_MatchesToObjectId()
    {
        var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 2);
        var key = oid.GetKey();
        var str = ObjectPair.ToObjectId(oid);
        Assert.Equal(str, key);
    }
    #endregion
}
