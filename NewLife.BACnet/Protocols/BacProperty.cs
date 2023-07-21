using System.IO.BACnet;

namespace NewLife.BACnet.Protocols;

/// <summary>属性</summary>
public class BacProperty
{
    #region 属性
    /// <summary>对象编号</summary>
    public BacnetObjectId ObjectId { get; set; }

    /// <summary>
    /// 点名
    /// </summary>
    public String Name { get; set; }

    /// <summary>
    /// 值
    /// </summary>
    public Object Value { get; set; }

    /// <summary>
    /// 值类型
    /// </summary>
    public Type Type { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    public String Description { get; set; }
    #endregion

    #region 构造
    /// <summary>实例化</summary>
    public BacProperty() { }

    /// <summary>实例化</summary>
    /// <param name="objectId"></param>
    public BacProperty(BacnetObjectId objectId)
    {
        ObjectId = objectId;

        Name = objectId.GetKey();
        Type = Parse(objectId.Type);
    }

    /// <summary>已重载。</summary>
    /// <returns></returns>
    public override String ToString() => Name ?? ObjectId.GetKey();
    #endregion

    #region 方法
    /// <summary>创建属性</summary>
    /// <param name="bv"></param>
    /// <returns></returns>
    public static BacProperty Create(BacnetValue bv)
    {
        //if (bv.Tag == BacnetApplicationTags.BACNET_APPLICATION_TAG_ERROR)
        //    throw new XException(bv.Value + "");
        if (bv.Tag == BacnetApplicationTags.BACNET_APPLICATION_TAG_ERROR)
            return null;

        var ss = ("" + bv.Value).Split(':');
        if (ss.Length < 2) return null;

        if (!Enum.TryParse<BacnetObjectTypes>(ss[0], out var otype) ||
            otype == BacnetObjectTypes.OBJECT_NOTIFICATION_CLASS ||
            otype == BacnetObjectTypes.OBJECT_DEVICE)
            return null;

        var bp = new BacProperty
        {
            ObjectId = new BacnetObjectId(otype, Convert.ToUInt32(ss[1]))
        };

        return bp;
    }

    /// <summary>创建属性</summary>
    /// <param name="pv"></param>
    /// <returns></returns>
    public static IEnumerable<BacProperty> Create(BacnetPropertyValue pv)
    {
        if (pv.value == null) yield break;

        foreach (var item in pv.value)
        {
            var bp = Create(item);
            if (bp != null) yield return bp;
        }
    }

    /// <summary>创建属性</summary>
    /// <param name="results"></param>
    /// <returns></returns>
    public static IEnumerable<BacProperty> Create(IList<BacnetReadAccessResult> results)
    {
        if (results == null) yield break;

        foreach (var rs in results)
        {
            var bp = new BacProperty { ObjectId = rs.objectIdentifier };
            bp.Fill(rs);
            yield return bp;

            //foreach (var item in rs.values)
            //{
            //    foreach (var elm in Create(item))
            //    {
            //        yield return elm;
            //    }
            //}
        }
    }

    /// <summary>填充</summary>
    /// <param name="result"></param>
    public void Fill(BacnetReadAccessResult result)
    {
        foreach (var elm in result.values)
        {
            if (elm.value == null || elm.value.Count == 0) continue;

            var bv = elm.value[0];
            if (bv.Tag == BacnetApplicationTags.BACNET_APPLICATION_TAG_ERROR)
                continue;

            switch ((BacnetPropertyIds)elm.property.propertyIdentifier)
            {
                case BacnetPropertyIds.PROP_DESCRIPTION:
                    Description = bv.ToString()?.Trim();
                    break;
                case BacnetPropertyIds.PROP_OBJECT_NAME:
                    Name = bv.ToString()?.Trim();
                    break;
                case BacnetPropertyIds.PROP_PRESENT_VALUE:
                    Value = bv.Value;
                    Type ??= bv.Value.GetType();
                    break;
            }
        }
    }

    private static Type Parse(BacnetObjectTypes type)
    {
        switch (type)
        {
            case BacnetObjectTypes.OBJECT_ANALOG_INPUT:
            case BacnetObjectTypes.OBJECT_ANALOG_OUTPUT:
            case BacnetObjectTypes.OBJECT_ANALOG_VALUE:
                return typeof(Single);
            case BacnetObjectTypes.OBJECT_BINARY_INPUT:
            case BacnetObjectTypes.OBJECT_BINARY_OUTPUT:
            case BacnetObjectTypes.OBJECT_BINARY_VALUE:
                return typeof(Boolean);
            case BacnetObjectTypes.OBJECT_CALENDAR:
                break;
            case BacnetObjectTypes.OBJECT_COMMAND:
                return typeof(UInt32);
            case BacnetObjectTypes.OBJECT_DEVICE:
                break;
            case BacnetObjectTypes.OBJECT_EVENT_ENROLLMENT:
                break;
            case BacnetObjectTypes.OBJECT_FILE:
                break;
            case BacnetObjectTypes.OBJECT_GROUP:
                break;
            case BacnetObjectTypes.OBJECT_LOOP:
                break;
            case BacnetObjectTypes.OBJECT_MULTI_STATE_INPUT:
            case BacnetObjectTypes.OBJECT_MULTI_STATE_OUTPUT:
                return typeof(UInt32);
            case BacnetObjectTypes.OBJECT_NOTIFICATION_CLASS:
                break;
            case BacnetObjectTypes.OBJECT_PROGRAM:
                break;
            case BacnetObjectTypes.OBJECT_SCHEDULE:
                break;
            case BacnetObjectTypes.OBJECT_AVERAGING:
                break;
            case BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE:
                break;
            case BacnetObjectTypes.OBJECT_TRENDLOG:
                break;
            case BacnetObjectTypes.OBJECT_LIFE_SAFETY_POINT:
                break;
            case BacnetObjectTypes.OBJECT_LIFE_SAFETY_ZONE:
                break;
            case BacnetObjectTypes.OBJECT_ACCUMULATOR:
                break;
            case BacnetObjectTypes.OBJECT_PULSE_CONVERTER:
                break;
            case BacnetObjectTypes.OBJECT_EVENT_LOG:
                break;
            case BacnetObjectTypes.OBJECT_GLOBAL_GROUP:
                break;
            case BacnetObjectTypes.OBJECT_TREND_LOG_MULTIPLE:
                break;
            case BacnetObjectTypes.OBJECT_LOAD_CONTROL:
                break;
            case BacnetObjectTypes.OBJECT_STRUCTURED_VIEW:
                break;
            case BacnetObjectTypes.OBJECT_ACCESS_DOOR:
                break;
            case BacnetObjectTypes.OBJECT_TIMER:
                break;
            case BacnetObjectTypes.OBJECT_ACCESS_CREDENTIAL:
                break;
            case BacnetObjectTypes.OBJECT_ACCESS_POINT:
                break;
            case BacnetObjectTypes.OBJECT_ACCESS_RIGHTS:
                break;
            case BacnetObjectTypes.OBJECT_ACCESS_USER:
                break;
            case BacnetObjectTypes.OBJECT_ACCESS_ZONE:
                break;
            case BacnetObjectTypes.OBJECT_CREDENTIAL_DATA_INPUT:
                break;
            case BacnetObjectTypes.OBJECT_NETWORK_SECURITY:
                break;
            case BacnetObjectTypes.OBJECT_BITSTRING_VALUE:
                break;
            case BacnetObjectTypes.OBJECT_CHARACTERSTRING_VALUE:
                break;
            case BacnetObjectTypes.OBJECT_DATE_PATTERN_VALUE:
                break;
            case BacnetObjectTypes.OBJECT_DATE_VALUE:
                break;
            case BacnetObjectTypes.OBJECT_DATETIME_PATTERN_VALUE:
                break;
            case BacnetObjectTypes.OBJECT_DATETIME_VALUE:
                break;
            case BacnetObjectTypes.OBJECT_INTEGER_VALUE:
                break;
            case BacnetObjectTypes.OBJECT_LARGE_ANALOG_VALUE:
                break;
            case BacnetObjectTypes.OBJECT_OCTETSTRING_VALUE:
                break;
            case BacnetObjectTypes.OBJECT_POSITIVE_INTEGER_VALUE:
                break;
            case BacnetObjectTypes.OBJECT_TIME_PATTERN_VALUE:
                break;
            case BacnetObjectTypes.OBJECT_TIME_VALUE:
                break;
            case BacnetObjectTypes.OBJECT_NOTIFICATION_FORWARDER:
                break;
            case BacnetObjectTypes.OBJECT_ALERT_ENROLLMENT:
                break;
            case BacnetObjectTypes.OBJECT_CHANNEL:
                break;
            case BacnetObjectTypes.OBJECT_LIGHTING_OUTPUT:
                break;
            case BacnetObjectTypes.OBJECT_BINARY_LIGHTING_OUTPUT:
                break;
            case BacnetObjectTypes.OBJECT_NETWORK_PORT:
                break;
            case BacnetObjectTypes.OBJECT_ELEVATOR_GROUP:
                break;
            case BacnetObjectTypes.OBJECT_ESCALATOR:
                break;
            case BacnetObjectTypes.OBJECT_LIFT:
                break;
            case BacnetObjectTypes.OBJECT_STAGING:
                break;
            case BacnetObjectTypes.OBJECT_AUDIT_LOG:
                break;
            case BacnetObjectTypes.OBJECT_AUDIT_REPORTER:
                break;
            case BacnetObjectTypes.OBJECT_COLOR:
                break;
            case BacnetObjectTypes.OBJECT_COLOR_TEMPERATURE:
                break;
            case BacnetObjectTypes.OBJECT_PROPRIETARY_MIN:
                break;
            case BacnetObjectTypes.OBJECT_PROPRIETARY_MAX:
                break;
            case BacnetObjectTypes.MAX_BACNET_OBJECT_TYPE:
                break;
            case BacnetObjectTypes.MAX_ASHRAE_OBJECT_TYPE:
                break;
            default:
                break;
        }

        return null;
    }
    #endregion
}