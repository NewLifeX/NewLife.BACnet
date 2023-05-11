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

    /// <summary>属性</summary>
    public List<BacProperty> Properties { get; set; } = new();

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
}