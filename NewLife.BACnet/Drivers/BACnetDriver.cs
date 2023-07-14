using System.Security.Cryptography;
using NewLife.BACnet.Protocols;
using NewLife.IoT;
using NewLife.IoT.Drivers;
using NewLife.IoT.ThingModels;

namespace NewLife.BACnet.Drivers;

/// <summary>
/// BACnet协议封装
/// </summary>
[Driver("BACnet")]
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
    public override IDriverParameter GetDefaultParameter() => new BACnetParameter();

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
                        Address = p.Address,
                        Port = p.Port,
                        //DeviceId = p.DeviceId
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
        using var span = Tracer?.NewSpan("bac:Read", new { p.Address, points });

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

        // 加锁，避免冲突
        lock (_client)
        {
            var bacNode = _client.GetNode(p.DeviceId);
            bacNode ??= _client.GetNode(p.Address);

            var dic = new Dictionary<String, Object>();

            //todo 批量读取还有问题，每次读取到1
            //var data = _client.ReadProperties(bacNode.Address, ps.Select(e => e.ObjectId).ToArray());
            //if (data == null) return null;

            //foreach (var item in ps)
            //{
            //    if (data.TryGetValue(item.ObjectId, out var v))
            //        dic[item.Point.Name] = v;
            //}

            foreach (var item in ps)
            {
                var rs = _client.ReadProperty(bacNode.Address, item.ObjectId);
                if (rs != null) dic[item.Point.Name] = rs;
            }

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
        return _client.Write(p.Address, point, value);
    }
    #endregion
}