using NewLife;
using NewLife.BACnet.Protocols;
using NewLife.Log;

XTrace.UseConsole();
XTrace.Log.Level = LogLevel.Debug;

var client = new BACnetClient
{
    //Address = NetHelper.MyIP() + "",
    //Port = 53817,
    DeviceId = 66,

    Log = XTrace.Log
};

client.Open();

Thread.Sleep(-1);