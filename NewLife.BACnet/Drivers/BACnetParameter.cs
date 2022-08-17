using NewLife.IoT;
using NewLife.IoT.Drivers;

namespace NewLife.BACnet.Drivers;

/// <summary>
/// BACnet参数
/// </summary>
public class BACnetParameter : IDriverParameter
{
    /// <summary>地址</summary>
    public String Address { get; set; }
}