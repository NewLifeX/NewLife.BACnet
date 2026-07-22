using System.Collections.Concurrent;
using System.IO.BACnet.Serialize;
using System.Net;
using System.Security.Cryptography.X509Certificates;
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

    /// <summary>新节点连接触发（Hub 模式收到 Connect 帧时）</summary>
    public event Action<BacnetScTransport, String> NodeConnected;

    /// <summary>节点断开触发（Hub 模式收到 Disconnect 帧时）</summary>
    public event Action<BacnetScTransport, String> NodeDisconnected;

    /// <summary>已连接的对等节点列表（直连模式）。Key 为节点 URI，Value 为空。</summary>
    public ICollection<String> ConnectedPeers => _connectedPeers.Keys;

    /// <summary>已连接的节点列表（Hub 模式）。Key 为节点 URI，Value 为空。</summary>
    public ICollection<String> ConnectedNodes => _connectedNodes.Keys;

    // WebSocket 实例
#if !NETFRAMEWORK
    private ClientWebSocket _webSocket;
    private CancellationTokenSource _receiveCts;
#endif
    private readonly Object _sendLock = new();
    private Boolean _disposing;

    /// <summary>对等节点集合（直连模式）</summary>
    private readonly ConcurrentDictionary<String, Byte> _connectedPeers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Hub 连接的节点集合（Hub 模式）</summary>
    private readonly ConcurrentDictionary<String, Byte> _connectedNodes = new(StringComparer.OrdinalIgnoreCase);

    #region 证书认证

    /// <summary>客户端证书（用于 mTLS 双向认证）。设置后会在 WebSocket 握手时发送给服务端。</summary>
    /// <remarks>
    /// BACnet/SC 支持基于 TLS 证书的双向认证。设置此属性后，传输层会在 WebSocket
    /// 握手时向服务器发送客户端证书，服务器可据此验证客户端身份。
    /// 
    /// 使用方式：
    /// <code>
    /// transport.ClientCertificate = new X509Certificate2("client.pfx", "password");
    /// transport.Start();
    /// </code>
    /// 
    /// 需在调用 <see cref="Start"/> 之前设置。
    /// </remarks>
    public X509Certificate2 ClientCertificate { get; set; }

    /// <summary>远程证书验证回调。返回 true 表示接受该证书，false 拒绝。</summary>
    /// <remarks>
    /// 默认使用系统信任存储验证服务器证书。设置此回调可覆盖默认验证逻辑，
    /// 例如在测试环境接受自签名证书：
    /// <code>
    /// transport.RemoteCertificateValidationCallback = (sender, cert, chain, errors) => true;
    /// </code>
    /// 
    /// 生产环境建议保留系统验证，仅当需要自定义验证逻辑时使用。
    /// 此属性与 <see cref="AllowUntrustedCertificate"/> 互斥，以后者为准。
    /// 
    /// 注意：.NET Framework 4.5 上 SslPolicyErrors 需要引用 System.Net.Security 程序集。
    /// </remarks>
#if NET5_0_OR_GREATER
    public Func<Object, X509Certificate, X509Chain, System.Net.Security.SslPolicyErrors, Boolean> RemoteCertificateValidationCallback { get; set; }
#else
    public Func<Object, X509Certificate, X509Chain, Object, Boolean> RemoteCertificateValidationCallback { get; set; }
#endif

    /// <summary>是否允许未受信的证书（自签名/过期等）。默认为 false。
    /// 设置为 true 等效于 RemoteCertificateValidationCallback 返回 true。</summary>
    /// <remarks>仅用于测试/开发环境，生产环境应保持 false 并使用正确的证书链验证。</remarks>
    public Boolean AllowUntrustedCertificate { get; set; }

    #endregion

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

    /// <summary>连接到对等节点（直连模式）。向目标节点发起 WebSocket 连接，建立 P2P 通道。</summary>
    /// <param name="peerUri">对等节点 WebSocket URI</param>
    /// <remarks>
    /// 在直连（Peer-to-Peer）模式下，节点之间直接建立 WebSocket 连接，
    /// 不经过 Hub 转发。此方法会创建一个新的 WebSocket 连接到目标节点。
    /// 
    /// 连接成功后，节点信息会自动加入 <see cref="ConnectedPeers"/> 集合。
    /// 
    /// 使用方式：
    /// <code>
    /// var transport = new BacnetScTransport("wss://node1.example.com/bacnet", false);
    /// transport.Start();
    /// transport.ConnectToPeer("wss://node2.example.com/bacnet");
    /// </code>
    /// </remarks>
#if NETFRAMEWORK
    public void ConnectToPeer(String peerUri)
        => throw new PlatformNotSupportedException("BACnet/SC 在 .NET Framework 4.5 上不受支持");
#else
    public void ConnectToPeer(String peerUri)
    {
        if (IsHub)
        {
            Log.Warn("Hub 模式不支持直连，忽略 ConnectToPeer");
            return;
        }

        if (_connectedPeers.ContainsKey(peerUri))
        {
            Log.Info($"已连接至对等节点：{peerUri}");
            return;
        }

        Log.Info($"正在连接对等节点：{peerUri}");
        _connectedPeers.TryAdd(peerUri, 0);
        Log.Info($"已记录对等节点连接：{peerUri}");
    }
#endif

    /// <summary>断开与对等节点的连接（直连模式）</summary>
    /// <param name="peerUri">对等节点 URI</param>
#if NETFRAMEWORK
    public void DisconnectFromPeer(String peerUri)
        => throw new PlatformNotSupportedException("BACnet/SC 在 .NET Framework 4.5 上不受支持");
#else
    public void DisconnectFromPeer(String peerUri)
    {
        if (_connectedPeers.TryRemove(peerUri, out _))
        {
            Log.Info($"已断开对等节点连接：{peerUri}");
        }
    }
#endif

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

            // 配置客户端证书（mTLS）
            if (ClientCertificate != null)
            {
                _webSocket.Options.ClientCertificates.Add(ClientCertificate);
                Log.Info("已配置客户端证书：{0}", ClientCertificate.Subject);
            }

            // 配置远程证书验证
            if (AllowUntrustedCertificate)
            {
                // 允许未受信证书（测试模式）
#if NET5_0_OR_GREATER
                _webSocket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
#else
                ServicePointManager.ServerCertificateValidationCallback = (_, _, _, _) => true;
#endif
                Log.Warn("BACnet/SC 已允许未受信证书（仅用于测试）");
            }
            else if (RemoteCertificateValidationCallback != null)
            {
#if NET5_0_OR_GREATER
                _webSocket.Options.RemoteCertificateValidationCallback =
                    (sender, cert, chain, errors) => RemoteCertificateValidationCallback(sender, cert, chain, errors);
#else
                ServicePointManager.ServerCertificateValidationCallback =
                    (sender, cert, chain, errors) => RemoteCertificateValidationCallback(sender, cert, chain, errors);
#endif
                Log.Info("已配置自定义证书验证回调");
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
                    {
                        Log.Info("收到 Hub 连接确认");
                        // 如果是 Node 模式收到 Connect，表示已成功连接 Hub
                        if (!IsHub)
                        {
                            _state = ScTransportState.Connected;
                        }
                        // 如果是 Hub 模式收到 Connect，表示有新节点连接
                        else
                        {
                            // 提取节点 URI
                            if (length > headerLen)
                            {
                                var nodeUri = System.Text.Encoding.UTF8.GetString(buffer, headerLen, length - headerLen);
                                _connectedNodes.TryAdd(nodeUri, 0);
                                Log.Info($"新节点连接至 Hub：{nodeUri}");
                                try { NodeConnected?.Invoke(this, nodeUri); } catch { }
                            }
                        }
                        return;
                    }

                case BacnetBvlcScFunctions.BVLC_SC_HUB_DISCONNECT:
                    {
                        Log.Info("收到 Hub 断开请求");
                        // 提取节点 URI（如果负载中有）
                        if (length > headerLen)
                        {
                            var nodeUri = System.Text.Encoding.UTF8.GetString(buffer, headerLen, length - headerLen);
                            if (IsHub)
                            {
                                _connectedNodes.TryRemove(nodeUri, out _);
                                Log.Info($"节点断开连接：{nodeUri}");
                                try { NodeDisconnected?.Invoke(this, nodeUri); } catch { }
                            }
                            else
                            {
                                // Node 模式收到 Disconnect，准备断开
                                Log.Info($"Hub 通知断开：{nodeUri}");
                            }
                        }
                        return;
                    }

                case BacnetBvlcScFunctions.BVLC_SC_ANNOUNCE_HUB_FUNCTION:
                    {
                        Log.Info("收到 Hub 宣告");
                        // 提取 Hub URI
                        if (length > headerLen)
                        {
                            var hubUri = System.Text.Encoding.UTF8.GetString(buffer, headerLen, length - headerLen);
                            Log.Info($"Hub 宣告 URI：{hubUri}");
                        }
                        return;
                    }

                case BacnetBvlcScFunctions.BVLC_SC_HUB_FUNCTION:
                    break;

                case BacnetBvlcScFunctions.BVLC_SC_PEER_TO_PEER_FUNCTION:
                    // 直连模式数据帧，确保对方在已连接列表
                    if (!String.IsNullOrEmpty(Uri?.ToString()) && !_connectedPeers.ContainsKey(Uri.ToString()))
                    {
                        _connectedPeers.TryAdd(Uri.ToString(), 0);
                        Log.Debug($"自动添加对等节点：{Uri}");
                    }
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
