using NewLife.IoT.Drivers;

namespace NewLife.BACnet.Drivers;

/// <summary>
/// BACnet节点
/// </summary>
public class BACnetNode : INode
{
    /// <summary>地址</summary>
    public String Address { get; set; }

    /// <summary>通道</summary>
    public IChannel Channel { get; set; }
}