using NewLife.BACnet.Protocols;
using NewLife.IoT.ThingModels;
using NewLife.Log;
using NewLife.Model;
using Stardust;

XTrace.UseConsole();
#if DEBUG
XTrace.Log.Level = LogLevel.Debug;
#endif

var services = ObjectContainer.Current;
var star = services.AddStardust();

var deviceId = 666;
XTrace.WriteLine("BACnet 客户端 deviceId={0}", deviceId);

var client = new BacClient
{
    //Address = NetHelper.MyIP() + "",
    //Port = 53817,
    DeviceId = deviceId,

    Tracer = star?.Tracer,
    Log = XTrace.Log
};

client.Open();

// 定时读取数据
for (var i = 0; i < 100; i++)
{
    var point = new PointModel { };
    //client.Read(null, point);

    Thread.Sleep(1000);
}

Thread.Sleep(-1);