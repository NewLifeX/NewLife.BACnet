using NewLife.BACnet.Protocols;
using NewLife.IoT;
using NewLife.IoT.Drivers;

namespace NewLife.BACnet.Drivers;

/// <summary>
/// BACnet节点
/// </summary>
public class BACnetNode : INode
{
    /// <summary>驱动</summary>
    public IDriver Driver { get; set; }

    /// <summary>设备</summary>
    public IDevice Device { get; set; }

    /// <summary>参数</summary>
    public IDriverParameter Parameter { get; set; }

    /// <summary>设备编号</summary>
    public Int32 DeviceId { get; set; }

    /// <summary>BAC连接</summary>
    public BacClient Client { get; set; }
}