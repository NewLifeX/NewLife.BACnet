using System.IO.BACnet;
using NewLife.IoT.ThingModels;

namespace NewLife.BACnet.Protocols;

public class ObjectPair
{
    public IPoint Point { get; set; }

    public BacnetObjectId ObjectId { get; set; }

    #region 辅助
    /// <summary>字符串转对象编号</summary>
    /// <param name="value"></param>
    /// <param name="oid"></param>
    /// <returns></returns>
    public static Boolean TryParse(String value, out BacnetObjectId oid)
    {
        oid = default;

        var ds = value?.SplitAsInt("_");
        if (ds == null) return false;

        var v = ds[0];
        var t = ds.Length > 1 ? ds[1] : 0;
        oid = new BacnetObjectId((BacnetObjectTypes)t, (UInt32)v);

        return true;
    }

    /// <summary>转字符串对象编号</summary>
    /// <param name="oid"></param>
    /// <returns></returns>
    public static String ToObjectId(BacnetObjectId oid) => $"{oid.instance}_{(Int32)oid.type}";
    #endregion
}
