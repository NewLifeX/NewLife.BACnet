namespace System.IO.BACnet;

/// <summary>BACnet/SC BVLC 功能码枚举。
/// 对应 BVLL_TYPE_BACNET_SC (0x83) 下的 SC 功能类型。</summary>
/// <remarks>
/// BACnet/SC (Secure Connect) 基于 WebSocket + TLS，使用独立的 BVLL 类型 0x83。
/// 功能码定义参考 ASHRAE 135-2016 Addendum bk 及后续标准。
/// </remarks>
public enum BacnetBvlcScFunctions : byte
{
    /// <summary>Hub 宣告自身（Hub → Nodes）</summary>
    BVLC_SC_ANNOUNCE_HUB_FUNCTION = 0x00,

    /// <summary>Node 连接至 Hub（Node → Hub）</summary>
    BVLC_SC_HUB_CONNECT = 0x01,

    /// <summary>Node 断开与 Hub 的连接</summary>
    BVLC_SC_HUB_DISCONNECT = 0x02,

    /// <summary>路由表公告（Hub → Nodes）</summary>
    BVLC_SC_ROUTING_TABLE_ADVERTISEMENT = 0x03,

    /// <summary>通过 Hub 转发的数据帧</summary>
    BVLC_SC_HUB_FUNCTION = 0x04,

    /// <summary>Node 间直连数据帧</summary>
    BVLC_SC_PEER_TO_PEER_FUNCTION = 0x05,
}
