using System.IO.BACnet.Serialize;
using System.Net;
#if !NETFRAMEWORK
using System.Net.WebSockets;
#endif
using NewLife.Log;

namespace System.IO.BACnet;

/// <summary>BACnet/SC WebSocket 传输层。
/// 基于 WebSocket + TLS 的 BACnet Secure Connect 传输实现。</summary>
/// <remarks>
/// BACnet/SC (Secure Connect) 使用 WebSocket 作为底层传输，支持：
/// - Hub/Node 星型拓扑（节点连接到 Hub，经 Hub 转发）
/// - 直连模式（Peer-to-Peer，节点间直接通信）
/// - TLS 1.2+ 加密（wss://）
/// 
/// 使用方式：
/// <code>
/// // Node 模式（连接 Hub）
/// var transport = new BacnetScTransport("wss://hub.example.com/bacnet", isHub: false);
/// transport.Start();
/// 
/// // Hub 模式（监听节点连接，需要 ASP.NET Core 或 HttpListener）
/// var transport = new BacnetScTransport("https://+:47810/", isHub: true);
/// transport.Start();
/// </code>
/// 
/// 注意：.NET Framework 4.5 不支持 WebSocket 客户端，使用时会抛出 PlatformNotSupportedException。
/// </remarks>
public class BacnetScTransport : BacnetTransportBase
{
    /// <summary>默认 SC 端口（IANA 未正式分配，社区常用 47810）</summary>
    public const Int32 DEFAULT_SC_PORT = 47810;

    /// <summary>最大帧长度（WebSocket 无 MTU 限制，使用 64KB）</summary>
    public const Int32 DEFAULT_MAX_PAYLOAD = 65536;

    /// <summary>SC BVLC 头长度</summary>
    public const Int32 SC_HEADER_LENGTH = BVLCSC.BVLC_HEADER_LENGTH;

    /// <summary>连接 URI</summary>
    public Uri Uri { get; }

    /// <summary>是否为 Hub 模式（监听模式）</summary>
    public Boolean IsHub { get; }

    /// <summary>是否已连接</summary>
    public Boolean IsConnected => _state == ScTransportState.Connected;

    /// <summary>本地端点地址</summary>
    public IPEndPoint LocalEndPoint { get; private set; }

    /// <summary>远程端点地址</summary>
    public IPEndPoint RemoteEndPoint { get; private set; }

    /// <summary>连接状态</summary>
    public ScTransportState State => _state;
    private volatile ScTransportState _state = ScTransportState.Disconnected;

    /// <summary>断开时触发</summary>
    public event Action<BacnetScTransport, String> Disconnected;

    // WebSocket 实例
#if !NETFRAMEWORK
    private ClientWebSocket _webSocket;
    private CancellationTokenSource _receiveCts;
#endif
    private readonly Object _sendLock = new();
    private Boolean _disposing;

    /// <summary>BACnet/SC 传输状态</summary>
    public enum ScTransportState
    {
        /// <summary>未连接</summary>
        Disconnected,

        /// <summary>连接中</summary>
        Connecting,

        /// <summary>已连接</summary>
        Connected,

        /// <summary>已断开</summary>
        Faulted
    }

    /// <summary>初始化 BACnet/SC 传输层</summary>
    /// <param name="uri">WebSocket 服务器 URI（wss:// 或 ws://）</param>
    /// <param name="isHub">是否为 Hub 模式</param>
    /// <param name="maxPayload">最大负载长度</param>
    public BacnetScTransport(String uri, Boolean isHub = false, Int32 maxPayload = DEFAULT_MAX_PAYLOAD)
    {
        Uri = new Uri(uri);
        IsHub = isHub;
        MaxBufferLength = maxPayload;
        HeaderLength = SC_HEADER_LENGTH;
        MaxAdpuLength = BVLCSC.BVLC_MAX_APDU;
        Type = IsHub ? BacnetAddressTypes.IP : BacnetAddressTypes.IP;

        if (IsHub)
        {
            Log.Warn("BACnet/SC Hub 模式需要外部 WebSocket 服务器（ASP.NET Core / HttpListener），传输层仅处理客户端连接");
        }
    }

    /// <summary>启动传输层（连接 WebSocket）</summary>
    public override void Start()
    {
        using var span = Tracer?.NewSpan("bac:ScStart", new { Uri, IsHub });
#if NETFRAMEWORK
        throw new PlatformNotSupportedException("BACnet/SC 在 .NET Framework 4.5 上不受支持，请使用 .NET Core / .NET 5+");
#else
        _disposing = false;
        _state = ScTransportState.Connecting;
        _receiveCts = new CancellationTokenSource();

        try
        {
            _webSocket = new ClientWebSocket();

            // TLS 1.2+
            if (String.Equals(Uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }

            // 异步连接
            var connectTask = _webSocket.ConnectAsync(Uri, _receiveCts.Token);
            connectTask.GetAwaiter().GetResult();

            if (_webSocket.State == WebSocketState.Open)
            {
                _state = ScTransportState.Connected;
                Log.Info($"BACnet/SC 已连接至 {Uri}");

                // 获取本地和远程端点（通过实际连接信息）
                try
                {
                    RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                }
                catch
                {
                    // ignore
                }

                // 启动接收循环
                Task.Factory.StartNew(ReceiveLoop, TaskCreationOptions.LongRunning);
            }
            else
            {
                _state = ScTransportState.Faulted;
                Log.Error($"BACnet/SC 连接失败，状态：{_webSocket.State}");
            }
        }
        catch (Exception ex)
        {
            _state = ScTransportState.Faulted;
            Log.Error($"BACnet/SC 连接异常：{ex.Message}");
            Cleanup();
            throw;
        }
#endif
    }

#if !NETFRAMEWORK
    /// <summary>接收循环</summary>
    private async void ReceiveLoop()
    {
        var buffer = new Byte[MaxBufferLength];
        var cts = _receiveCts;

        try
        {
            while (!_disposing && cts != null && !cts.IsCancellationRequested)
            {
                var segment = new ArraySegment<Byte>(buffer);
                WebSocketReceiveResult result;

                try
                {
                    result = await _webSocket.ReceiveAsync(segment, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (WebSocketException ex)
                {
                    Log.Error($"WebSocket 接收异常：{ex.Message}");
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Log.Info($"WebSocket 远端关闭：{result.CloseStatus}");
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Remote close", CancellationToken.None);
                    break;
                }

                if (result.MessageType != WebSocketMessageType.Binary)
                    continue;

                var receivedLength = result.Count;

                // 处理可能的分片消息
                if (result.EndOfMessage)
                {
                    ProcessReceivedData(buffer, receivedLength);
                }
                else
                {
                    // 多分片消息，收集完整后再处理
                    using var ms = new MemoryStream(MaxBufferLength);
                    ms.Write(buffer, 0, receivedLength);

                    while (!result.EndOfMessage && !_disposing)
                    {
                        result = await _webSocket.ReceiveAsync(segment, cts.Token);
                        ms.Write(buffer, 0, result.Count);
                    }

                    ProcessReceivedData(ms.ToArray(), (Int32)ms.Length);
                }
            }
        }
        catch (Exception ex)
        {
            if (!_disposing)
                Log.Error($"BACnet/SC 接收循环异常：{ex.Message}");
        }
        finally
        {
            _state = ScTransportState.Disconnected;
            OnDisconnected("接收循环结束");
        }
    }

    private void ProcessReceivedData(Byte[] buffer, Int32 length)
    {
        try
        {
            if (length < BVLCSC.BVLC_HEADER_LENGTH)
            {
                Log.Warn($"BACnet/SC 帧过短：{length} 字节");
                return;
            }

            var headerLen = BVLCSC.Decode(buffer, 0, out var function, out var msgLength);

            if (headerLen == -1)
            {
                Log.Warn($"无效的 BACnet/SC BVLC 头");
                return;
            }

            // 处理控制帧
            switch (function)
            {
                case BacnetBvlcScFunctions.BVLC_SC_HUB_CONNECT:
                    Log.Info("收到 Hub 连接确认");
                    return;

                case BacnetBvlcScFunctions.BVLC_SC_HUB_DISCONNECT:
                    Log.Info("收到 Hub 断开请求");
                    return;

                case BacnetBvlcScFunctions.BVLC_SC_ANNOUNCE_HUB_FUNCTION:
                    Log.Info("收到 Hub 宣告");
                    return;

                case BacnetBvlcScFunctions.BVLC_SC_HUB_FUNCTION:
                case BacnetBvlcScFunctions.BVLC_SC_PEER_TO_PEER_FUNCTION:
                    // 数据帧，继续向上传递
                    break;

                default:
                    Log.Debug($"忽略 SC 功能码：{function}");
                    return;
            }

            // 检查数据是否足够
            if (length <= headerLen)
            {
                Log.Warn($"BACnet/SC 帧缺少数据");
                return;
            }

            // 构建传递给上层的地址
            var remoteAddress = new BacnetAddress(BacnetAddressTypes.IP, 0, []);
            InvokeMessageRecieved(buffer, headerLen, length - headerLen, remoteAddress);
        }
        catch (Exception ex)
        {
            Log.Error($"处理 SC 数据异常：{ex.Message}");
        }
    }
#endif

    /// <summary>发送数据</summary>
    /// <param name="buffer">数据缓冲区（含 BVLC 头空间）</param>
    /// <param name="offset">NPDU 数据偏移</param>
    /// <param name="dataLength">NPDU 数据长度</param>
    /// <param name="address">目标地址</param>
    /// <param name="waitForTransmission">是否等待发送完成</param>
    /// <param name="timeout">超时（毫秒）</param>
    /// <returns>发送字节数</returns>
    public override Int32 Send(Byte[] buffer, Int32 offset, Int32 dataLength, BacnetAddress address, Boolean waitForTransmission, Int32 timeout)
    {
#if NETFRAMEWORK
        throw new PlatformNotSupportedException("BACnet/SC 在 .NET Framework 4.5 上不受支持");
#else
        if (_webSocket == null || _webSocket.State != WebSocketState.Open)
            return 0;

        // 编码 BVLC 头（使用 Hub Function 或 Peer-to-Peer Function）
        var function = (address.net == 0xFFFF)
            ? BacnetBvlcScFunctions.BVLC_SC_HUB_FUNCTION
            : BacnetBvlcScFunctions.BVLC_SC_PEER_TO_PEER_FUNCTION;

        var fullLength = dataLength + SC_HEADER_LENGTH;
        BVLCSC.Encode(buffer, function, fullLength);

        try
        {
            lock (_sendLock)
            {
                var segment = new ArraySegment<Byte>(buffer, 0, fullLength);
                var sendTask = _webSocket.SendAsync(segment, WebSocketMessageType.Binary, true, CancellationToken.None);

                if (waitForTransmission)
                    sendTask.GetAwaiter().GetResult();
            }

            return dataLength;
        }
        catch (Exception ex)
        {
            Log.Error($"BACnet/SC 发送异常：{ex.Message}");
            return 0;
        }
#endif
    }

    /// <summary>获取广播地址（SC 模式通过 Hub 转发，返回默认地址）</summary>
    public override BacnetAddress GetBroadcastAddress()
    {
        return new BacnetAddress(BacnetAddressTypes.IP, 0xFFFF, []);
    }

    /// <summary>释放资源</summary>
    public override void Dispose()
    {
        if (_disposing) return;
        _disposing = true;

        _state = ScTransportState.Disconnected;

#if !NETFRAMEWORK
        _receiveCts?.Cancel();
        _receiveCts?.Dispose();
        _receiveCts = null;

        if (_webSocket != null)
        {
            try
            {
                if (_webSocket.State == WebSocketState.Open || _webSocket.State == WebSocketState.CloseReceived)
                {
                    var closeTask = _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Dispose", CancellationToken.None);
                    closeTask.GetAwaiter().GetResult();
                }
            }
            catch
            {
                // ignore
            }
            finally
            {
                _webSocket.Dispose();
                _webSocket = null;
            }
        }
#endif
    }

    /// <summary>触发断开事件</summary>
    private void OnDisconnected(String reason)
    {
        try
        {
            Disconnected?.Invoke(this, reason);
        }
        catch
        {
            // ignore
        }
    }

#if !NETFRAMEWORK
    /// <summary>清理资源（不触发事件）</summary>
    private void Cleanup()
    {
        _receiveCts?.Cancel();
        _receiveCts?.Dispose();
        _receiveCts = null;

        _webSocket?.Dispose();
        _webSocket = null;
    }
#endif

    /// <summary>转换 SC URI 为 BacnetAddress</summary>
    public static void Convert(Uri uri, out BacnetAddress address)
    {
        var uriBytes = System.Text.Encoding.UTF8.GetBytes(uri.ToString());
        address = new BacnetAddress(BacnetAddressTypes.IP, 0, uriBytes);
    }

    /// <summary>转换为字符串</summary>
    public override String ToString()
    {
        return $"SC:{Uri}";
    }
}
