using System;
using System.Collections.Generic;
using System.IO.BACnet;
using System.IO.BACnet.Serialize;
using System.IO.BACnet.Storage;
using Xunit;
using BacObject = System.IO.BACnet.Storage.Object;

namespace UnitTest;

/// <summary>DeviceStorage 单元测试。使用内存内构造，不依赖文件系统和网络。</summary>
public class DeviceStorageTests
{
    #region 辅助：构建测试用存储
    /// <summary>构建含 DEVICE + AI:0 + AV:0 的最小存储</summary>
    private static DeviceStorage BuildStorage(UInt32 deviceId = 666)
    {
        var storage = new DeviceStorage { DeviceId = deviceId };

        var deviceObj = new BacObject
        {
            Type = BacnetObjectTypes.OBJECT_DEVICE,
            Instance = deviceId,
            Properties = new[]
            {
                new Property
                {
                    Id = BacnetPropertyIds.PROP_OBJECT_NAME,
                    Tag = BacnetApplicationTags.BACNET_APPLICATION_TAG_CHARACTER_STRING
                }
            }
        };
        deviceObj.Properties[0].BacnetValue = new BacnetValue[]
        {
            new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_CHARACTER_STRING, "TestDevice")
        };

        var aiObj = new BacObject
        {
            Type = BacnetObjectTypes.OBJECT_ANALOG_INPUT,
            Instance = 0,
            Properties = new[]
            {
                new Property
                {
                    Id = BacnetPropertyIds.PROP_PRESENT_VALUE,
                    Tag = BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL
                }
            }
        };
        aiObj.Properties[0].BacnetValue = new BacnetValue[]
        {
            new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, 12.5f)
        };

        var avObj = new BacObject
        {
            Type = BacnetObjectTypes.OBJECT_ANALOG_VALUE,
            Instance = 0,
            Properties = new[]
            {
                new Property
                {
                    Id = BacnetPropertyIds.PROP_PRESENT_VALUE,
                    Tag = BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL
                }
            }
        };
        avObj.Properties[0].BacnetValue = new BacnetValue[]
        {
            new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, 0f)
        };

        storage.Objects = new BacObject[] { deviceObj, aiObj, avObj };
        return storage;
    }
    #endregion

    #region FindObject
    [Fact]
    [System.ComponentModel.DisplayName("FindObject 存在的对象类型+实例返回非空")]
    public void FindObject_Exists_ReturnsObject()
    {
        var storage = BuildStorage();
        var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 0);
        var obj = storage.FindObject(oid);
        Assert.NotNull(obj);
        Assert.Equal(BacnetObjectTypes.OBJECT_ANALOG_INPUT, obj.Type);
        Assert.Equal(0u, obj.Instance);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FindObject 不存在的实例返回 null")]
    public void FindObject_NotExists_ReturnsNull()
    {
        var storage = BuildStorage();
        var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 99);
        var obj = storage.FindObject(oid);
        Assert.Null(obj);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FindObject 不存在的类型返回 null")]
    public void FindObject_WrongType_ReturnsNull()
    {
        var storage = BuildStorage();
        var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_BINARY_INPUT, 0);
        var obj = storage.FindObject(oid);
        Assert.Null(obj);
    }
    #endregion

    #region FindProperty
    [Fact]
    [System.ComponentModel.DisplayName("FindProperty 存在的属性返回非空")]
    public void FindProperty_Exists_ReturnsProperty()
    {
        var storage = BuildStorage();
        var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 0);
        var prop = storage.FindProperty(oid, BacnetPropertyIds.PROP_PRESENT_VALUE);
        Assert.NotNull(prop);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FindProperty 不存在的属性返回 null")]
    public void FindProperty_NotExists_ReturnsNull()
    {
        var storage = BuildStorage();
        var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 0);
        var prop = storage.FindProperty(oid, BacnetPropertyIds.PROP_OBJECT_NAME);
        Assert.Null(prop);
    }
    #endregion

    #region ReadProperty
    [Fact]
    [System.ComponentModel.DisplayName("ReadProperty 存在的属性返回 Good 和正确值")]
    public void ReadProperty_Exists_ReturnsGood()
    {
        var storage = BuildStorage();
        var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 0);
        var code = storage.ReadProperty(oid, BacnetPropertyIds.PROP_PRESENT_VALUE, ASN1.BACNET_ARRAY_ALL, out var value);

        Assert.Equal(DeviceStorage.ErrorCodes.Good, code);
        Assert.NotNull(value);
        Assert.Single(value);
        Assert.Equal(12.5f, (Single)value[0].Value);
    }

    [Fact]
    [System.ComponentModel.DisplayName("ReadProperty 对象不存在返回 UnknownObject")]
    public void ReadProperty_UnknownObject_ReturnsError()
    {
        var storage = BuildStorage();
        var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_BINARY_INPUT, 0);
        var code = storage.ReadProperty(oid, BacnetPropertyIds.PROP_PRESENT_VALUE, ASN1.BACNET_ARRAY_ALL, out _);
        Assert.Equal(DeviceStorage.ErrorCodes.UnknownObject, code);
    }

    [Fact]
    [System.ComponentModel.DisplayName("ReadProperty 属性不存在返回 NotExist")]
    public void ReadProperty_UnknownProperty_ReturnsNotExist()
    {
        var storage = BuildStorage();
        var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 0);
        var code = storage.ReadProperty(oid, BacnetPropertyIds.PROP_OBJECT_NAME, ASN1.BACNET_ARRAY_ALL, out _);
        Assert.Equal(DeviceStorage.ErrorCodes.NotExist, code);
    }

    [Fact]
    [System.ComponentModel.DisplayName("ReadProperty 通配 DeviceId 时自动替换实例号")]
    public void ReadProperty_WildcardDeviceId_UsesActualDeviceId()
    {
        var storage = BuildStorage(deviceId: 42);
        // 通配 device ID（instance >= BACNET_MAX_INSTANCE）
        var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, ASN1.BACNET_MAX_INSTANCE);
        var code = storage.ReadProperty(oid, BacnetPropertyIds.PROP_OBJECT_NAME, ASN1.BACNET_ARRAY_ALL, out var value);
        Assert.Equal(DeviceStorage.ErrorCodes.Good, code);
        Assert.Equal("TestDevice", (String)value[0].Value);
    }
    #endregion

    #region WriteProperty
    [Fact]
    [System.ComponentModel.DisplayName("WriteProperty 写入成功并可读回")]
    public void WriteProperty_Roundtrip()
    {
        var storage = BuildStorage();
        var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, 0);
        var newValue = new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, 99.5f);

        var code = storage.WriteProperty(oid, BacnetPropertyIds.PROP_PRESENT_VALUE, ASN1.BACNET_ARRAY_ALL, new[] { newValue });
        Assert.Equal(DeviceStorage.ErrorCodes.Good, code);

        // 读回验证
        code = storage.ReadProperty(oid, BacnetPropertyIds.PROP_PRESENT_VALUE, ASN1.BACNET_ARRAY_ALL, out var value);
        Assert.Equal(DeviceStorage.ErrorCodes.Good, code);
        Assert.Equal(99.5f, (Single)value[0].Value);
    }

    [Fact]
    [System.ComponentModel.DisplayName("WriteProperty 对象不存在且不允许新建返回 NotExist")]
    public void WriteProperty_UnknownObject_NotAdd_ReturnsNotExist()
    {
        var storage = BuildStorage();
        var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_BINARY_INPUT, 0);
        var bv = new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_BOOLEAN, true);
        var code = storage.WriteProperty(oid, BacnetPropertyIds.PROP_PRESENT_VALUE, ASN1.BACNET_ARRAY_ALL, new[] { bv }, addIfNotExits: false);
        Assert.Equal(DeviceStorage.ErrorCodes.NotExist, code);
    }

    [Fact]
    [System.ComponentModel.DisplayName("WriteProperty addIfNotExits=true 新建对象和属性")]
    public void WriteProperty_AddIfNotExists_CreatesObject()
    {
        var storage = BuildStorage();
        var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_BINARY_VALUE, 1);
        var bv = new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_BOOLEAN, true);

        var code = storage.WriteProperty(oid, BacnetPropertyIds.PROP_PRESENT_VALUE, ASN1.BACNET_ARRAY_ALL, new[] { bv }, addIfNotExits: true);
        Assert.Equal(DeviceStorage.ErrorCodes.Good, code);

        // 读回验证已创建
        code = storage.ReadProperty(oid, BacnetPropertyIds.PROP_PRESENT_VALUE, ASN1.BACNET_ARRAY_ALL, out var value);
        Assert.Equal(DeviceStorage.ErrorCodes.Good, code);
        Assert.True((Boolean)value[0].Value);
    }
    #endregion

    #region ChangeOfValue 事件
    [Fact]
    [System.ComponentModel.DisplayName("WriteProperty 触发 ChangeOfValue 事件")]
    public void WriteProperty_TriggersChangeOfValueEvent()
    {
        var storage = BuildStorage();
        var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, 0);
        BacnetObjectId? firedOid = null;
        BacnetPropertyIds? firedProp = null;

        storage.ChangeOfValue += (sender, objectId, propertyId, arrayIndex, value) =>
        {
            firedOid = objectId;
            firedProp = propertyId;
        };

        var newValue = new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, 55.0f);
        storage.WriteProperty(oid, BacnetPropertyIds.PROP_PRESENT_VALUE, ASN1.BACNET_ARRAY_ALL, new[] { newValue });

        Assert.Equal(oid, firedOid);
        Assert.Equal(BacnetPropertyIds.PROP_PRESENT_VALUE, firedProp);
    }
    #endregion

    #region ReadOverride / WriteOverride 钩子
    [Fact]
    [System.ComponentModel.DisplayName("ReadOverride 命中时返回覆盖值")]
    public void ReadOverride_HandledTrue_ReturnsOverriddenValue()
    {
        var storage = BuildStorage();
        var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 0);

        storage.ReadOverride += (BacnetObjectId objectId, BacnetPropertyIds propertyId, UInt32 arrayIndex,
            out IList<BacnetValue> value, out DeviceStorage.ErrorCodes status, out Boolean handled) =>
        {
            value = new BacnetValue[] { new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, 777.0f) };
            status = DeviceStorage.ErrorCodes.Good;
            handled = true;
        };

        var code = storage.ReadProperty(oid, BacnetPropertyIds.PROP_PRESENT_VALUE, ASN1.BACNET_ARRAY_ALL, out var result);
        Assert.Equal(DeviceStorage.ErrorCodes.Good, code);
        Assert.Equal(777.0f, (Single)result[0].Value);
    }

    [Fact]
    [System.ComponentModel.DisplayName("WriteOverride 命中时不写入存储")]
    public void WriteOverride_HandledTrue_DoesNotWriteStorage()
    {
        var storage = BuildStorage();
        var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, 0);

        storage.WriteOverride += (BacnetObjectId objectId, BacnetPropertyIds propertyId, UInt32 arrayIndex,
            IList<BacnetValue> value, out DeviceStorage.ErrorCodes status, out Boolean handled) =>
        {
            status = DeviceStorage.ErrorCodes.Good;
            handled = true; // 拦截，不写入
        };

        var newValue = new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, 500.0f);
        storage.WriteProperty(oid, BacnetPropertyIds.PROP_PRESENT_VALUE, ASN1.BACNET_ARRAY_ALL, new[] { newValue });

        // 原值应未被修改
        storage.ReadProperty(oid, BacnetPropertyIds.PROP_PRESENT_VALUE, ASN1.BACNET_ARRAY_ALL, out var result);
        Assert.Equal(0f, (Single)result[0].Value);
    }
    #endregion

    #region ReadPropertyAll
    [Fact]
    [System.ComponentModel.DisplayName("ReadPropertyAll 返回所有属性")]
    public void ReadPropertyAll_ReturnsAllProperties()
    {
        var storage = BuildStorage();
        var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 0);
        var ok = storage.ReadPropertyAll(oid, out var values);
        Assert.True(ok);
        Assert.NotNull(values);
        Assert.NotEmpty(values);
    }

    [Fact]
    [System.ComponentModel.DisplayName("ReadPropertyAll 对象不存在返回 false")]
    public void ReadPropertyAll_UnknownObject_ReturnsFalse()
    {
        var storage = BuildStorage();
        var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_BINARY_INPUT, 99);
        var ok = storage.ReadPropertyAll(oid, out _);
        Assert.False(ok);
    }
    #endregion
}
