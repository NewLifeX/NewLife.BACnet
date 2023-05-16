using System.IO.BACnet;
using NewLife.IoT.ThingModels;
using NewLife.Log;
using NewLife.Reflection;
using NewLife.Threading;

namespace NewLife.BACnet.Protocols;

/// <summary>BACnet客户端。默认UDP协议</summary>
public class BacClient : DisposeBase
{
    #region 属性
    /// <summary>地址</summary>
    public String Address { get; set; }

    /// <summary>共享端口。节点在该端口上监听广播数据，多进程共享，默认0xBAC0，即47808</summary>
    public Int32 Port { get; set; } = 0xBAC0;

    /// <summary>传输层</summary>
    public IBacnetTransport Transport { get; set; }

    /// <summary>设备编号</summary>
    public Int32 DeviceId { get; set; }

    private readonly List<BacNode> _nodes = new();
    private BacnetClient _client;
    #endregion

    #region 构造
    /// <summary>释放资源</summary>
    /// <param name="disposing"></param>
    protected override void Dispose(Boolean disposing)
    {
        base.Dispose(disposing);

        _client.TryDispose();
    }
    #endregion

    #region 方法
    /// <summary>打开连接</summary>
    public void Open()
    {
        Transport ??= new BacnetIpUdpProtocolTransport(Port);

        var client = new BacnetClient(Transport);
        client.OnIam += OnIam;

        // 监听端口
        client.Start();

        if (Transport is BacnetIpUdpProtocolTransport udp)
            WriteLog("本地：{0}", udp.LocalEndPoint);

        // 广播“你是谁”
        client.WhoIs();

        _client = client;

        _timer = new TimerX(DoScan, null, 0, 10_000) { Async = true };
    }

    /// <summary>获取指定地址的节点</summary>
    /// <param name="address"></param>
    /// <returns></returns>
    public BacNode GetNode(String address) => _nodes.FirstOrDefault(e => e.Address + "" == address);
    #endregion

    #region 设备扫描管理
    private TimerX _timer;

    private void DoScan(Object state)
    {
        foreach (var node in _nodes)
        {
            if (node.Address != null)
            {
                //var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, node.DeviceId);
                //var rs = _client.ReadPropertyRequest(node.Address, oid, BacnetPropertyIds.PROP_OBJECT_LIST, out var list);
                //if (rs)
                //{
                //    var count = rs.ToInt() + 1;

                //    var bobj = new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, node.DeviceId);

                //    node.Properties.Clear();
                //    var prs = new List<BacnetPropertyReference>();
                //    for (var i = 0; i < count; i++)
                //    {
                //        var property = new BacnetPropertyReference((UInt32)BacnetPropertyIds.PROP_OBJECT_LIST, (UInt32)i);
                //        prs.Add(property);
                //    }
                //    for (var i = 0; i < count;)
                //    {
                //        var batch = prs.Skip(i).Take(16).ToList();
                //        if (batch.Count == 0) break;

                //        var results = _client.ReadPropertyMultipleRequest(node.Address, bobj, batch);
                //        if (results != null)
                //        {
                //            var ps = BacProperty.Create(results);
                //            node.Properties.AddRange(ps);
                //        }

                //        i += batch.Count;
                //    }
                //}
            }
        }
    }
    #endregion

    #region 读写方法
    /// <summary>读取</summary>
    /// <param name="node"></param>
    /// <param name="points"></param>
    /// <returns></returns>
    public Dictionary<String, Object> Read(BacNode node, params IPoint[] points)
    {
        var addr = node.Address;
        if (addr == null || node.Properties == null) return null;

        var dic = new Dictionary<String, Object>();

        var rList = new List<BacnetPropertyReference> {
            new BacnetPropertyReference((UInt32)BacnetPropertyIds.PROP_DESCRIPTION, UInt32.MaxValue),
            new BacnetPropertyReference((UInt32)BacnetPropertyIds.PROP_REQUIRED, UInt32.MaxValue),
            new BacnetPropertyReference((UInt32)BacnetPropertyIds.PROP_OBJECT_NAME, UInt32.MaxValue),
            new BacnetPropertyReference((UInt32)BacnetPropertyIds.PROP_PRESENT_VALUE, UInt32.MaxValue)
        };

        var pt = node.Properties[0];
        if (points.Length == 1)
        {
            if (_client.ReadPropertyRequest(addr, pt.ObjectId, BacnetPropertyIds.PROP_PRESENT_VALUE, out var rs))
            {
            }
        }
        else
        {
            var results = new List<BacnetReadAccessResult>();
            for (var i = 0; i < node.Properties.Count; i++)
            {
                var batch = node.Properties.Skip(i).Take(16).ToList();
                var properties = batch.Select(e => new BacnetReadAccessSpecification(e.ObjectId, rList)).ToList();
                if (_client.ReadPropertyMultipleRequest(addr, pt.ObjectId, rList, out var values, 0))
                {
                    results.AddRange(values);
                }
            }

            foreach (var item in results)
            {
                var oid = item.objectIdentifier;
                var pi = node.Properties.FirstOrDefault(t => t.ObjectId.Instance == oid.Instance && t.ObjectId.Type == oid.Type);
                if (pi != null)
                {
                    pi.Fill(item);

                    var key = $"{pi.ObjectId.Instance}_{(Int32)pi.ObjectId.Type}";
                    var point = points.FirstOrDefault(e => e.Address == key);
                    if (point != null) dic[point.Name] = pi.Value;
                }
            }
        }

        return dic;
    }

    /// <summary>写入</summary>
    /// <param name="addr"></param>
    /// <param name="point"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    public virtual Object Write(String addr, IPoint point, Object value)
    {
        var bacnet = _nodes.FirstOrDefault(e => e.Address + "" == addr);

        var ss = point.Address.Split('_');
        var oid = ss[0].ToInt();
        var type = (BacnetObjectTypes)ss[1].ToInt();

        BacProperty pi = null;
        if (ss.Length == 1)
            pi = bacnet?.Properties.FirstOrDefault(t => t.Name == point.Address);
        else if (ss.Length == 2)
            pi = bacnet?.Properties.FirstOrDefault(t => t.ObjectId.Instance == oid && t.ObjectId.Type == type);

        var values = new[] { new BacnetValue(value.ChangeType(pi.Type)) };
        _client.WritePropertyRequest(bacnet.Address, pi.ObjectId, BacnetPropertyIds.PROP_PRESENT_VALUE, values);

        return null;
    }

    private void OnIam(BacnetClient sender, BacnetAddress addr, UInt32 deviceId, UInt32 maxAPDU, BacnetSegmentations segmentation, UInt16 vendorId)
    {
        lock (_nodes)
        {
            foreach (var bn in _nodes)
            {
                XTrace.WriteLine("OnIam [{0}]: {1}", bn.Address, bn.DeviceId);
                if (bn.GetAdd(deviceId) != null) return;
            }

            _nodes.Add(new BacNode(addr, deviceId));
        }

    }
    #endregion

    #region 日志
    /// <summary>日志</summary>
    public ILog Log { get; set; }

    /// <summary>写日志</summary>
    /// <param name="format"></param>
    /// <param name="args"></param>
    public void WriteLog(String format, params Object[] args) => Log?.Info(format, args);
    #endregion
}