using System.IO.BACnet;

namespace NewLife.BACnet.Protocols;

public class BacNode
{
    public BacnetAddress Address { get; set; }
    public UInt32 DeviceId { get; set; }

    public List<BacProperty> Properties { get; set; } = new();

    public BacNode(BacnetAddress adr, UInt32 deviceId)
    {
        Address = adr;
        DeviceId = deviceId;
    }

    public BacnetAddress GetAdd(UInt32 deviceId) => DeviceId == deviceId ? Address : null;
}