namespace System.IO.BACnet;

/// <summary>BACnet/SC BVLC 结果码枚举。</summary>
public enum BacnetBvlcScResults : ushort
{
    /// <summary>成功</summary>
    SUCCESSFUL_COMPLETION = 0x0000,

    /// <summary>Hub 连接被拒绝</summary>
    HUB_CONNECT_NAK = 0x0010,

    /// <summary>断开失败</summary>
    HUB_DISCONNECT_NAK = 0x0020,

    /// <summary>路由表公告被拒绝</summary>
    ROUTING_TABLE_ADVERTISEMENT_NAK = 0x0030,
}
