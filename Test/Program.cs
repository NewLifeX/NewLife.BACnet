using NewLife;
using NewLife.BACnet.Protocols;
using NewLife.Log;

XTrace.UseConsole();
#if DEBUG
XTrace.Log.Level = LogLevel.Debug;
#endif

XTrace.WriteLine("BACnet 测试");

var server = new BacServer
{
    DeviceId = 777,
    StorageFile = "DeviceDescriptor.xml",

    Log = XTrace.Log,
};

server.Open();

var client = new BacClient
{
    //Address = NetHelper.MyIP() + "",
    //Port = 53817,
    DeviceId = 66,

    Log = XTrace.Log
};

client.Open();

Thread.Sleep(-1);