using System.IO.BACnet;
using System.IO.BACnet.Storage;
using NewLife.BACnet.Protocols;
using NewLife.Log;
using NewLife.Model;
using NewLife.Security;
using NewLife.Threading;
using Stardust;
using Object = System.Object;

XTrace.UseConsole();
#if DEBUG
XTrace.Log.Level = LogLevel.Debug;
#endif

var services = ObjectContainer.Current;
var star = services.AddStardust();

//var deviceId = Rand.Next(100, 1000);
var deviceId = 666;
XTrace.WriteLine("BACnet 服务端 deviceId={0}", deviceId);

var server = new BacServer
{
    DeviceId = deviceId,
    StorageFile = "DeviceDescriptor.xml",

    Tracer = star?.Tracer,
    Log = XTrace.Log,
};

server.Open();

var OBJECT_ANALOG_VALUE_0 = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, 0);
var OBJECT_ANALOG_INPUT_0 = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 0);

// 定时改变数据
var timer = new TimerX(DoCheck, server.Storage, 0, 5_000) { Async = true };

Thread.Sleep(-1);

void DoCheck(Object? state)
{
    var ds = state as DeviceStorage;
    lock (ds)
    {
        // 写入随机数，方便测试
        var bv = new[] { new BacnetValue(Rand.Next(1000, 10000)) };
        ds.WriteProperty(OBJECT_ANALOG_VALUE_0, BacnetPropertyIds.PROP_PRESENT_VALUE, 1, bv, true);

        ds.ReadProperty(OBJECT_ANALOG_VALUE_0, BacnetPropertyIds.PROP_PRESENT_VALUE, 1, out var valtoread);
        var coef = Convert.ToDouble(valtoread[0].Value);

        var sin = (Single)(coef * Math.Sin(0.1));
        var valtowrite = new[] { new BacnetValue(sin) };
        ds.WriteProperty(OBJECT_ANALOG_INPUT_0, BacnetPropertyIds.PROP_PRESENT_VALUE, 1, valtowrite, true);
    }
}