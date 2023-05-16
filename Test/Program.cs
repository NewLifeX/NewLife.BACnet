using System.IO.BACnet;
using NewLife.BACnet.Protocols;
using NewLife.Log;
using NewLife.Threading;

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

var OBJECT_ANALOG_VALUE_0 = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, 0);
var OBJECT_ANALOG_INPUT_0 = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 0);
Double count = 0;

// 定时改变数据
var timer = new TimerX(s =>
{
    lock (server.Storage)         // read and write callback are fired in a separated thread, so multiple access needs protection
    {
        // Read the Present Value
        // index 0 : number of values in the array
        // index 1 : first value
        server.Storage.ReadProperty(OBJECT_ANALOG_VALUE_0, BacnetPropertyIds.PROP_PRESENT_VALUE, 1, out var valtoread);
        // Get the first ... and here the only element
        var coef = Convert.ToDouble(valtoread[0].Value);

        var sin = (Single)(coef * Math.Sin(count));
        // Write the Present Value
        var valtowrite = new BacnetValue[1] { new BacnetValue(sin) };
        server.Storage.WriteProperty(OBJECT_ANALOG_INPUT_0, BacnetPropertyIds.PROP_PRESENT_VALUE, 1, valtowrite, true);
    }
    Thread.Sleep(1000);
    count += 0.1;
}, null, 1_000, 1_000)
{ Async = true };

var client = new BacClient
{
    //Address = NetHelper.MyIP() + "",
    //Port = 53817,
    DeviceId = 66,

    Log = XTrace.Log
};

client.Open();

// 定时读取数据
for (int i = 0; i < 100; i++)
{
    BacnetValue Value;
    bool ret;
    // Read Present_Value property on the object ANALOG_INPUT:0 provided by the device 12345
    // Scalar value only
    ret = ReadScalarValue(12345, new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 0), BacnetPropertyIds.PROP_PRESENT_VALUE, out Value);

    if (ret == true)
    {
        Console.WriteLine("Read value : " + Value.Value.ToString());

        // Write Present_Value property on the object ANALOG_OUTPUT:0 provided by the device 4000
        BacnetValue newValue = new BacnetValue(Convert.ToSingle(Value.Value));   // expect it's a float
        var adr = DeviceAddr((uint)client.DeviceId);
        if (bacnet_client.ReadPropertyRequest(adr, BacnetObjet, Propriete, out NoScalarValue))
        { }

        ret = WriteScalarValue(4000, new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_OUTPUT, 0), BacnetPropertyIds.PROP_PRESENT_VALUE, newValue);

        if (bacnet_client.WritePropertyRequest(adr, BacnetObjet, Propriete, NoScalarValue))
        { }

        Console.WriteLine("Write feedback : " + ret.ToString());
    }
    else
        Console.WriteLine("Error somewhere !");
}

Thread.Sleep(-1);