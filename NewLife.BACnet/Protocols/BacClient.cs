using System.IO.BACnet;
using System.Security.Cryptography;
using System.Xml.Linq;
using NewLife.IoT.Drivers;
using NewLife.IoT.ThingModels;
using NewLife.Log;
using NewLife.Reflection;
using NewLife.Threading;

namespace NewLife.BACnet.Protocols;

/// <summary>BACnet客户端。默认UDP协议</summary>
public class BacClient : DisposeBase, ITracerFeature, ILogFeature
{
    #region 属性
    /// <summary>地址</summary>
    public String Address { get; set; }

    /// <summary>共享端口。节点在该端口上监听广播数据，多进程共享，默认0xBAC0，即47808</summary>
    public Int32 Port { get; set; } = 0xBAC0;

    /// <summary>传输层</summary>
    public IBacnetTransport Transport { get; set; }

    /// <summary>目标设备编号。仅处理该节点，默认0接受所有节点</summary>
    public Int32 DeviceId { get; set; }

    /// <summary>是否活跃</summary>
    public Boolean Active { get; set; }

    private readonly List<BacNode> _nodes = new();
    /// <summary>节点集合</summary>
    public IList<BacNode> Nodes => _nodes;

    private BacnetClient _client;
    #endregion

    #region 构造
    /// <summary>释放资源</summary>
    /// <param name="disposing"></param>
    protected override void Dispose(Boolean disposing)
    {
        base.Dispose(disposing);

        _timer.TryDispose();
        _client.TryDispose();
    }
    #endregion

    #region 方法
    /// <summary>打开连接</summary>
    public void Open()
    {
        if (Active) return;

        using var span = Tracer?.NewSpan("bac:Open", new { Port, DeviceId, Address });
        try
        {
            Transport ??= new BacnetIpUdpProtocolTransport(Port) { Tracer = Tracer };

            var client = new BacnetClient(Transport) { Tracer = Tracer };
            client.OnIam += OnIam;

            // 监听端口
            client.Start();

            if (Transport is BacnetIpUdpProtocolTransport udp)
                WriteLog("本地：{0}", udp.LocalEndPoint);

            //// 广播“你是谁”
            //client.WhoIs();

            _client = client;

            _timer = new TimerX(DoScan, null, 0, 60_000) { Async = true };

            Active = true;
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);
            throw;
        }
    }

    /// <summary>关闭连接</summary>
    public void Close()
    {
        if (!Active) return;

        Transport.TryDispose();
        Transport = null;

        Active = false;
    }

    /// <summary>获取指定地址的节点</summary>
    /// <param name="address"></param>
    /// <returns></returns>
    public BacNode GetNode(String address) => _nodes.FirstOrDefault(e => e.Address + "" == address);

    /// <summary>获取指定地址的节点</summary>
    /// <param name="deviceId"></param>
    /// <returns></returns>
    public BacNode GetNode(Int32 deviceId) => _nodes.FirstOrDefault(e => e.DeviceId == deviceId);
    #endregion

    #region 扫描管理
    private TimerX _timer;

    private void DoScan(Object state)
    {
        using var span = Tracer?.NewSpan("bac:Scan", new { Port, DeviceId, Address });

        // 广播“你是谁”
        _client.WhoIs();
    }

    TaskCompletionSource<BacNode> _tcs;
    /// <summary>扫描节点</summary>
    /// <returns></returns>
    public void Scan()
    {
        _tcs = new TaskCompletionSource<BacNode>();
        try
        {
            // 广播“你是谁”
            _client.WhoIs();

            _tcs.Task.Wait(3_000);
        }
        finally
        {
            _tcs.TryDispose();
            _tcs = null;
        }
    }

    private void OnIam(BacnetClient sender, BacnetAddress addr, UInt32 deviceId, UInt32 maxAPDU, BacnetSegmentations segmentation, UInt16 vendorId)
    {
        using var span = Tracer?.NewSpan("bac:OnIam", new { addr, deviceId, vendorId });

        // 只要目标DeviceId
        if (DeviceId > 0 && deviceId != DeviceId) return;

        lock (_nodes)
        {
            foreach (var bn in _nodes)
            {
                XTrace.WriteLine("OnIam [{0}]: {1}", bn.Address, bn.DeviceId);
                if (bn.GetAdd(deviceId) != null) return;
            }

            // 新增节点
            var node = new BacNode(addr, deviceId);
            _nodes.Add(node);

            // 读取属性列表
            Task.Run(() => GetProperties(node, true));
            //_timer.SetNext(-1);
            _tcs?.TrySetResult(node);
        }

    }

    /// <summary>获取节点属性列表</summary>
    public void GetProperties()
    {
        foreach (var node in _nodes)
        {
            if (node.Address != null)
            {
                GetProperties(node, false);
            }
        }
    }

    /// <summary>获取节点属性列表</summary>
    /// <param name="node">节点</param>
    /// <param name="includeValue">是否包含数值</param>
    public void GetProperties(BacNode node, Boolean includeValue)
    {
        if (node.Address == null) return;

        // 读取属性对象列表，点位列表
        var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, node.DeviceId);
        if (_client.ReadPropertyRequest(node.Address, oid, BacnetPropertyIds.PROP_OBJECT_LIST, out var list))
        {
            node.Ids = list;

            if (includeValue) GetValues(node);
        }
    }

    /// <summary>获取节点的属性数值</summary>
    /// <param name="node"></param>
    public void GetValues(BacNode node)
    {
        if (node.Address == null || node.Ids == null) return;

        // 构建属性引用列表
        var prs = new List<BacnetPropertyReference>();
        for (var i = 0; i < node.Ids.Count; i++)
        {
            var property = new BacnetPropertyReference((UInt32)BacnetPropertyIds.PROP_PRESENT_VALUE, (UInt32)i);
            prs.Add(property);
        }

        // 分批读取属性数值
        var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, node.DeviceId);
        var list2 = new List<BacProperty>();
        for (var i = 0; i < node.Ids.Count;)
        {
            var batch = prs.Skip(i).Take(16).ToList();
            if (batch.Count == 0) break;

            if (_client.ReadPropertyMultipleRequest(node.Address, oid, batch, out var results))
            {
                var ps = BacProperty.Create(results);
                list2.AddRange(ps);
            }

            i += batch.Count;
        }
        node.Properties = list2;
    }
    #endregion

    #region 读写方法
    /// <summary>读取属性值</summary>
    /// <param name="addr"></param>
    /// <param name="oid"></param>
    /// <returns></returns>
    public Object ReadProperty(BacnetAddress addr, BacnetObjectId oid)
    {
        if (_client.ReadPropertyRequest(addr, oid, BacnetPropertyIds.PROP_PRESENT_VALUE, out var rs))
        {
            return rs[0].Value;
        }

        return null;
    }

    /// <summary>读取属性值</summary>
    /// <param name="addr"></param>
    /// <param name="id"></param>
    /// <returns></returns>
    public Object ReadProperty(BacnetAddress addr, String id)
    {
        if (!ObjectPair.TryParse(id, out var oid)) return null;

        if (_client.ReadPropertyRequest(addr, oid, BacnetPropertyIds.PROP_PRESENT_VALUE, out var rs))
        {
            return rs[0].Value;
        }

        return null;
    }

    /// <summary>批量读取多个对象的属性值</summary>
    /// <param name="addr"></param>
    /// <param name="oids"></param>
    /// <returns></returns>
    public IDictionary<BacnetObjectId, Object> ReadProperties(BacnetAddress addr, IList<BacnetObjectId> oids)
    {
        // 构建属性引用列表
        var prs = new List<BacnetReadAccessSpecification>();
        for (var i = 0; i < oids.Count; i++)
        {
            var property = new BacnetPropertyReference((UInt32)BacnetPropertyIds.PROP_PRESENT_VALUE, 0);
            prs.Add(new BacnetReadAccessSpecification(oids[i], new[] { property }));
        }

        // 批量读取属性数值
        var results = new Dictionary<BacnetObjectId, Object>();
        if (_client.ReadPropertyMultipleRequest(addr, prs.ToArray(), out var values))
        {
            foreach (var item in values)
            {
                foreach (var elm in item.values)
                {
                    if (elm.value == null || elm.value.Count == 0) continue;

                    if ((BacnetPropertyIds)elm.property.propertyIdentifier == BacnetPropertyIds.PROP_PRESENT_VALUE)
                    {
                        var bv = elm.value[0];
                        if (bv.Tag == BacnetApplicationTags.BACNET_APPLICATION_TAG_ERROR)
                            throw new XException(bv.Value + "");

                        results[item.objectIdentifier] = bv.Value;
                    }
                }
            }
        }

        return results;
    }

    /// <summary>写入属性值</summary>
    /// <param name="addr"></param>
    /// <param name="oid"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    public Boolean WriteProperty(BacnetAddress addr, BacnetObjectId oid, Object value)
    {
        var bv = new BacnetValue(value);
        return _client.WritePropertyRequest(addr, oid, BacnetPropertyIds.PROP_PRESENT_VALUE, new[] { bv });
    }

    /// <summary>写入属性值</summary>
    /// <param name="addr"></param>
    /// <param name="id"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    public Boolean WriteProperty(BacnetAddress addr, String id, Object value)
    {
        if (!ObjectPair.TryParse(id, out var oid)) return false;

        var bv = new BacnetValue(value);
        return _client.WritePropertyRequest(addr, oid, BacnetPropertyIds.PROP_PRESENT_VALUE, new[] { bv });
    }

    /// <summary>批量写入多个对象的属性值</summary>
    /// <param name="addr"></param>
    /// <param name="data"></param>
    /// <returns></returns>
    public Boolean WriteProperties(BacnetAddress addr, IDictionary<BacnetObjectId, Object> data)
    {
        // 构建属性引用列表
        var prs = new List<BacnetReadAccessResult>();
        foreach (var item in data)
        {
            var bv = new BacnetValue(item.Value);
            var property = new BacnetPropertyReference((UInt32)BacnetPropertyIds.PROP_PRESENT_VALUE, 0);
            var bpv = new BacnetPropertyValue { property = property, value = new[] { bv } };
            prs.Add(new BacnetReadAccessResult(item.Key, new[] { bpv }));
        }

        return _client.WritePropertyMultipleRequest(addr, prs.ToArray());
    }

    /// <summary>读取</summary>
    /// <param name="node"></param>
    /// <param name="points"></param>
    /// <returns></returns>
    public Dictionary<String, Object> Read(BacNode node, params IPoint[] points)
    {
        var addr = node.Address;
        if (addr == null || node.Properties == null) return null;

        using var span = Tracer?.NewSpan("bac:Read", new { node.Address, points });

        // 点位转为属性。点位地址0_0，前面是编号，后面是类型
        var ps = new List<ObjectPair>();
        foreach (var item in points)
        {
            if (ObjectPair.TryParse(item.Address, out var oid))
            {
                ps.Add(new ObjectPair { Point = item, ObjectId = oid });
            }
        }
        if (ps.Count == 0) return null;

        var dic = new Dictionary<String, Object>();

        if (ps.Count == 1)
        {
            // 单个点位，直接读取
            if (_client.ReadPropertyRequest(addr, ps[0].ObjectId, BacnetPropertyIds.PROP_PRESENT_VALUE, out var rs))
            {
                dic[ps[0].Point.Name + ""] = rs[0].Value;
            }
        }
        else
        {
            // 构建属性引用列表
            var prs = new List<BacnetPropertyReference>();
            for (var i = 0; i < ps.Count; i++)
            {
                var property = new BacnetPropertyReference((UInt32)BacnetPropertyIds.PROP_PRESENT_VALUE, ps[i].ObjectId.Instance);
                prs.Add(property);
            }

            // 分批读取属性数值
            var objId = new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, node.DeviceId);
            var results = new List<BacnetReadAccessResult>();
            for (var i = 0; i < prs.Count; i++)
            {
                var batch = prs.Skip(i).Take(16).ToList();
                if (_client.ReadPropertyMultipleRequest(addr, objId, batch, out var values))
                {
                    results.AddRange(values);
                }

                i += batch.Count;
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
        using var span = Tracer?.NewSpan("bac:Write", new { addr, point, value });

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
    #endregion

    #region 日志
    /// <summary>性能追踪</summary>
    public ITracer Tracer { get; set; }

    /// <summary>日志</summary>
    public ILog Log { get; set; }

    /// <summary>写日志</summary>
    /// <param name="format"></param>
    /// <param name="args"></param>
    public void WriteLog(String format, params Object[] args) => Log?.Info(format, args);
    #endregion
}