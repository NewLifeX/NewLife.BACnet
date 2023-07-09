using NewLife.Log;

namespace System.IO.BACnet;

public abstract class BacnetTransportBase : IBacnetTransport
{
    /// <summary>性能追踪</summary>
    public ITracer Tracer { get; set; }

    /// <summary>日志</summary>
    public ILog Log { get; set; } = XTrace.Log;

    public int HeaderLength { get; protected set; }
    public int MaxBufferLength { get; protected set; }
    public BacnetAddressTypes Type { get; protected set; }
    public BacnetMaxAdpu MaxAdpuLength { get; protected set; }
    public byte MaxInfoFrames { get; set; } = 0xFF;

    protected BacnetTransportBase()
    {
    }

    public abstract void Start();

    public abstract BacnetAddress GetBroadcastAddress();

    public virtual bool WaitForAllTransmits(int timeout)
    {
        return true; // not used 
    }

    public abstract int Send(byte[] buffer, int offset, int dataLength, BacnetAddress address, bool waitForTransmission, int timeout);

    public event MessageRecievedHandler MessageRecieved;

    protected void InvokeMessageRecieved(byte[] buffer, int offset, int msgLength, BacnetAddress remoteAddress)
    {
        MessageRecieved?.Invoke(this, buffer, offset, msgLength, remoteAddress);
    }

    public abstract void Dispose();
}
