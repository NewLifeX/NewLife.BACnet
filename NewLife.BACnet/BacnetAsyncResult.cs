namespace System.IO.BACnet;

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

    public Boolean Segmented { get; private set; }
    public Byte[] Result { get; private set; }
    public Object AsyncState { get; set; }
    public Boolean CompletedSynchronously { get; private set; }
    public WaitHandle AsyncWaitHandle => _waitHandle;
    public Boolean IsCompleted => _waitHandle.WaitOne(0);
    public BacnetAddress Address { get; }

    public Exception Error
    {
        get => _error;
        set
        {
            _error = value;
            CompletedSynchronously = true;
            _waitHandle.Set();
        }
    }

    public BacnetAsyncResult(BacnetClient comm, BacnetAddress adr, Byte invokeId, Byte[] transmitBuffer, Int32 transmitLength, Boolean waitForTransmit, Int32 transmitTimeout)
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

        if (_waitHandle != null)
        {
            _waitHandle.Dispose();
            _waitHandle = null;
        }
    }
}
