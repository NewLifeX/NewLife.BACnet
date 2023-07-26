using System.ComponentModel;
using NewLife.IoT.Drivers;

namespace NewLife.BACnet.Drivers;

/// <summary>
/// BACnet参数
/// </summary>
public class BACnetParameter : IDriverParameter, IDriverParameterKey
{
    ///// <summary>地址</summary>
    //public String Address { get; set; }

    /// <summary>设备编号。在BACnet网络中的特定设备</summary>
    [Description("设备编号。在BACnet网络中的特定设备")]
    public Int32 DeviceId { get; set; }

    /// <summary>端口。默认0xBAC0，即47808</summary>
    [Description("端口。默认0xBAC0，即47808")]
    public Int32 Port { get; set; } = 0xBAC0;

    /// <summary>唯一标识</summary>
    /// <returns></returns>
    public String GetKey() => DeviceId + "";
}