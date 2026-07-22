using System.Net;
using System.Text.RegularExpressions;
using NewLife.Log;

namespace System.IO.BACnet;

/// <summary>BACnet IPv6 虚拟链路控制层（BVLCV6），处理 BACnet/IPv6 帧编解码和 BBMD/FD 管理。</summary>
/// <remarks>基于 Addendum 135-2012aj-4 实现。部分代码为 IPv4 版适配移植，IPv6 需实际网络环境验证。</remarks>
public class BVLCV6
{
    /// <summary>BACnet/IPv6 BVLL 类型标识（0x82）</summary>
    public const Byte BVLL_TYPE_BACNET_IPV6 = 0x82;
    /// <summary>BVLC 头长度（10 字节），广播帧可为 7 字节</summary>
    public const Byte BVLC_HEADER_LENGTH = 10;
    /// <summary>BVLC 最大 APDU 长度（1476 字节）</summary>
    public const BacnetMaxAdpu BVLC_MAX_APDU = BacnetMaxAdpu.MAX_APDU1476;

    private Boolean _bbmdFdServiceActivated;
    private readonly List<IPEndPoint> _bbmds = new();
    private readonly BacnetAddress _broadcastAdd;
    private readonly List<Regex> _autorizedFDR = new();
    // Two lists for optional BBMD activity
    private readonly List<KeyValuePair<IPEndPoint, DateTime>> _foreignDevices = new();
    private readonly BacnetIpV6UdpProtocolTransport _myTransport;

    /// <summary>是否使用随机虚拟 MAC 地址</summary>
    public Boolean RandomVmac;
    /// <summary>3 字节虚拟 MAC 地址（IPv6 特有）</summary>
    public Byte[] VMAC = new Byte[3];
    /// <summary>日志实例</summary>
    public ILog Log { get; set; } = XTrace.Log;

    /// <summary>初始化 BVLCV6 实例</summary>
    /// <param name="transport">关联的 IPv6 UDP 传输层</param>
    /// <param name="vMac">虚拟 MAC 地址（-1 表示随机生成，否则使用指定值的高 3 字节）</param>
    /// <remarks>随机 VMAC 时确保高位为 01xxxxxx；指定 VMAC 时高位强制为 0，唯一性由调用者保证。</remarks>
    public BVLCV6(BacnetIpV6UdpProtocolTransport transport, Int32 vMac)
    {
        _myTransport = transport;
        _broadcastAdd = _myTransport.GetBroadcastAddress();

        if (vMac == -1)
        {
            RandomVmac = true;
            new Random().NextBytes(VMAC);
            VMAC[0] = (Byte)((VMAC[0] & 0x7F) | 0x40); // ensure 01xxxxxx on the High byte    

            // Open with default interface specified, cannot send it or 
            // it will generate an uncheckable continuous local loopback
            if (!_myTransport.LocalEndPoint.ToString().Contains("[::]"))
                SendAddressResolutionRequest(VMAC);
            else
                RandomVmac = false; // back to false avoiding loop back
        }
        else // Device Id is the Vmac Id
        {
            VMAC[0] = (Byte)((vMac >> 16) & 0x3F); // ensure the 2 high bits are 0 on the High byte
            VMAC[1] = (Byte)((vMac >> 8) & 0xFF);
            VMAC[2] = (Byte)(vMac & 0xFF);
            // unicity is guaranteed by the end user !
        }
    }

    /// <summary>添加 Foreign Device 注册的 IP 规则（白名单）。规则为空时接受所有 IP。</summary>
    /// <param name="ipRule">正则表达式规则</param>
    public void AddFDRAutorisationRule(Regex ipRule) => _autorizedFDR.Add(ipRule);

    /// <summary>添加 BBMD 对等体，启动 BBMD/FD 服务</summary>
    /// <param name="bbmd">BBMD 端点。为 null 时仅启动 FD 活动。</param>
    public void AddBBMDPeer(IPEndPoint bbmd)
    {
        _bbmdFdServiceActivated = true;

        if (bbmd == null)
            return;

        lock (_bbmds)
            _bbmds.Add(bbmd);
    }

    // Add a FD to the table or renew it
    private void RegisterForeignDevice(IPEndPoint sender, Int32 ttl)
    {
        lock (_foreignDevices)
        {
            // remove it, if any
            _foreignDevices.Remove(_foreignDevices.Find(item => item.Key.Equals(sender)));
            // TTL + 30s grace period
            var Expiration = DateTime.Now.AddSeconds(ttl + 30);
            // add it
            if (_autorizedFDR.Count == 0) // No rules, accept all
            {
                _foreignDevices.Add(new KeyValuePair<IPEndPoint, DateTime>(sender, Expiration));
                return;
            }

            if (_autorizedFDR.Any(r => r.Match(sender.Address.ToString()).Success))
            {
                _foreignDevices.Add(new KeyValuePair<IPEndPoint, DateTime>(sender, Expiration));
                return;
            }

            Log.Info($"Rejected FDR registration, IP : {sender.Address}");
        }
    }

    // Send a Frame to each registered foreign devices, except the original sender
    private void SendToFDs(Byte[] buffer, Int32 msgLength, IPEndPoint epSender = null)
    {
        lock (_foreignDevices)
        {
            // remove oldest Device entries (Time expiration > TTL + 30s delay)
            _foreignDevices.Remove(_foreignDevices.Find(item => DateTime.Now > item.Value));
            // Send to all others, except the original sender
            foreach (var client in _foreignDevices)
            {
                if (!client.Key.Equals(epSender))
                    _myTransport.Send(buffer, msgLength, client.Key);
            }
        }
    }

    // Send a Frame to each registered BBMD
    private void SendToBBMDs(Byte[] buffer, Int32 msgLength)
    {
        lock (_bbmds)
        {
            foreach (var ep in _bbmds)
            {
                _myTransport.Send(buffer, msgLength, ep);
            }
        }
    }

    // Never tested
    private void Forward_NPDU(Byte[] buffer, Int32 msgLength, Boolean toGlobalBroadcast, IPEndPoint epSender, BacnetAddress bacSender)
    {
        // Forms the forwarded NPDU from the original (broadcast npdu), and send it to all

        // copy, 18 bytes shifted (orignal bvlc header : 7 bytes, new one : 25 bytes)
        var b = new Byte[msgLength + 18];
        // normaly only 'small' frames are present here, so no need to check if it's to big for Udp
        Array.Copy(buffer, 0, b, 18, msgLength);

        // 7 bytes for the BVLC Header, with the embedded 6 bytes IP:Port of the original sender
        First7BytesHeaderEncode(b, BacnetBvlcV6Functions.BVLC_FORWARDED_NPDU, msgLength + 18);
        // replace my Vmac by the orignal source vMac
        Array.Copy(bacSender.VMac, 0, b, 4, 3);
        // Add IpV6 endpoint
        Array.Copy(bacSender.adr, 0, b, 7, 18);
        // Send To BBMD
        SendToBBMDs(b, msgLength + 18);
        // Send To FD, except the sender
        SendToFDs(b, msgLength + 18, epSender);
        // Broadcast if required
        if (toGlobalBroadcast)
        {
            BacnetIpV6UdpProtocolTransport.Convert(_broadcastAdd, out var ep);
            _myTransport.Send(b, msgLength + 18, ep);
        }
    }

    private void First7BytesHeaderEncode(Byte[] b, BacnetBvlcV6Functions function, Int32 msgLength)
    {
        b[0] = BVLL_TYPE_BACNET_IPV6;
        b[1] = (Byte)function;
        b[2] = (Byte)((msgLength & 0xFF00) >> 8);
        b[3] = (Byte)((msgLength & 0x00FF) >> 0);
        Array.Copy(VMAC, 0, b, 4, 3);
    }

    // Send ack or nack
    private void SendResult(IPEndPoint sender, BacnetBvlcV6Results resultCode)
    {
        var b = new Byte[9];
        First7BytesHeaderEncode(b, BacnetBvlcV6Functions.BVLC_RESULT, 9);
        b[7] = (Byte)(((UInt16)resultCode & 0xFF00) >> 8);
        b[8] = (Byte)((UInt16)resultCode & 0xFF);
        _myTransport.Send(b, 9, sender);
    }

    /// <summary>向 BBMD 发送 Foreign Device 注册请求</summary>
    /// <param name="bbmd">目标 BBMD 端点</param>
    /// <param name="ttl">注册有效期（秒）</param>
    public void SendRegisterAsForeignDevice(IPEndPoint bbmd, Int16 ttl)
    {
        var b = new Byte[9];
        First7BytesHeaderEncode(b, BacnetBvlcV6Functions.BVLC_REGISTER_FOREIGN_DEVICE, 9);
        b[7] = (Byte)((ttl & 0xFF00) >> 8);
        b[8] = (Byte)(ttl & 0xFF);
        _myTransport.Send(b, 9, bbmd);
    }

    /// <summary>通过 BBMD 向远端网络发送 Who-Is 广播</summary>
    /// <param name="buffer">数据缓冲区</param>
    /// <param name="bbmd">目标 BBMD 端点</param>
    /// <param name="msgLength">消息长度</param>
    public void SendRemoteWhois(Byte[] buffer, IPEndPoint bbmd, Int32 msgLength)
    {
        // 7 bytes for the BVLC Header
        First7BytesHeaderEncode(buffer, BacnetBvlcV6Functions.BVLC_DISTRIBUTE_BROADCAST_TO_NETWORK, msgLength);
        _myTransport.Send(buffer, msgLength, bbmd);
    }

    // Send ack
    private void SendAddressResolutionAck(IPEndPoint sender, Byte[] vMacDest, BacnetBvlcV6Functions function)
    {
        var b = new Byte[10];
        First7BytesHeaderEncode(b, function, 10);
        Array.Copy(vMacDest, 0, b, 7, 3);
        _myTransport.Send(b, 10, sender);
    }

    // quite the same frame as the previous one
    private void SendAddressResolutionRequest(Byte[] vMacDest)
    {
        BacnetIpV6UdpProtocolTransport.Convert(_broadcastAdd, out var ep);

        var b = new Byte[10];
        First7BytesHeaderEncode(b, BacnetBvlcV6Functions.BVLC_ADDRESS_RESOLUTION, 10);
        Array.Copy(vMacDest, 0, b, 7, 3);
        _myTransport.Send(b, 10, ep);
    }

    /// <summary>编码 BVLCV6 帧头。由内部服务调用（当 BBMD 同时也是活动设备时）</summary>
    /// <param name="buffer">数据缓冲区</param>
    /// <param name="offset">起始偏移（通常为 0）</param>
    /// <param name="function">BVLCV6 功能码</param>
    /// <param name="msgLength">消息总长度</param>
    /// <param name="address">目标 BACnet 地址</param>
    /// <returns>BVLC 头长度（7 字节广播或 10 字节单播）</returns>
    public Int32 Encode(Byte[] buffer, Int32 offset, BacnetBvlcV6Functions function, Int32 msgLength, BacnetAddress address)
    {
        // offset always 0, we are the first after udp
        First7BytesHeaderEncode(buffer, function, msgLength);

        // BBMD service
        if (function == BacnetBvlcV6Functions.BVLC_ORIGINAL_BROADCAST_NPDU && _bbmdFdServiceActivated)
        {
            var me = _myTransport.LocalEndPoint;
            BacnetIpV6UdpProtocolTransport.Convert(me, out var bacme);
            Array.Copy(VMAC, bacme.VMac, 3);

            Forward_NPDU(buffer, msgLength, false, me, bacme); // send to all BBMDs and FDs

            return 7; // ready to send
        }

        if (function != BacnetBvlcV6Functions.BVLC_ORIGINAL_UNICAST_NPDU)
            return 0; // ?

        buffer[7] = address.VMac[0];
        buffer[8] = address.VMac[1];
        buffer[9] = address.VMac[2];
        return 10; // ready to send
    }

    /// <summary>解码 BVLCV6 帧头。每次收到 UDP 帧时调用。</summary>
    /// <param name="buffer">数据缓冲区</param>
    /// <param name="offset">起始偏移（通常为 0）</param>
    /// <param name="function">解码后的功能码</param>
    /// <param name="msgLength">解码后的消息长度</param>
    /// <param name="sender">发送端 IP 端点</param>
    /// <param name="remoteAddress">解码后的远程 BACnet 地址</param>
    /// <returns>BVLC 头长度（上层继续处理），-1（无效帧）</returns>
    public Int32 Decode(Byte[] buffer, Int32 offset, out BacnetBvlcV6Functions function, out Int32 msgLength,
        IPEndPoint sender, BacnetAddress remoteAddress)
    {
        // offset always 0, we are the first after udp
        // and a previous test by the caller guaranteed at least 4 bytes into the buffer

        function = (BacnetBvlcV6Functions)buffer[1];
        msgLength = (buffer[2] << 8) | (buffer[3] << 0);
        if (buffer[0] != BVLL_TYPE_BACNET_IPV6 || buffer.Length != msgLength) return -1;

        Array.Copy(buffer, 4, remoteAddress.VMac, 0, 3);

        switch (function)
        {
            case BacnetBvlcV6Functions.BVLC_RESULT:
                return 9; // only for the upper layers

            case BacnetBvlcV6Functions.BVLC_ORIGINAL_UNICAST_NPDU:
                return 10; // only for the upper layers

            case BacnetBvlcV6Functions.BVLC_ORIGINAL_BROADCAST_NPDU:
                // Send to FDs & BBMDs, not broadcast or it will be made twice !
                if (_bbmdFdServiceActivated)
                    Forward_NPDU(buffer, msgLength, false, sender, remoteAddress);
                return 7; // also for the upper layers

            case BacnetBvlcV6Functions.BVLC_ADDRESS_RESOLUTION:
                // need to verify that the VMAC is mine
                if (VMAC[0] == buffer[7] && VMAC[1] == buffer[8] && VMAC[2] == buffer[9])
                    // coming from myself ? avoid loopback
                    if (!_myTransport.LocalEndPoint.Equals(sender))
                        SendAddressResolutionAck(sender, remoteAddress.VMac,
                            BacnetBvlcV6Functions.BVLC_ADDRESS_RESOLUTION_ACK);
                return 0; // not for the upper layers

            case BacnetBvlcV6Functions.BVLC_FORWARDED_ADDRESS_RESOLUTION:
                // no need to verify the target VMAC, should be OK
                SendAddressResolutionAck(sender, remoteAddress.VMac,
                    BacnetBvlcV6Functions.BVLC_ADDRESS_RESOLUTION_ACK);
                return 0; // not for the upper layers

            case BacnetBvlcV6Functions.BVLC_ADDRESS_RESOLUTION_ACK: // adresse conflict
                if (VMAC[0] == buffer[4] && VMAC[1] == buffer[5] && VMAC[2] == buffer[6] && RandomVmac)
                {
                    new Random().NextBytes(VMAC);
                    VMAC[0] = (Byte)((VMAC[0] & 0x7F) | 0x40);
                    SendAddressResolutionRequest(VMAC);
                }
                return 0; // not for the upper layers

            case BacnetBvlcV6Functions.BVLC_VIRTUAL_ADDRESS_RESOLUTION:
                SendAddressResolutionAck(sender, remoteAddress.VMac,
                    BacnetBvlcV6Functions.BVLC_VIRTUAL_ADDRESS_RESOLUTION_ACK);
                return 0; // not for the upper layers

            case BacnetBvlcV6Functions.BVLC_VIRTUAL_ADDRESS_RESOLUTION_ACK:
                return 0; // not for the upper layers

            case BacnetBvlcV6Functions.BVLC_FORWARDED_NPDU:
                if (_myTransport.LocalEndPoint.Equals(sender)) return 0;

                // certainly TODO the same code I've put in the IPV4 implementation
                if (_bbmdFdServiceActivated && msgLength >= 25)
                {
                    Boolean ret;
                    lock (_bbmds)
                        ret = _bbmds.Exists(items => items.Equals(sender)); // verify sender presence in the table
                                                                            // avoid also loopback

                    if (ret) // message from a know BBMD address, sent to all FDs and broadcast
                    {
                        SendToFDs(buffer, msgLength); // send without modification
                                                      // Assume all BVLC_FORWARDED_NPDU are directly sent to me in the 
                                                      // unicast mode and not by the way of the multicast address
                                                      // If not, it's not really a big problem, devices on the local net will 
                                                      // receive two times the message (after all it's just WhoIs, Iam, ...)
                        BacnetIpV6UdpProtocolTransport.Convert(_broadcastAdd, out var ep);
                        _myTransport.Send(buffer, msgLength, ep);
                    }
                }
                return 25; // for the upper layers

            case BacnetBvlcV6Functions.BVLC_REGISTER_FOREIGN_DEVICE:
                if (_bbmdFdServiceActivated && msgLength == 9)
                {
                    var TTL = (buffer[7] << 8) + buffer[8]; // unit is second
                    RegisterForeignDevice(sender, TTL);
                    SendResult(sender, BacnetBvlcV6Results.SUCCESSFUL_COMPLETION); // ack
                }
                return 0; // not for the upper layers

            case BacnetBvlcV6Functions.BVLC_DELETE_FOREIGN_DEVICE_TABLE_ENTRY:
                return 0; // not for the upper layers    

            case BacnetBvlcV6Functions.BVLC_SECURE_BVLC:
                return 0; // not for the upper layers

            case BacnetBvlcV6Functions.BVLC_DISTRIBUTE_BROADCAST_TO_NETWORK: // Sent by a Foreign Device, not a BBMD
                if (_bbmdFdServiceActivated)
                {
                    // Send to FDs except the sender, BBMDs and broadcast
                    lock (_foreignDevices)
                    {
                        if (_foreignDevices.Exists(item => item.Key.Equals(sender))) // verify previous registration
                            Forward_NPDU(buffer, msgLength, true, sender, remoteAddress);
                        else
                            SendResult(sender, BacnetBvlcV6Results.DISTRIBUTE_BROADCAST_TO_NETWORK_NAK);
                    }
                }
                return 0; // not for the upper layers

            // error encoding function or experimental one
            default:
                return -1;
        }
    }
}
