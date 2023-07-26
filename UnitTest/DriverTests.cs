using System;
using System.Threading;
using NewLife.BACnet.Drivers;
using NewLife.IoT.ThingModels;
using NewLife.Log;
using NewLife.Serialization;
using NewLife.UnitTest;
using Xunit;

namespace UnitTest;

[TestCaseOrderer("NewLife.UnitTest.PriorityOrderer", "NewLife.UnitTest")]
public class DriverTests
{
    static BACnetDriver _driver;
    static BACnetParameter _parameter;

    static DriverTests()
    {
#if DEBUG
        XTrace.Log.Level = LogLevel.Debug;
#endif

        _driver = new BACnetDriver
        {
            Log = XTrace.Log,
        };

        _parameter = new BACnetParameter
        {
            //Address = "127.0.0.1",
            Port = 47808,
            DeviceId = 666,
        };
    }

    [Fact]
    [TestOrder(10)]
    public void GetDefaultParameter()
    {
        var driver = new BACnetDriver();

        var ps = driver.CreateParameter(null);
        Assert.NotNull(ps);

        var bp = ps as BACnetParameter;
        Assert.NotNull(bp);
        Assert.Equal(0xbac0, bp.Port);
    }

    [Fact]
    [TestOrder(20)]
    public void Open()
    {
        var driver = _driver;
        var dev = new ThingDevice();
        var node = driver.Open(dev, _parameter);
        Assert.NotNull(node);

        var bacNode = node as BACnetNode;
        Assert.NotNull(bacNode);
        Assert.NotNull(bacNode.Client);

        //bacNode.Client.Open();

        //var client = driver.GetValue("_client");
        var client = driver.Client;
        Assert.Equal(client, bacNode.Client);

        driver.Close(node);

        client = driver.Client;
        Assert.Null(client);
    }

    [Fact]
    [TestOrder(30)]
    public void Scan()
    {
        var driver = _driver;
        var dev = new ThingDevice();
        var node = driver.Open(dev, _parameter);

        var client = driver.Client;
        client.Scan();

        var nodes = client.Nodes;
        Assert.True(nodes.Count > 0);
    }

    [Fact]
    [TestOrder(40)]
    public void Read()
    {
        var driver = _driver;
        _parameter.DeviceId = (Int32)driver.Client.Nodes[0].DeviceId;

        var dev = new ThingDevice();
        var node = driver.Open(dev, _parameter);
        //Thread.Sleep(500);

        var point = new PointModel { Name = "A_value", Address = "0_2" };
        for (var i = 0; i < 5; i++)
        {
            var rs = driver.Read(node, new[] { point });
            Assert.NotNull(rs);
            Assert.Single(rs);
            XTrace.WriteLine(rs.ToJson());

            Thread.Sleep(100);
        }
    }
}