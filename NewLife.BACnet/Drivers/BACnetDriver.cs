using NewLife.BACnet.Protocols;
using NewLife.IoT;
using NewLife.IoT.Drivers;
using NewLife.IoT.ThingModels;
using NewLife.Log;
using NewLife.Serialization;

namespace NewLife.BACnet.Drivers;

/// <summary>
/// BACnet协议封装
/// </summary>
[Driver("BACnet")]
public class BACnetDriver : DisposeBase, IDriver
{
    private BACnetClient _client;
    private Int32 _nodes;

    #region 构造
    #endregion

    #region 方法
    /// <summary>
    /// 创建默认参数
    /// </summary>
    /// <returns></returns>
    public IDriverParameter GetDefaultParameter() => new BACnetParameter();

    /// <summary>
    /// 获取默认节点
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public IPoint[] GetDefaultPoints() => null;

    /// <summary>
    /// 打开通道。一个BACnet设备可能分为多个通道读取，需要共用Tcp连接，以不同节点区分
    /// </summary>
    /// <param name="device">通道</param>
    /// <param name="parameters">参数</param>
    /// <returns></returns>
    public INode Open(IDevice device, IDictionary<String, Object> parameters)
    {
        var p = JsonHelper.Convert<BACnetParameter>(parameters);
        if (p == null) return null;

        // 实例化一次Tcp连接
        if (_client == null)
        {
            lock (this)
            {
                if (_client == null)
                {
                    var client = new BACnetClient();

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
        };
    }

    /// <summary>
    /// 关闭设备驱动
    /// </summary>
    /// <param name="node"></param>
    public void Close(INode node)
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
    public virtual IDictionary<String, Object> Read(INode node, IPoint[] points)
    {
        if (points == null || points.Length == 0) return null;

        // 加锁，避免冲突
        lock (_client)
        {
            var p = (node as BACnetNode).Parameter as BACnetParameter;
            var device = _client.GetNode(p.Address);
            var dic = _client.Read(device, points);

            return dic;
        }
    }

    /// <summary>
    /// 写入数据
    /// </summary>
    /// <param name="node">节点对象，可存储站号等信息，仅驱动自己识别</param>
    /// <param name="point">点位</param>
    /// <param name="value">数值</param>
    public virtual Object Write(INode node, IPoint point, Object value)
    {
        var p = (node as BACnetNode).Parameter as BACnetParameter;
        return _client.Write(p.Address, point, value);
    }

    /// <summary>
    /// 控制设备，特殊功能使用
    /// </summary>
    /// <param name="node"></param>
    /// <param name="parameters"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void Control(INode node, IDictionary<String, Object> parameters) => throw new NotImplementedException();
    #endregion

    #region 日志
    /// <summary>日志</summary>
    public ILog Log { get; set; }

    /// <summary>写日志</summary>
    /// <param name="format"></param>
    /// <param name="args"></param>
    public void WriteLog(String format, params Object[] args) => Log?.Info(format, args);

    /// <summary>性能追踪器</summary>
    public ITracer Tracer { get; set; }
    #endregion
}