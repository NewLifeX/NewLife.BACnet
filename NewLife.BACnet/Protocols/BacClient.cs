using System.IO.BACnet;
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

    /// <summary>设备编号</summary>
    public Int32 DeviceId { get; set; }

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
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);
            throw;
        }
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

    #region 设备扫描管理
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

            var node = new BacNode(addr, deviceId);
            _nodes.Add(node);

            //_timer.SetNext(-1);
            _tcs.TrySetResult(node);
        }

    }

    /// <summary>获取节点属性列表</summary>
    public void GetProperties()
    {
        foreach (var node in _nodes)
        {
            if (node.Address != null)
            {
                GetProperties(node);
            }
        }
    }

    /// <summary>获取节点属性列表</summary>
    /// <param name="node"></param>
    public void GetProperties(BacNode node)
    {
        if (node.Address == null) return;

        var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, node.DeviceId);
        if (_client.ReadPropertyRequest(node.Address, oid, BacnetPropertyIds.PROP_OBJECT_LIST, out var list))
        {
            node.Properties.Clear();
            var prs = new List<BacnetPropertyReference>();
            for (var i = 0; i < list.Count; i++)
            {
                var property = new BacnetPropertyReference((UInt32)BacnetPropertyIds.PROP_OBJECT_LIST, (UInt32)i);
                prs.Add(property);
            }
            for (var i = 0; i < list.Count;)
            {
                var batch = prs.Skip(i).Take(16).ToList();
                if (batch.Count == 0) break;

                if (_client.ReadPropertyMultipleRequest(node.Address, oid, batch, out var results))
                {
                    var ps = BacProperty.Create(results);
                    node.Properties.AddRange(ps);
                }

                i += batch.Count;
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

        using var span = Tracer?.NewSpan("bac:Read", new { node.Address, points });

        var dic = new Dictionary<String, Object>();

        var rList = new List<BacnetPropertyReference> {
            new BacnetPropertyReference((UInt32)BacnetPropertyIds.PROP_DESCRIPTION, UInt32.MaxValue),
            new BacnetPropertyReference((UInt32)BacnetPropertyIds.PROP_REQUIRED, UInt32.MaxValue),
            new BacnetPropertyReference((UInt32)BacnetPropertyIds.PROP_OBJECT_NAME, UInt32.MaxValue),
            new BacnetPropertyReference((UInt32)BacnetPropertyIds.PROP_PRESENT_VALUE, UInt32.MaxValue)
        };

        //var pt = node.Properties[0];
        var obj = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, 0);
        if (points.Length == 1)
        {
            if (_client.ReadPropertyRequest(addr, obj, BacnetPropertyIds.PROP_PRESENT_VALUE, out var rs))
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
                if (_client.ReadPropertyMultipleRequest(addr, obj, rList, out var values, 0))
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