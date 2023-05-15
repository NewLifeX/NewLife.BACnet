using NewLife;
using NewLife.BACnet.Protocols;
using NewLife.Log;

XTrace.UseConsole();
XTrace.Log.Level = LogLevel.Debug;

var server = new BACnetServer
{
    DeviceId = 1234,
    StorageFile = "DeviceDescriptor.xml",

    Log = XTrace.Log,
};

server.Open();

var client = new BACnetClient
{
    //Address = NetHelper.MyIP() + "",
    //Port = 53817,
    DeviceId = 66,

    Log = XTrace.Log
};

client.Open();

Thread.Sleep(-1);