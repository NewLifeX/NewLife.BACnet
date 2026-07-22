namespace System.IO.BACnet;

/// <summary>
/// 异步操作结果，支持同步（IAsyncResult/WaitHandle）和异步（Task）两种模式，
/// 用于 BACnet 确认服务的请求-响应匹配。
/// </summary>
public class BacnetAsyncResult : IAsyncResult, IDisposable
{
    private BacnetClient _comm;
    private readonly Byte _waitInvokeId;
    private Exception _error;
    private readonly Byte[] _transmitBuffer;
    private readonly Int32 _transmitLength;
    private readonly Boolean _waitForTransmit;
    private readonly Int32 _transmitTimeout;
    private ManualResetEvent _waitHandle;
    private readonly CancellationTokenSource _timeoutCts;
    private readonly TaskCompletionSource<Byte[]> _tcs;
    private Int32 _completed;

    public Boolean Segmented { get; private set; }
    public Byte[] Result { get; private set; }
    public Object AsyncState { get; set; }
    public Boolean CompletedSynchronously { get; private set; }
    public WaitHandle AsyncWaitHandle => _waitHandle;
    public Boolean IsCompleted => _waitHandle?.WaitOne(0) ?? true;
    public BacnetAddress Address { get; }

    /// <summary>
    /// 获取可用于 await 的 Task，在请求完成时返回响应数据。
    /// 简单确认（SimpleAck）返回空数组；复杂确认（ComplexAck）返回完整 APDU 数据；
    /// 出错时抛出异常。
    /// </summary>
    public Task<Byte[]> Task => _tcs.Task;

    public Exception Error
    {
        get => _error;
        set
        {
            _error = value;
            CompletedSynchronously = true;
            _waitHandle?.Set();
            TryCompleteTcs(null, value);
        }
    }

    public BacnetAsyncResult(BacnetClient comm, BacnetAddress adr, Byte invokeId,
        Byte[] transmitBuffer, Int32 transmitLength, Boolean waitForTransmit, Int32 transmitTimeout,
        CancellationToken cancellationToken = default)
    {
        _transmitTimeout = transmitTimeout;
        Address = adr;
        _waitForTransmit = waitForTransmit;
        _transmitBuffer = transmitBuffer;
        _transmitLength = transmitLength;
        _comm = comm;
        _waitInvokeId = invokeId;
        _comm.OnComplexAck += OnComplexAck;
        _comm.OnError += OnError;
        _comm.OnAbort += OnAbort;
        _comm.OnReject += OnReject;
        _comm.OnSimpleAck += OnSimpleAck;
        _comm.OnSegment += OnSegment;
        _waitHandle = new ManualResetEvent(false);
#if NET45
        _tcs = new TaskCompletionSource<Byte[]>();
#else
        _tcs = new TaskCompletionSource<Byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
#endif

        // 超时取消
        if (transmitTimeout > 0)
        {
            _timeoutCts = new CancellationTokenSource(transmitTimeout);
            _timeoutCts.Token.Register(() => TryCompleteTcs(null, new TimeoutException("BACnet request timed out")));
        }

        // 外部 CancellationToken 联动
        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() => TryCompleteTcs(null, new OperationCanceledException(cancellationToken)));
        }
    }

    public void Resend()
    {
        try
        {
            if (_comm.Transport.Send(_transmitBuffer, _comm.Transport.HeaderLength, _transmitLength, Address, _waitForTransmit, _transmitTimeout) < 0)
            {
                Error = new IOException("Write Timeout");
            }
        }
        catch (Exception ex)
        {
            Error = new Exception($"Write Exception: {ex.Message}");
        }
    }

    /// <summary>尝试完成 TCS，保证只执行一次</summary>
    private Boolean TryCompleteTcs(Byte[] result, Exception error)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
            return false;

        if (error != null)
            return _tcs.TrySetException(error);

        return _tcs.TrySetResult(result ?? []);
    }

    private void OnSegment(BacnetClient sender, BacnetAddress adr, BacnetPduTypes type, BacnetConfirmedServices service, Byte invokeId, BacnetMaxSegments maxSegments, BacnetMaxAdpu maxAdpu, Byte sequenceNumber, Byte[] buffer, Int32 offset, Int32 length)
    {
        if (invokeId != _waitInvokeId)
            return;

        Segmented = true;
        _waitHandle.Set();
    }

    private void OnSimpleAck(BacnetClient sender, BacnetAddress adr, BacnetPduTypes type, BacnetConfirmedServices service, Byte invokeId, Byte[] data, Int32 dataOffset, Int32 dataLength)
    {
        if (invokeId != _waitInvokeId)
            return;

        _waitHandle.Set();
        TryCompleteTcs([], null);
    }

    private void OnAbort(BacnetClient sender, BacnetAddress adr, BacnetPduTypes type, Byte invokeId, BacnetAbortReason reason, Byte[] buffer, Int32 offset, Int32 length)
    {
        if (invokeId != _waitInvokeId)
            return;

        Error = new Exception($"Abort from device, reason: {reason}");
    }

    private void OnReject(BacnetClient sender, BacnetAddress adr, BacnetPduTypes type, Byte invokeId, BacnetRejectReason reason, Byte[] buffer, Int32 offset, Int32 length)
    {
        if (invokeId != _waitInvokeId)
            return;

        Error = new Exception($"Reject from device, reason: {reason}");
    }

    private void OnError(BacnetClient sender, BacnetAddress adr, BacnetPduTypes type, BacnetConfirmedServices service, Byte invokeId, BacnetErrorClasses errorClass, BacnetErrorCodes errorCode, Byte[] buffer, Int32 offset, Int32 length)
    {
        if (invokeId != _waitInvokeId)
            return;

        Error = new Exception($"Error from device: {errorClass} - {errorCode}");
    }

    private void OnComplexAck(BacnetClient sender, BacnetAddress adr, BacnetPduTypes type, BacnetConfirmedServices service, Byte invokeId, Byte[] buffer, Int32 offset, Int32 length)
    {
        if (invokeId != _waitInvokeId)
            return;

        Segmented = false;
        Result = new Byte[length];

        if (length > 0)
            Array.Copy(buffer, offset, Result, 0, length);

        //notify waiter even if segmented
        _waitHandle.Set();

        // 非分片消息才能完成 TCS（分片由 PerformDefaultSegmentHandling 组装后重新触发）
        TryCompleteTcs(Result, null);
    }

    /// <summary>
    /// Will continue waiting until all segments are recieved
    /// </summary>
    public Boolean WaitForDone(Int32 timeout)
    {
        while (true)
        {
            if (!AsyncWaitHandle.WaitOne(timeout))
                return false;
            if (Segmented)
                _waitHandle.Reset();
            else
                return true;
        }
    }

    public void Dispose()
    {
        if (_comm != null)
        {
            _comm.OnComplexAck -= OnComplexAck;
            _comm.OnError -= OnError;
            _comm.OnAbort -= OnAbort;
            _comm.OnReject -= OnReject;
            _comm.OnSimpleAck -= OnSimpleAck;
            _comm.OnSegment -= OnSegment;
            _comm = null;
        }

        _timeoutCts?.Dispose();

        // 如果 TCS 尚未完成，标记为已取消
        if (Interlocked.Exchange(ref _completed, 1) == 0)
            _tcs.TrySetCanceled();

        if (_waitHandle != null)
        {
            _waitHandle.Dispose();
            _waitHandle = null;
        }
    }
}
