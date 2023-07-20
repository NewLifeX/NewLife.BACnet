using System.IO.BACnet;

namespace NewLife.BACnet.Protocols;

/// <summary>
/// BAC节点
/// </summary>
public class BacNode
{
    /// <summary>地址</summary>
    public BacnetAddress Address { get; set; }

    /// <summary>设备编号</summary>
    public UInt32 DeviceId { get; set; }

    ///// <summary>点位集合</summary>
    //public IList<BacnetObjectId> Ids { get; set; }

    /// <summary>属性集合</summary>
    public IList<BacProperty> Properties { get; set; }

    /// <summary>实例化</summary>
    /// <param name="adr"></param>
    /// <param name="deviceId"></param>
    public BacNode(BacnetAddress adr, UInt32 deviceId)
    {
        Address = adr;
        DeviceId = deviceId;
    }

    /// <summary>获取设备地址</summary>
    /// <param name="deviceId"></param>
    /// <returns></returns>
    public BacnetAddress GetAdd(UInt32 deviceId) => DeviceId == deviceId ? Address : null;

    /// <summary>已重载。</summary>
    /// <returns></returns>
    public override String ToString() => $"[{DeviceId}]{Address}";
}