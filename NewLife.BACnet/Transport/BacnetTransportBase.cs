using NewLife.Log;

namespace System.IO.BACnet;

/// <summary>BACnet 传输层抽象基类。提供统一的收发接口和日志/追踪支持。</summary>
/// <remarks>
/// 所有传输层实现（UDP、SC、串口等）继承自此基类。
/// 子类需实现 <see cref="Start"/>、<see cref="Send"/>、<see cref="GetBroadcastAddress"/> 和 <see cref="Dispose"/>。
/// 
/// 可观测性：
/// - <see cref="Tracer"/> 用于 APM 追踪关键操作耗时（如连接、发送）
/// - <see cref="Log"/> 用于日志记录
/// </remarks>
public abstract class BacnetTransportBase : IBacnetTransport
{
    /// <summary>性能追踪。可用于记录收发字节数和关键操作耗时。</summary>
    public ITracer Tracer { get; set; }

    /// <summary>日志</summary>
    public ILog Log { get; set; } = XTrace.Log;

    /// <summary>传输层头长度（字节），每类传输层为固定值</summary>
    public Int32 HeaderLength { get; protected set; }

    /// <summary>最大缓冲区长度（字节）</summary>
    public Int32 MaxBufferLength { get; protected set; }

    /// <summary>传输层地址类型</summary>
    public BacnetAddressTypes Type { get; protected set; }

    /// <summary>最大 APDU 长度</summary>
    public BacnetMaxAdpu MaxAdpuLength { get; protected set; }

    /// <summary>最大信息帧数（MS/TP 协议使用）</summary>
    public Byte MaxInfoFrames { get; set; } = 0xFF;

    /// <summary>初始化传输层基类</summary>
    protected BacnetTransportBase()
    {
    }

    /// <summary>启动传输层（建立连接、开始监听等）</summary>
    public abstract void Start();

    /// <summary>获取广播地址</summary>
    /// <returns>广播地址</returns>
    public abstract BacnetAddress GetBroadcastAddress();

    /// <summary>等待所有发送完成</summary>
    /// <param name="timeout">超时时间（毫秒）</param>
    /// <returns>是否全部完成</returns>
    public virtual Boolean WaitForAllTransmits(Int32 timeout)
    {
        return true; // not used 
    }

    /// <summary>发送数据</summary>
    /// <param name="buffer">数据缓冲区</param>
    /// <param name="offset">数据起始偏移</param>
    /// <param name="dataLength">数据长度</param>
    /// <param name="address">目标地址</param>
    /// <param name="waitForTransmission">是否等待发送完成</param>
    /// <param name="timeout">超时时间（毫秒）</param>
    /// <returns>实际发送的字节数</returns>
    public abstract Int32 Send(Byte[] buffer, Int32 offset, Int32 dataLength, BacnetAddress address, Boolean waitForTransmission, Int32 timeout);

    /// <summary>收到消息时触发</summary>
    public event MessageRecievedHandler MessageRecieved;

    /// <summary>触发消息接收事件</summary>
    /// <param name="buffer">数据缓冲区</param>
    /// <param name="offset">数据起始偏移</param>
    /// <param name="msgLength">消息长度</param>
    /// <param name="remoteAddress">远程地址</param>
    protected void InvokeMessageRecieved(Byte[] buffer, Int32 offset, Int32 msgLength, BacnetAddress remoteAddress)
    {
        MessageRecieved?.Invoke(this, buffer, offset, msgLength, remoteAddress);
    }

    /// <summary>释放资源</summary>
    public abstract void Dispose();
}
