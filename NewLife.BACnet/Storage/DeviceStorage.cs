using System.Reflection;
using System.Xml.Serialization;

namespace System.IO.BACnet.Storage;

/// <summary>设备数据存储。管理 BACnet 仿真设备的 XML 对象/属性存储。</summary>
/// <remarks>
/// 提供对 BACnet 设备对象的 XML 序列化/反序列化存储，支持：
/// - 设备对象的 CRUD 操作
/// - 属性读写（单属性/多属性/全部属性）
/// - 可写属性优先级控制（Priority Array 1-16）
/// - 读写钩子（ReadOverride/WriteOverride）允许应用层覆盖默认行为
/// - 属性变化事件（ChangeOfValue），用于驱动 COV 通知
/// </remarks>
[Serializable]
public class DeviceStorage
{
    /// <summary>设备实例 ID</summary>
    [XmlIgnore]
    public UInt32 DeviceId { get; set; }

    /// <summary>属性值变化事件委托</summary>
    /// <param name="sender">触发事件的存储实例</param>
    /// <param name="objectId">变化的对象 ID</param>
    /// <param name="propertyId">变化的属性 ID</param>
    /// <param name="arrayIndex">数组索引</param>
    /// <param name="value">新的属性值</param>
    public delegate void ChangeOfValueHandler(DeviceStorage sender, BacnetObjectId objectId, BacnetPropertyIds propertyId, UInt32 arrayIndex, IList<BacnetValue> value);

    /// <summary>属性值变化时触发，用于驱动 COV 通知</summary>
    public event ChangeOfValueHandler ChangeOfValue;

    /// <summary>读属性覆盖委托</summary>
    /// <param name="objectId">对象 ID</param>
    /// <param name="propertyId">属性 ID</param>
    /// <param name="arrayIndex">数组索引</param>
    /// <param name="value">输出：属性值</param>
    /// <param name="status">输出：错误码</param>
    /// <param name="handled">输出：是否已处理</param>
    public delegate void ReadOverrideHandler(BacnetObjectId objectId, BacnetPropertyIds propertyId, UInt32 arrayIndex, out IList<BacnetValue> value, out ErrorCodes status, out Boolean handled);

    /// <summary>读属性覆盖事件，应用层可注册此事件拦截默认读行为</summary>
    public event ReadOverrideHandler ReadOverride;

    /// <summary>写属性覆盖委托</summary>
    /// <param name="objectId">对象 ID</param>
    /// <param name="propertyId">属性 ID</param>
    /// <param name="arrayIndex">数组索引</param>
    /// <param name="value">要写入的属性值</param>
    /// <param name="status">输出：错误码</param>
    /// <param name="handled">输出：是否已处理</param>
    public delegate void WriteOverrideHandler(BacnetObjectId objectId, BacnetPropertyIds propertyId, UInt32 arrayIndex, IList<BacnetValue> value, out ErrorCodes status, out Boolean handled);

    /// <summary>写属性覆盖事件，应用层可注册此事件拦截默认写行为</summary>
    public event WriteOverrideHandler WriteOverride;

    /// <summary>设备对象集合</summary>
    public Object[] Objects { get; set; }

    /// <summary>初始化设备存储，自动分配随机设备 ID</summary>
    public DeviceStorage()
    {
        DeviceId = (UInt32)new Random().Next();
        Objects = new Object[0];
    }

    /// <summary>根据对象 ID 和属性 ID 查找属性</summary>
    /// <param name="objectId">对象 ID</param>
    /// <param name="propertyId">属性 ID</param>
    /// <returns>属性实例，未找到返回 null</returns>
    public Property FindProperty(BacnetObjectId objectId, BacnetPropertyIds propertyId)
    {
        //liniear search
        var obj = FindObject(objectId);
        return FindProperty(obj, propertyId);
    }

    private static Property FindProperty(Object obj, BacnetPropertyIds propertyId)
    {
        //liniear search
        return obj?.Properties.FirstOrDefault(p => p.Id == propertyId);
    }

    private Object FindObject(BacnetObjectTypes objectType)
    {
        //liniear search
        return Objects.FirstOrDefault(obj => obj.Type == objectType);
    }

    /// <summary>根据对象 ID 查找对象</summary>
    /// <param name="objectId">对象 ID（类型 + 实例号）</param>
    /// <returns>对象实例，未找到返回 null</returns>
    public Object FindObject(BacnetObjectId objectId)
    {
        //liniear search
        return Objects.FirstOrDefault(obj => obj.Type == objectId.type && obj.Instance == objectId.instance);
    }

    /// <summary>属性读写操作错误码</summary>
    public enum ErrorCodes
    {
        Good = 0,
        GenericError = -1,
        NotExist = -2,
        NotForMe = -3,
        WriteAccessDenied = -4,
        UnknownObject = -5,
        UnknownProperty = -6
    }

    /// <summary>读取属性值（返回 Int32 类型）</summary>
    /// <param name="objectId">对象 ID</param>
    /// <param name="propertyId">属性 ID</param>
    /// <returns>属性值的 Int32 表示，失败返回 0</returns>
    public Int32 ReadPropertyValue(BacnetObjectId objectId, BacnetPropertyIds propertyId)
    {
        if (ReadProperty(objectId, propertyId, Serialize.ASN1.BACNET_ARRAY_ALL, out IList<BacnetValue> value) != ErrorCodes.Good)
            return 0;

        if (value == null || value.Count < 1)
            return 0;

        return (Int32)Convert.ChangeType(value[0].Value, typeof(Int32));
    }

    /// <summary>读取属性值</summary>
    /// <param name="objectId">对象 ID</param>
    /// <param name="propertyId">属性 ID</param>
    /// <param name="arrayIndex">数组索引（0=计数，ALL=全部，N=第 N 个）</param>
    /// <param name="value">输出的属性值列表</param>
    /// <returns>错误码</returns>
    /// <remarks>支持 ReadOverride 钩子，如果已注册则优先调用外部处理。</remarks>
    public ErrorCodes ReadProperty(BacnetObjectId objectId, BacnetPropertyIds propertyId, UInt32 arrayIndex, out IList<BacnetValue> value)
    {
        value = new BacnetValue[0];

        //wildcard device_id
        if (objectId.type == BacnetObjectTypes.OBJECT_DEVICE && objectId.instance >= Serialize.ASN1.BACNET_MAX_INSTANCE)
            objectId.instance = DeviceId;

        //overrides
        if (ReadOverride != null)
        {
            ReadOverride(objectId, propertyId, arrayIndex, out value, out ErrorCodes status, out var handled);
            if (handled)
                return status;
        }

        //find in storage
        var obj = FindObject(objectId);
        if (obj == null)
            return ErrorCodes.UnknownObject;

        //object found now find property
        var p = FindProperty(objectId, propertyId);
        if (p == null)
            return ErrorCodes.NotExist;

        //get value ... check for array index
        if (arrayIndex == 0)
        {
            value = new[] { new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_UNSIGNED_INT, (UInt32)p.BacnetValue.Count) };
        }
        else if (arrayIndex != Serialize.ASN1.BACNET_ARRAY_ALL)
        {
            value = new[] { p.BacnetValue[(Int32)arrayIndex - 1] };
        }
        else
        {
            value = p.BacnetValue;
        }

        return ErrorCodes.Good;
    }

    /// <summary>批量读取多个属性</summary>
    /// <param name="objectId">对象 ID</param>
    /// <param name="properties">要读取的属性引用列表</param>
    /// <param name="values">输出的属性值列表</param>
    public void ReadPropertyMultiple(BacnetObjectId objectId, ICollection<BacnetPropertyReference> properties, out IList<BacnetPropertyValue> values)
    {
        var valuesRet = new List<BacnetPropertyValue>();

        foreach (var entry in properties)
        {
            var newEntry = new BacnetPropertyValue { property = entry };

            switch (ReadProperty(objectId, (BacnetPropertyIds)entry.propertyIdentifier, entry.propertyArrayIndex, out newEntry.value))
            {
                case ErrorCodes.UnknownObject:
                    newEntry.value = new[]
                    {
                            new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_ERROR,
                            new BacnetError(BacnetErrorClasses.ERROR_CLASS_OBJECT, BacnetErrorCodes.ERROR_CODE_UNKNOWN_OBJECT))
                        };
                    break;
                case ErrorCodes.NotExist:
                    newEntry.value = new[]
                    {
                            new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_ERROR,
                            new BacnetError(BacnetErrorClasses.ERROR_CLASS_PROPERTY, BacnetErrorCodes.ERROR_CODE_UNKNOWN_PROPERTY))
                        };
                    break;
            }

            valuesRet.Add(newEntry);
        }

        values = valuesRet;
    }

    /// <summary>读取对象的所有属性值</summary>
    /// <param name="objectId">对象 ID</param>
    /// <param name="values">输出的全部属性值列表</param>
    /// <returns>是否成功找到对象</returns>
    public Boolean ReadPropertyAll(BacnetObjectId objectId, out IList<BacnetPropertyValue> values)
    {
        //find
        var obj = FindObject(objectId);
        if (obj == null)
        {
            values = null;
            return false;
        }

        //build
        var propertyValues = new BacnetPropertyValue[obj.Properties.Length];
        for (var i = 0; i < obj.Properties.Length; i++)
        {
            var newEntry = new BacnetPropertyValue
            {
                property = new BacnetPropertyReference((UInt32)obj.Properties[i].Id, Serialize.ASN1.BACNET_ARRAY_ALL)
            };

            if (ReadProperty(objectId, obj.Properties[i].Id, Serialize.ASN1.BACNET_ARRAY_ALL, out newEntry.value) != ErrorCodes.Good)
            {
                var bacnetError = new BacnetError(BacnetErrorClasses.ERROR_CLASS_OBJECT, BacnetErrorCodes.ERROR_CODE_UNKNOWN_PROPERTY);
                newEntry.value = new[] { new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_ERROR, bacnetError) };
            }

            propertyValues[i] = newEntry;
        }

        values = propertyValues;
        return true;
    }

    /// <summary>写入属性值（Int32 类型，自动类型转换）</summary>
    /// <param name="objectId">对象 ID</param>
    /// <param name="propertyId">属性 ID</param>
    /// <param name="value">要写入的整数值</param>
    public void WritePropertyValue(BacnetObjectId objectId, BacnetPropertyIds propertyId, Int32 value)
    {
        //get existing type
        if (ReadProperty(objectId, propertyId, Serialize.ASN1.BACNET_ARRAY_ALL, out IList<BacnetValue> readValues) != ErrorCodes.Good)
            return;

        if (readValues == null || readValues.Count == 0)
            return;

        //write
        WriteProperty(objectId, propertyId, Serialize.ASN1.BACNET_ARRAY_ALL, new[]
        {
                new BacnetValue(readValues[0].Tag, Convert.ChangeType(value, readValues[0].Value.GetType()))
            });
    }


    /// <summary>写入属性值（简便重载，自动使用 ALL 索引）</summary>
    /// <param name="objectId">对象 ID</param>
    /// <param name="propertyId">属性 ID</param>
    /// <param name="value">要写入的属性值</param>
    public void WriteProperty(BacnetObjectId objectId, BacnetPropertyIds propertyId, BacnetValue value)
    {
        WriteProperty(objectId, propertyId, Serialize.ASN1.BACNET_ARRAY_ALL, new[] { value });
    }

    /// <summary>写入属性值（完整参数）</summary>
    /// <param name="objectId">对象 ID</param>
    /// <param name="propertyId">属性 ID</param>
    /// <param name="arrayIndex">数组索引</param>
    /// <param name="value">要写入的属性值列表</param>
    /// <param name="addIfNotExits">属性不存在时是否自动创建</param>
    /// <returns>错误码</returns>
    /// <remarks>
    /// 写入后自动触发 <see cref="ChangeOfValue"/> 事件，用于驱动 COV 通知。
    /// 支持 WriteOverride 钩子，如果已注册则优先调用外部处理。
    /// </remarks>
    public ErrorCodes WriteProperty(BacnetObjectId objectId, BacnetPropertyIds propertyId, UInt32 arrayIndex, IList<BacnetValue> value, Boolean addIfNotExits = false)
    {
        //wildcard device_id
        if (objectId.type == BacnetObjectTypes.OBJECT_DEVICE && objectId.instance >= Serialize.ASN1.BACNET_MAX_INSTANCE)
            objectId.instance = DeviceId;

        //overrides
        if (WriteOverride != null)
        {
            WriteOverride(objectId, propertyId, arrayIndex, value, out ErrorCodes status, out var handled);
            if (handled)
                return status;
        }

        //find
        var p = FindProperty(objectId, propertyId);
        if (p == null)
        {
            if (!addIfNotExits) return ErrorCodes.NotExist;

            //add obj
            var obj = FindObject(objectId);
            if (obj == null)
            {
                obj = new Object
                {
                    Type = objectId.type,
                    Instance = objectId.instance
                };
                var arr = Objects;
                Array.Resize(ref arr, arr.Length + 1);
                arr[arr.Length - 1] = obj;
                Objects = arr;
            }

            //add property
            p = new Property { Id = propertyId };
            var props = obj.Properties;
            Array.Resize(ref props, props.Length + 1);
            props[props.Length - 1] = p;
            obj.Properties = props;
        }

        //set type if needed
        if (p.Tag == BacnetApplicationTags.BACNET_APPLICATION_TAG_NULL && value != null)
        {
            foreach (var v in value)
            {
                if (v.Tag == BacnetApplicationTags.BACNET_APPLICATION_TAG_NULL)
                    continue;

                p.Tag = v.Tag;
                break;
            }
        }

        //write
        p.BacnetValue = value;

        //send event ... for subscriptions
        ChangeOfValue?.Invoke(this, objectId, propertyId, arrayIndex, value);

        return ErrorCodes.Good;
    }

    /// <summary>写入可命令属性（支持 Priority Array 1-16 级优先级）</summary>
    /// <param name="objectId">对象 ID</param>
    /// <param name="propertyId">属性 ID（仅 PROP_PRESENT_VALUE 和 PROP_RELINQUISH_DEFAULT）</param>
    /// <param name="value">要写入的属性值</param>
    /// <param name="priority">写入优先级（1-16，1 最高）</param>
    /// <returns>错误码</returns>
    /// <remarks>
    /// 向具有 16 级 Priority Array 的对象写入 Present Value，
    /// 符合 BACnet 标准中可命令属性的写入规范。
    /// 写入值后自动切换 Present Value 到最高优先级的值。
    /// </remarks>
    public ErrorCodes WriteCommandableProperty(BacnetObjectId objectId, BacnetPropertyIds propertyId, BacnetValue value, UInt32 priority)
    {

        if (propertyId != BacnetPropertyIds.PROP_PRESENT_VALUE)
            return ErrorCodes.NotForMe;

        var presentvalue = FindProperty(objectId, BacnetPropertyIds.PROP_PRESENT_VALUE);
        if (presentvalue == null)
            return ErrorCodes.NotForMe;

        var relinquish = FindProperty(objectId, BacnetPropertyIds.PROP_RELINQUISH_DEFAULT);
        if (relinquish == null)
            return ErrorCodes.NotForMe;

        var outOfService = FindProperty(objectId, BacnetPropertyIds.PROP_OUT_OF_SERVICE);
        if (outOfService == null)
            return ErrorCodes.NotForMe;

        var array = FindProperty(objectId, BacnetPropertyIds.PROP_PRIORITY_ARRAY);
        if (array == null)
            return ErrorCodes.NotForMe;

        var errorcode = ErrorCodes.GenericError;

        try
        {
            // If PROP_OUT_OF_SERVICE=True, value is accepted as is : http://www.bacnetwiki.com/wiki/index.php?title=Priority_Array                 
            if ((Boolean)outOfService.BacnetValue[0].Value && propertyId == BacnetPropertyIds.PROP_PRESENT_VALUE)
            {
                WriteProperty(objectId, BacnetPropertyIds.PROP_PRESENT_VALUE, value);
                return ErrorCodes.Good;
            }

            IList<BacnetValue> valueArray = null;

            // Thank's to Steve Karg
            // The 135-2016 text:
            // 19.2.2 Application Priority Assignments
            // All commandable objects within a device shall be configurable to accept writes to all priorities except priority 6
            if (priority == 6)
                return ErrorCodes.WriteAccessDenied;

            // http://www.chipkin.com/changing-the-bacnet-present-value-or-why-the-present-value-doesn%E2%80%99t-change/
            // Write Property PROP_PRESENT_VALUE : A value is placed in the PROP_PRIORITY_ARRAY
            if (propertyId == BacnetPropertyIds.PROP_PRESENT_VALUE)
            {
                errorcode = ErrorCodes.Good;

                valueArray = array.BacnetValue;
                if (value.Value == null)
                    valueArray[(Int32)priority - 1] = new BacnetValue(null);
                else
                    valueArray[(Int32)priority - 1] = value;
                array.BacnetValue = valueArray;
            }

            // Look on the priority Array to find the first value to be set in PROP_PRESENT_VALUE
            if (errorcode == ErrorCodes.Good)
            {

                var done = false;
                for (var i = 0; i < 16; i++)
                {
                    if (valueArray[i].Value == null)
                        continue;

                    WriteProperty(objectId, BacnetPropertyIds.PROP_PRESENT_VALUE, valueArray[i]);
                    done = true;
                    break;
                }

                if (done == false)  // Nothing in the array : PROP_PRESENT_VALUE = PROP_RELINQUISH_DEFAULT
                {
                    var defaultValue = relinquish.BacnetValue;
                    WriteProperty(objectId, BacnetPropertyIds.PROP_PRESENT_VALUE, defaultValue[0]);
                }
            }
        }
        catch
        {
            errorcode = ErrorCodes.GenericError;
        }

        return errorcode;
    }

    /// <summary>批量写入多个属性</summary>
    /// <param name="objectId">对象 ID</param>
    /// <param name="values">要写入的属性值列表</param>
    /// <returns>每个属性对应的错误码数组</returns>
    public ErrorCodes[] WritePropertyMultiple(BacnetObjectId objectId, ICollection<BacnetPropertyValue> values)
    {
        return values
            .Select(v => WriteProperty(objectId, (BacnetPropertyIds)v.property.propertyIdentifier, v.property.propertyArrayIndex, v.value))
            .ToArray();
    }

    /// <summary>将设备存储序列化为 XML 文件</summary>
    /// <param name="path">XML 文件路径</param>
    public void Save(String path)
    {
        var s = new XmlSerializer(typeof(DeviceStorage));
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        s.Serialize(fs, this);
    }

    /// <summary>从 XML 文件加载设备存储</summary>
    /// <param name="path">XML 文件路径（支持嵌入式资源或外部文件）</param>
    /// <param name="deviceId">可选的设备 ID，覆盖 XML 中的设备 ID</param>
    /// <returns>反序列化的设备存储实例</returns>
    public static DeviceStorage Load(String path, UInt32? deviceId = null)
    {
        StreamReader reader = null;

        if (File.Exists(path.GetFullPath()))
            reader = new StreamReader(path.GetFullPath());
        else
        {
            var assembly = Assembly.GetCallingAssembly();
            var ms = assembly.GetManifestResourceStream(path);

            // check if the xml file is an embedded resource
            if (ms != null) reader = new StreamReader(ms);
        }

        // if not check the external file
        if (reader == null)
            throw new Exception("No AppSettings found");

        var s = new XmlSerializer(typeof(DeviceStorage));

        using (reader)
        {
            var ret = (DeviceStorage)s.Deserialize(reader);

            //set device_id
            var obj = ret.FindObject(BacnetObjectTypes.OBJECT_DEVICE);
            if (obj != null)
                ret.DeviceId = obj.Instance;

            // use the deviceId in the Xml file or another one
            if (!deviceId.HasValue)
                return ret;

            ret.DeviceId = deviceId.Value;
            if (obj == null)
                return ret;

            // change the value
            obj.Instance = deviceId.Value;
            IList<BacnetValue> val = new[]
            {
                new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_OBJECT_ID, $"OBJECT_DEVICE:{deviceId.Value}")
            };

            ret.WriteProperty(new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE,
                Serialize.ASN1.BACNET_MAX_INSTANCE), BacnetPropertyIds.PROP_OBJECT_IDENTIFIER, 1, val, true);

            return ret;
        }
    }
}
#pragma warning restore CS1591 // 缺少对公共可见类型或成员的 XML 注释
