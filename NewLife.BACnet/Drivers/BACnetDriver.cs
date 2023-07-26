using System.ComponentModel;
using NewLife.BACnet.Protocols;
using NewLife.IoT;
using NewLife.IoT.Drivers;
using NewLife.IoT.ThingModels;
using NewLife.Reflection;

namespace NewLife.BACnet.Drivers;

/// <summary>
/// BACnet协议封装
/// </summary>
[Driver("BACnet")]
[Description("楼宇自动化")]
public class BACnetDriver : DriverBase
{
    #region 属性
    private BacClient _client;
    /// <summary>客户端</summary>
    public BacClient Client => _client;

    private Int32 _nodes;
    #endregion

    #region 构造
    #endregion

    #region 方法
    /// <summary>
    /// 创建默认参数
    /// </summary>
    /// <returns></returns>
    protected override IDriverParameter OnCreateParameter() => new BACnetParameter();

    /// <summary>
    /// 打开通道。一个BACnet设备可能分为多个通道读取，需要共用Tcp连接，以不同节点区分
    /// </summary>
    /// <param name="device">通道</param>
    /// <param name="parameter">参数</param>
    /// <returns></returns>
    public override INode Open(IDevice device, IDriverParameter parameter)
    {
        if (parameter is not BACnetParameter p) return null;

        // 实例化一次Tcp连接
        if (_client == null)
        {
            lock (this)
            {
                if (_client == null)
                {
                    var client = new BacClient
                    {
                        //Address = p.Address,
                        Port = p.Port,

                        // 这里不指定设备，自动搜索网络中所有设备，以便支持多个设备
                        //DeviceId = p.DeviceId

                        Log = Log,
                        Tracer = Tracer,
                    };

                    // 外部已指定通道时，打开连接
                    if (device != null) client.Open();

                    _client = client;
                }
            }
        }

        Interlocked.Increment(ref _nodes);

        return new BACnetNode
        {
            Driver = this,
            Device = device,
            Parameter = p,
            DeviceId = p.DeviceId,
            Client = _client,
        };
    }

    /// <summary>
    /// 关闭设备驱动
    /// </summary>
    /// <param name="node"></param>
    public override void Close(INode node)
    {
        if (Interlocked.Decrement(ref _nodes) <= 0)
        {
            _client.TryDispose();
            _client = null;
        }
    }

    /// <summary>
    /// 读取数据
    /// </summary>
    /// <param name="node">节点对象，可存储站号等信息，仅驱动自己识别</param>
    /// <param name="points">点位集合</param>
    /// <returns></returns>
    public override IDictionary<String, Object> Read(INode node, IPoint[] points)
    {
        if (points == null || points.Length == 0) return null;

        var p = (node as BACnetNode).Parameter as BACnetParameter;
        using var span = Tracer?.NewSpan("bac:Read", new { p.DeviceId, points });

        // 点位转为属性。点位地址0_0，前面是编号，后面是类型
        var ps = new List<ObjectPair>();
        foreach (var item in points)
        {
            if (ObjectPair.TryParse(item.Address, out var oid))
            {
                ps.Add(new ObjectPair { Point = item, ObjectId = oid });
            }
            else if (ObjectPair.TryParse(item.Name, out var oid2))
            {
                ps.Add(new ObjectPair { Point = item, ObjectId = oid2 });
            }
        }
        if (ps.Count == 0) return null;

        var bacNode = _client.GetNode(p.DeviceId);
        //bacNode ??= _client.GetNode(p.Address);
        if (bacNode == null) return null;

        // 加锁，避免冲突
        lock (_client)
        {
            var dic = new Dictionary<String, Object>();

            //todo 批量读取还有问题，每次读取到1
            var data = _client.ReadProperties(bacNode.Address, ps.Select(e => e.ObjectId).ToArray());
            if (data == null) return null;

            foreach (var item in ps)
            {
                if (data.TryGetValue(item.ObjectId, out var v))
                    dic[item.Point.Name] = v;
            }

            //// 逐个读取
            //foreach (var item in ps)
            //{
            //    var rs = _client.ReadProperty(bacNode.Address, item.ObjectId);
            //    if (rs != null) dic[item.Point.Name] = rs;
            //}

            return dic;
        }
    }

    /// <summary>
    /// 写入数据
    /// </summary>
    /// <param name="node">节点对象，可存储站号等信息，仅驱动自己识别</param>
    /// <param name="point">点位</param>
    /// <param name="value">数值</param>
    public override Object Write(INode node, IPoint point, Object value)
    {
        var p = (node as BACnetNode).Parameter as BACnetParameter;
        var bnode = _client.GetNode(p.DeviceId);

        // 优先使用地址，其次名称
        var id = !point.Address.IsNullOrEmpty() ? point.Address : point.Name;

        // 根据属性转换数据类型
        var property = bnode.Properties.FirstOrDefault(e => e.Name == id);
        if (property != null)
        {
            value = value.ChangeType(property.Type);
        }

        return _client.WriteProperty(bnode.Address, id, value);
    }
    #endregion
}