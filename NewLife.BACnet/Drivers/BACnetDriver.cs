using System.ComponentModel;
using NewLife.BACnet.Protocols;
using NewLife.IoT;
using NewLife.IoT.Drivers;
using NewLife.IoT.ThingModels;
using NewLife.Reflection;

namespace NewLife.BACnet.Drivers;

/// <summary>BACnet协议驱动</summary>
/// <remarks>楼宇自动化与控制网络（Building Automation and Control Networks）协议驱动封装，基于 UDP 广播/单播与 BACnet 设备通信。</remarks>
[Driver("BACnet")]
[DisplayName("楼宇自动化与控制网络")]
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
    /// <summary>创建默认参数</summary>
    /// <returns></returns>
    protected override IDriverParameter OnCreateParameter() => new BACnetParameter();

    /// <summary>打开通道，返回节点对象</summary>
    /// <param name="device">通道</param>
    /// <param name="parameter">参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns></returns>
    /// <remarks>一个 BACnet 设备可能分为多个通道读取，多个节点共用同一个 BacClient 连接，通过引用计数管理连接生命周期。</remarks>
    public override Task<INode> OpenAsync(IDevice device, IDriverParameter? parameter, CancellationToken cancellationToken = default)
    {
        if (parameter is not BACnetParameter p) return TaskEx.FromResult<INode>(null);

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

                        TargetAddress = p.TargetAddress,

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

        INode node = new BACnetNode
        {
            Driver = this,
            Device = device,
            Parameter = p,
            DeviceId = p.DeviceId,
            Client = _client,
        };
        return TaskEx.FromResult(node);
    }

    /// <summary>关闭节点，释放底层连接</summary>
    /// <param name="node">节点对象</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <remarks>内部使用引用计数，最后一个节点关闭时才真正释放 BacClient。</remarks>
    public override Task CloseAsync(INode node, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Decrement(ref _nodes) <= 0)
        {
            _client.TryDispose();
            _client = null;
        }
        return TaskEx.CompletedTask;
    }

    /// <summary>批量读取点位数据</summary>
    /// <param name="node">节点对象，可存储站号等信息，仅驱动自己识别</param>
    /// <param name="points">点位集合</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns></returns>
    /// <remarks>将点位地址解析为 BACnet ObjectId，通过 ReadPropertyMultiple 批量读取，返回含点位与值的 ReadResult。</remarks>
    public override Task<ReadResult> ReadAsync(INode node, IPoint[] points, CancellationToken cancellationToken = default)
    {
        if (points == null || points.Length == 0) return TaskEx.FromResult(new ReadResult());

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
        if (ps.Count == 0) return TaskEx.FromResult(new ReadResult());

        var bacNode = _client.GetNode(p.DeviceId);
        //bacNode ??= _client.GetNode(p.Address);
        if (bacNode == null) return TaskEx.FromResult(new ReadResult());

        // 加锁，避免冲突
        lock (_client)
        {
            //todo 批量读取还有问题，每次读取到1
            var data = _client.ReadProperties(bacNode.Address, ps.Select(e => e.ObjectId).ToArray());
            if (data == null) return TaskEx.FromResult(new ReadResult());

            var resultPoints = new List<IPoint>();
            var resultValues = new List<Object?>();
            foreach (var item in ps)
            {
                if (data.TryGetValue(item.ObjectId, out var v))
                {
                    resultPoints.Add(item.Point);
                    resultValues.Add(v);
                }
            }

            //// 逐个读取
            //foreach (var item in ps)
            //{
            //    var rs = _client.ReadProperty(bacNode.Address, item.ObjectId);
            //    if (rs != null) dic[item.Point.Name] = rs;
            //}

            var result = new ReadResult
            {
                IsSuccess = true,
                Points = resultPoints.ToArray(),
                Values = resultValues.ToArray(),
            };
            return TaskEx.FromResult(result);
        }
    }

    /// <summary>写入数据</summary>
    /// <param name="node">节点对象，可存储站号等信息，仅驱动自己识别</param>
    /// <param name="requests">写入请求数组</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <remarks>
    /// 单点写入调用 WriteProperty；多点写入构建字典后调用 WriteProperties（BACnet WritePropertyMultiple 服务），
    /// 减少网络往返，提高批量写入吞吐量。
    /// </remarks>
    public override Task<WriteResult> WriteAsync(INode node, WriteRequest[] requests, CancellationToken cancellationToken = default)
    {
        var p = (node as BACnetNode).Parameter as BACnetParameter;
        var bnode = _client.GetNode(p.DeviceId);

        // 将请求转换为 <ObjectId, value> 映射，同时完成类型适配
        var data = new Dictionary<System.IO.BACnet.BacnetObjectId, Object>(requests.Length);
        foreach (var req in requests)
        {
            var point = req.Point;
            var value = req.Value;

            // 优先使用地址，其次名称
            var id = !point.Address.IsNullOrEmpty() ? point.Address : point.Name;
            if (!ObjectPair.TryParse(id, out var oid)) continue;

            // 根据已知属性元数据转换数据类型
            var property = bnode.Properties?.FirstOrDefault(e => e.Name == id);
            if (property != null) value = value.ChangeType(property.Type);

            data[oid] = value;
        }

        if (data.Count == 0) return TaskEx.FromResult(WriteResult.SuccessBatch(0));

        Boolean ok;
        if (data.Count == 1)
        {
            // 单点：WriteProperty（SERVICE_CONFIRMED_WRITE_PROPERTY）
            var kv = data.First();
            ok = _client.WriteProperty(bnode.Address, kv.Key, kv.Value);
        }
        else
        {
            // 多点：WriteProperties（SERVICE_CONFIRMED_WRITE_PROP_MULTIPLE）
            ok = _client.WriteProperties(bnode.Address, data);
        }

        return TaskEx.FromResult(ok ? WriteResult.SuccessBatch(data.Count) : WriteResult.SuccessBatch(0));
    }
    #endregion
}