using System.IO.BACnet;

namespace NewLife.BACnet.Protocols;

/// <summary>属性</summary>
public class BacProperty
{
    #region 属性
    /// <summary>对象编号</summary>
    public BacnetObjectId ObjectId { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    public String Description { get; set; }

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
    #endregion

    #region 方法
    /// <summary>创建属性</summary>
    /// <param name="bv"></param>
    /// <returns></returns>
    public static BacProperty Create(BacnetValue bv)
    {
        if (bv.Tag == BacnetApplicationTags.BACNET_APPLICATION_TAG_ERROR)
            throw new XException(bv.Value + "");

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
            foreach (var item in rs.values)
            {
                foreach (var elm in Create(item))
                {
                    yield return elm;
                }
            }
        }
    }

    /// <summary>填充</summary>
    /// <param name="result"></param>
    public void Fill(BacnetReadAccessResult result)
    {
        foreach (var elm in result.values)
        {
            if (elm.value == null || elm.value.Count == 0) continue;

            switch ((BacnetPropertyIds)elm.property.propertyIdentifier)
            {
                case BacnetPropertyIds.PROP_DESCRIPTION:
                    Description = elm.value[0].ToString()?.Trim();
                    break;
                case BacnetPropertyIds.PROP_OBJECT_NAME:
                    Name = elm.value[0].ToString()?.Trim();
                    break;
                case BacnetPropertyIds.PROP_PRESENT_VALUE:
                    Value = elm.value[0].Value;
                    Type = elm.value[0].Value.GetType();
                    break;
            }
        }
    }
    #endregion
}