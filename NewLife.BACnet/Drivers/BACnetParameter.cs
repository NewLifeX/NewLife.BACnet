using NewLife.IoT.Drivers;

namespace NewLife.BACnet.Drivers;

/// <summary>
/// BACnet参数
/// </summary>
public class BACnetParameter : IDriverParameter
{
    /// <summary>地址</summary>
    public String Address { get; set; }

    /// <summary>端口。默认0xBAC0，即47808</summary>
    public Int32 Port { get; set; } = 0xBAC0;

    /// <summary>设备编号</summary>
    public Int32 DeviceId { get; set; }
}