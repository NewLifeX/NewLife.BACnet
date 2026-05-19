using NewLife.BACnet.Protocols;
using NewLife.IoT;
using NewLife.IoT.Drivers;

namespace NewLife.BACnet.Drivers;

/// <summary>BACnet 驱动节点</summary>
/// <remarks>每个节点对应一台 BACnet 逻辑设备，多节点共享同一个 BacClient 连接实例。</remarks>
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

    /// <summary>是否已连接。反映底层 BacClient 的连接状态</summary>
    public Boolean IsConnected => Client != null;
}