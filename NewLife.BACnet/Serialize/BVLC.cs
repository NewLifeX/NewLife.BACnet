using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using NewLife.Log;

namespace System.IO.BACnet.Serialize;

/// <summary>BACnet 虚拟链路控制层（BVLC），处理 BACnet/IP 帧的编解码和 BBMD 转发。</summary>
/// <remarks>
/// 实现 BACnet 标准中 BVLL（BACnet Virtual Link Layer）的编解码，
/// 包括 Original-Unicast-NPDU、Original-Broadcast-NPDU、Forwarded-NPDU 等帧类型。
/// 支持 BBMD（BACnet Broadcast Management Device）的广播转发和
/// Foreign Device 注册管理。
/// 特别感谢 VTS tool 和 Steve Karg 开源栈的参考实现。
/// </remarks>
public class BVLC
{
    /// <summary>BVLC 消息接收回调委托</summary>
    /// <param name="sender">发送端 IP 端点</param>
    /// <param name="function">BVLC 功能码</param>
    /// <param name="result">处理结果码</param>
    /// <param name="data">附加数据</param>
    public delegate void BVLCMessageReceiveHandler(IPEndPoint sender, BacnetBvlcFunctions function, BacnetBvlcResults result, object data);

    /// <summary>收到 BVLC 消息时触发</summary>
    public event BVLCMessageReceiveHandler MessageReceived;

    private readonly BacnetIpUdpProtocolTransport _myBbmdTransport;
    readonly string _broadcastAdd;
    private bool _bbmdFdServiceActivated;

    /// <summary>BACnet/IP BVLL 类型标识</summary>
    public const byte BVLL_TYPE_BACNET_IP = 0x81;

    /// <summary>BVLC 头长度（4 字节）</summary>
    public const byte BVLC_HEADER_LENGTH = 4;

    /// <summary>BVLC 最大 APDU 长度（1476 字节）</summary>
    public const BacnetMaxAdpu BVLC_MAX_APDU = BacnetMaxAdpu.MAX_APDU1476;

    // Two lists for optional BBMD activity
    readonly List<KeyValuePair<IPEndPoint, DateTime>> _foreignDevices = new();
    private readonly List<KeyValuePair<IPEndPoint, IPAddress>> _bbmds = new();

    // Contains the rules to accept FRD based on the IP adress
    // If empty it's equal to *.*.*.*, everyone allows
    private readonly List<Regex> _autorizedFdr = new();

    /// <summary>日志实例</summary>
    public ILog Log { get; set; } = XTrace.Log;

    /// <summary>初始化 BVLC 实例</summary>
    /// <param name="transport">关联的 UDP 传输层</param>
    public BVLC(BacnetIpUdpProtocolTransport transport)
    {
        _myBbmdTransport = transport;
        _broadcastAdd = _myBbmdTransport.GetBroadcastAddress().ToString().Split(':')[0];
    }

    /// <summary>获取已注册的 Foreign Device 列表</summary>
    /// <returns>以分号分隔的 "IP:Port" 列表</returns>
    public string FDList()
    {
        var sb = new StringBuilder();
        lock (_foreignDevices)
        {
            // remove oldest Device entries (Time expiration > TTL + 30s delay)
            _foreignDevices.Remove(_foreignDevices.Find(item => DateTime.Now > item.Value));

            foreach (var client in _foreignDevices)
                sb.Append($"{client.Key.Address}:{client.Key.Port};");
        }
        return sb.ToString();
    }

    /// <summary>添加 Foreign Device 注册的 IP 规则（白名单）</summary>
    /// <param name="ipRule">正则表达式规则。规则为空时接受所有 IP。</param>
    public void AddFDRAutorisationRule(Regex ipRule)
    {
        _autorizedFdr.Add(ipRule);
    }

    /// <summary>添加 BBMD 对等体，启动 BBMD/FD 服务</summary>
    /// <param name="bbmd">BBMD 端点。为 null 时仅启动 FD 活动。</param>
    /// <param name="mask">广播分布掩码</param>
    public void AddBBMDPeer(IPEndPoint bbmd, IPAddress mask)
    {
        _bbmdFdServiceActivated = true;

        if (bbmd == null)
            return;

        lock (_bbmds)
            _bbmds.Add(new KeyValuePair<IPEndPoint, IPAddress>(bbmd, mask));
    }

    // Add a FD to the table or renew it
    private void RegisterForeignDevice(IPEndPoint sender, int ttl)
    {
        lock (_foreignDevices)
        {
            // remove it, if any
            _foreignDevices.Remove(_foreignDevices.Find(item => item.Key.Equals(sender)));
            // TTL + 30s grace period
            var expiration = DateTime.Now.AddSeconds(ttl + 30);
            // add it
            if (_autorizedFdr.Count == 0) // No rules, accept all
            {
                _foreignDevices.Add(new KeyValuePair<IPEndPoint, DateTime>(sender, expiration));
                return;
            }
            foreach (var r in _autorizedFdr)
            {
                if (r.Match(sender.Address.ToString()).Success)
                {
                    _foreignDevices.Add(new KeyValuePair<IPEndPoint, DateTime>(sender, expiration));
                    return;
                }
            }
            Log.Info($"Rejected FDR registration, IP : {sender.Address}");
        }
    }

    // Send a Frame to each registered foreign devices, except the original sender
    private void SendToFDs(byte[] buffer, int msgLength, IPEndPoint ePsender = null)
    {
        lock (_foreignDevices)
        {
            // remove oldest Device entries (Time expiration > TTL + 30s delay)
            _foreignDevices.Remove(_foreignDevices.Find(item => DateTime.Now > item.Value));
            // Send to all others, except the original sender
            foreach (var client in _foreignDevices)
            {
                if (!client.Key.Equals(ePsender))
                    _myBbmdTransport.Send(buffer, msgLength, client.Key);
            }
        }
    }

    private static IPEndPoint BBMDSentAdd(IPEndPoint bbmd, IPAddress mask)
    {
        var bm = mask.GetAddressBytes();
        var bip = bbmd.Address.GetAddressBytes();

        /* annotation in Steve Karg bacnet stack :

        The B/IP address to which the Forwarded-NPDU message is
        sent is formed by inverting the broadcast distribution
        mask in the BDT entry and logically ORing it with the
        BBMD address of the same entry. This process
        produces either the directed broadcast address of the remote
        subnet or the unicast address of the BBMD on that subnet
        depending on the contents of the broadcast distribution
        mask. 

        remark from me :
           for instance remote BBMD 192.168.0.1 - mask 255.255.255.255
                messages are forward directly to 192.168.0.1
           remote BBMD 192.168.0.1 - mask 255.255.255.0
                messages are forward to 192.168.0.255, ie certainly the local broadcast
                address, but these datagrams are generaly destroy by the final IP router
         */

        for (var i = 0; i < bm.Length; i++)
            bip[i] = (byte)(bip[i] | ~bm[i]);

        return new IPEndPoint(new IPAddress(bip), bbmd.Port);
    }

    // Send a Frame to each registered BBMD except the original sender
    private void SendToBbmDs(byte[] buffer, int msgLength)
    {
        lock (_bbmds)
        {
            foreach (var e in _bbmds)
            {
                var endpoint = BBMDSentAdd(e.Key, e.Value);
                _myBbmdTransport.Send(buffer, msgLength, endpoint);
            }
        }
    }

    private static void First4BytesHeaderEncode(IList<byte> b, BacnetBvlcFunctions function, int msgLength)
    {
        b[0] = BVLL_TYPE_BACNET_IP;
        b[1] = (byte)function;
        b[2] = (byte)((msgLength & 0xFF00) >> 8);
        b[3] = (byte)((msgLength & 0x00FF) >> 0);
    }

    private void Forward_NPDU(byte[] buffer, int msgLength, bool toGlobalBroadcast, IPEndPoint ePsender)
    {
        // Forms the forwarded NPDU from the original one, and send it to all
        // orignal     - 4 bytes BVLC -  NPDU  - APDU
        // change to   -  10 bytes BVLC  -  NPDU  - APDU

        // copy, 6 bytes shifted
        var b = new byte[msgLength + 6];    // normaly only 'small' frames are present here, so no need to check if it's to big for Udp
        Array.Copy(buffer, 0, b, 6, msgLength);

        // 10 bytes for the BVLC Header, with the embedded 6 bytes IP:Port of the original sender
        First4BytesHeaderEncode(b, BacnetBvlcFunctions.BVLC_FORWARDED_NPDU, msgLength + 6);
        BacnetIpUdpProtocolTransport.Convert(ePsender, out var bacSender); // to embbed in the forward BVLC header
        for (var i = 0; i < bacSender.adr.Length; i++)
            b[4 + i] = bacSender.adr[i];

        // To BBMD
        SendToBbmDs(b, msgLength + 6);
        // To FD, except the sender
        SendToFDs(b, msgLength + 6, ePsender);
        // Broadcast if required
        if (toGlobalBroadcast)
            _myBbmdTransport.Send(b, msgLength + 6, new IPEndPoint(IPAddress.Parse(_broadcastAdd), _myBbmdTransport.SharedPort));
    }

    // Send ack or nack
    private void SendResult(IPEndPoint sender, BacnetBvlcResults resultCode)
    {
        var b = new byte[6];
        First4BytesHeaderEncode(b, BacnetBvlcFunctions.BVLC_RESULT, 6);
        b[4] = (byte)(((ushort)resultCode & 0xFF00) >> 8);
        b[5] = (byte)((ushort)resultCode & 0xFF);

        _myBbmdTransport.Send(b, 6, sender);
    }

    /// <summary>向 BBMD 发送 Foreign Device 注册请求</summary>
    /// <param name="bbmd">目标 BBMD 端点</param>
    /// <param name="ttl">注册有效期（秒）</param>
    public void SendRegisterAsForeignDevice(IPEndPoint bbmd, short ttl)
    {
        var b = new byte[6];
        First4BytesHeaderEncode(b, BacnetBvlcFunctions.BVLC_REGISTER_FOREIGN_DEVICE, 6);
        b[4] = (byte)((ttl & 0xFF00) >> 8);
        b[5] = (byte)(ttl & 0xFF);
        _myBbmdTransport.Send(b, 6, bbmd);
    }

    /// <summary>请求读取 BBMD 的广播分布表（BDT）</summary>
    /// <param name="bbmd">目标 BBMD 端点</param>
    public void SendReadBroadCastTable(IPEndPoint bbmd)
    {
        var b = new byte[4];
        First4BytesHeaderEncode(b, BacnetBvlcFunctions.BVLC_READ_BROADCAST_DIST_TABLE, 4);
        _myBbmdTransport.Send(b, 4, bbmd);
    }

    /// <summary>请求读取 BBMD 的 Foreign Device 表（FDT）</summary>
    /// <param name="bbmd">目标 BBMD 端点</param>
    public void SendReadFDRTable(IPEndPoint bbmd)
    {
        var b = new byte[4];
        First4BytesHeaderEncode(b, BacnetBvlcFunctions.BVLC_READ_FOREIGN_DEVICE_TABLE, 4);
        _myBbmdTransport.Send(b, 4, bbmd);
    }

    /// <summary>写入 BBMD 的广播分布表（BDT）</summary>
    /// <param name="bbmd">目标 BBMD 端点</param>
    /// <param name="entries">BDT 条目列表，每个条目包含 BBMD 端点 + 分布掩码</param>
    public void SendWriteBroadCastTable(IPEndPoint bbmd, List<Tuple<IPEndPoint, IPAddress>> entries)
    {
        var b = new byte[4 + 10 * entries.Count];
        First4BytesHeaderEncode(b, BacnetBvlcFunctions.BVLC_WRITE_BROADCAST_DISTRIBUTION_TABLE, 4 + 10 * entries.Count);

        for (var i = 0; i < entries.Count; i++)
        {
            Array.Copy(entries[i].Item1.Address.GetAddressBytes(), 0, b, 4 + i * 10, 4);
            b[8 + i * 10] = (byte)(entries[i].Item1.Port >> 8);
            b[9 + i * 10] = (byte)(entries[i].Item1.Port & 0xFF);
            Array.Copy(entries[i].Item2.GetAddressBytes(), 0, b, 10 + i * 10, 4);
        }

        _myBbmdTransport.Send(b, 4 + 10 * entries.Count, bbmd);
    }

    /// <summary>请求 BBMD 删除指定的 Foreign Device 条目</summary>
    /// <param name="bbmd">目标 BBMD 端点</param>
    /// <param name="foreignDevice">要删除的 Foreign Device 端点</param>
    public void SendDeleteForeignDeviceEntry(IPEndPoint bbmd, IPEndPoint foreignDevice)
    {
        var b = new byte[4 + 6];
        First4BytesHeaderEncode(b, BacnetBvlcFunctions.BVLC_READ_FOREIGN_DEVICE_TABLE, 4 + 6);
        Array.Copy(foreignDevice.Address.GetAddressBytes(), 0, b, 4, 4);
        b[8] = (byte)(foreignDevice.Port >> 8);
        b[9] = (byte)(foreignDevice.Port & 0xFF);
        _myBbmdTransport.Send(b, 4 + 6, bbmd);
    }

    /// <summary>通过 BBMD 向远端网络发送 Who-Is 广播</summary>
    /// <param name="buffer">数据缓冲区</param>
    /// <param name="bbmd">目标 BBMD 端点</param>
    /// <param name="msgLength">消息长度</param>
    public void SendRemoteWhois(byte[] buffer, IPEndPoint bbmd, int msgLength)
    {
        Encode(buffer, 0, BacnetBvlcFunctions.BVLC_DISTRIBUTE_BROADCAST_TO_NETWORK, msgLength);
        _myBbmdTransport.Send(buffer, msgLength, bbmd);

    }

    /// <summary>编码 BVLC 头。内部服务（BBMD 自身也是活动设备时）调用此方法。</summary>
    /// <param name="buffer">数据缓冲区</param>
    /// <param name="offset">起始偏移（通常为 0）</param>
    /// <param name="function">BVLC 功能码</param>
    /// <param name="msgLength">消息总长度</param>
    /// <returns>BVLC 头长度（4 字节）</returns>
    public int Encode(byte[] buffer, int offset, BacnetBvlcFunctions function, int msgLength)
    {
        // offset always 0, we are the first after udp

        // do the job
        First4BytesHeaderEncode(buffer, function, msgLength);

        // optional BBMD service
        if (_bbmdFdServiceActivated && function == BacnetBvlcFunctions.BVLC_ORIGINAL_BROADCAST_NPDU)
        {
            var me = _myBbmdTransport.LocalEndPoint;
            // just sometime working, enable to get the local ep, always 0.0.0.0 if the socket is open with
            // System.Net.IPAddress.Any
            // So in this case don't send a bad message
            if (me.Address.ToString() != "0.0.0.0")
                Forward_NPDU(buffer, msgLength, false, me);   // send to all BBMDs and FDs
        }
        return 4; // ready to send
    }

    /// <summary>解码 BVLC 头。每次收到 UDP 帧时调用。</summary>
    /// <param name="buffer">数据缓冲区</param>
    /// <param name="offset">起始偏移（通常为 0）</param>
    /// <param name="function">解码后的 BVLC 功能码</param>
    /// <param name="msgLength">解码后的消息长度</param>
    /// <param name="sender">发送端 IP 端点</param>
    /// <returns>BVLC 头长度（上层继续处理），0（已内部处理），-1（无效帧）</returns>
    public int Decode(byte[] buffer, int offset, out BacnetBvlcFunctions function, out int msgLength, IPEndPoint sender)
    {
        // offset always 0, we are the first after udp
        // and a previous test by the caller guaranteed at least 4 bytes into the buffer

        function = (BacnetBvlcFunctions)buffer[1];
        msgLength = (buffer[2] << 8) | (buffer[3] << 0);
        if (buffer[0] != BVLL_TYPE_BACNET_IP || buffer.Length != msgLength) return -1;

        switch (function)
        {
            case BacnetBvlcFunctions.BVLC_RESULT:
                var resultCode = (buffer[4] << 8) + buffer[5];
                MessageReceived?.Invoke(sender, function, (BacnetBvlcResults)resultCode, null);
                return 0;   // not for the upper layers

            case BacnetBvlcFunctions.BVLC_ORIGINAL_UNICAST_NPDU:
                return 4;   // only for the upper layers

            case BacnetBvlcFunctions.BVLC_ORIGINAL_BROADCAST_NPDU: // Normaly received in an IP local or global broadcast packet
                                                                   // Send to FDs & BBMDs, not broadcast or it will be made twice !
                if (_bbmdFdServiceActivated)
                    Forward_NPDU(buffer, msgLength, false, sender);
                return 4;   // also for the upper layers

            case BacnetBvlcFunctions.BVLC_FORWARDED_NPDU:   // Sent only by a BBMD, broadcast on it network, or broadcast demand by one of it's FDs
                if (_bbmdFdServiceActivated && msgLength >= 10)
                {
                    bool ret;
                    lock (_bbmds)
                        ret = _bbmds.Exists(items => items.Key.Address.Equals(sender.Address));    // verify sender (@ not Port!) presence in the table

                    if (ret)    // message from a know BBMD address, sent to all FDs and broadcast
                    {
                        SendToFDs(buffer, msgLength);  // send without modification

                        // Assume all BVLC_FORWARDED_NPDU are directly sent to me in the 
                        // unicast mode and not by the way of the local broadcast address
                        // ie my mask must be 255.255.255.255 in the others BBMD tables
                        // If not, it's not really a big problem, devices on the local net will 
                        // receive two times the message (after all it's just WhoIs, Iam, ...)
                        _myBbmdTransport.Send(buffer, msgLength, new IPEndPoint(IPAddress.Parse(_broadcastAdd), _myBbmdTransport.SharedPort));
                    }
                }

                return 10;  // also for the upper layers

            case BacnetBvlcFunctions.BVLC_DISTRIBUTE_BROADCAST_TO_NETWORK:  // Sent by a Foreign Device, not a BBMD
                if (_bbmdFdServiceActivated)
                {
                    // Send to FDs except the sender, BBMDs and broadcast
                    lock (_foreignDevices)
                    {
                        if (_foreignDevices.Exists(item => item.Key.Equals(sender))) // verify previous registration
                            Forward_NPDU(buffer, msgLength, true, sender);
                        else
                            SendResult(sender, BacnetBvlcResults.BVLC_RESULT_DISTRIBUTE_BROADCAST_TO_NETWORK_NAK);
                    }
                }
                return 0;   // not for the upper layers

            case BacnetBvlcFunctions.BVLC_REGISTER_FOREIGN_DEVICE:
                if (_bbmdFdServiceActivated && msgLength == 6)
                {
                    var ttl = (buffer[4] << 8) + buffer[5]; // unit is second
                    RegisterForeignDevice(sender, ttl);
                    SendResult(sender, BacnetBvlcResults.BVLC_RESULT_SUCCESSFUL_COMPLETION);  // ack
                }
                return 0;  // not for the upper layers

            // We don't care about Read/Write operation in the BBMD/FDR tables (who realy use it ?)
            case BacnetBvlcFunctions.BVLC_READ_FOREIGN_DEVICE_TABLE:
                SendResult(sender, BacnetBvlcResults.BVLC_RESULT_READ_FOREIGN_DEVICE_TABLE_NAK);
                return 0;

            case BacnetBvlcFunctions.BVLC_DELETE_FOREIGN_DEVICE_TABLE_ENTRY:
                SendResult(sender, BacnetBvlcResults.BVLC_RESULT_DELETE_FOREIGN_DEVICE_TABLE_ENTRY_NAK);
                return 0;

            case BacnetBvlcFunctions.BVLC_READ_BROADCAST_DIST_TABLE:
                SendResult(sender, BacnetBvlcResults.BVLC_RESULT_READ_BROADCAST_DISTRIBUTION_TABLE_NAK);
                return 0;

            case BacnetBvlcFunctions.BVLC_WRITE_BROADCAST_DISTRIBUTION_TABLE:
            case BacnetBvlcFunctions.BVLC_READ_BROADCAST_DIST_TABLE_ACK:
                {
                    var nbEntries = (msgLength - 4) / 10;
                    var entries = new List<Tuple<IPEndPoint, IPAddress>>();

                    for (var i = 0; i < nbEntries; i++)
                    {
                        long add = BitConverter.ToInt32(buffer, 4 + i * 10);

                        Array.Reverse(buffer, 8 + i * 10, 2);
                        var port = BitConverter.ToUInt16(buffer, 8 + i * 10);

                        // new IPAddress(long) with 255.255.255.255 (ie -1) not OK
                        var mask = new byte[4];
                        Array.Copy(buffer, 10 + i * 10, mask, 0, 4);

                        var entry = Tuple.Create(new IPEndPoint(new IPAddress(add), port), new IPAddress(mask));
                        entries.Add(entry);
                    }

                    if (MessageReceived != null && function == BacnetBvlcFunctions.BVLC_READ_BROADCAST_DIST_TABLE_ACK)
                        MessageReceived(sender, function, BacnetBvlcResults.BVLC_RESULT_SUCCESSFUL_COMPLETION, entries);

                    // Today we don't accept it
                    if (function == BacnetBvlcFunctions.BVLC_WRITE_BROADCAST_DISTRIBUTION_TABLE)
                        SendResult(sender, BacnetBvlcResults.BVLC_RESULT_WRITE_BROADCAST_DISTRIBUTION_TABLE_NAK);

                    return 0;
                }

            case BacnetBvlcFunctions.BVLC_READ_FOREIGN_DEVICE_TABLE_ACK:
                {
                    var nbEntries = (msgLength - 4) / 10;
                    var entries = new List<Tuple<IPEndPoint, ushort, ushort>>();

                    for (var i = 0; i < nbEntries; i++)
                    {
                        long add = BitConverter.ToInt32(buffer, 4 + i * 10);

                        Array.Reverse(buffer, 8 + i * 10, 2);
                        var port = BitConverter.ToUInt16(buffer, 8 + i * 10);

                        Array.Reverse(buffer, 10 + i * 10, 2);
                        var ttl = BitConverter.ToUInt16(buffer, 10 + i * 10);
                        Array.Reverse(buffer, 12 + i * 10, 2);
                        var remainTtl = BitConverter.ToUInt16(buffer, 12 + i * 10);

                        var entry = Tuple.Create(new IPEndPoint(new IPAddress(add), port), ttl, remainTtl);
                        entries.Add(entry);
                    }

                    MessageReceived?.Invoke(sender, function, BacnetBvlcResults.BVLC_RESULT_SUCCESSFUL_COMPLETION, entries);
                    return 0;
                }

            // error encoding function or experimental one
            default:
                return -1;
        }
    }
}
