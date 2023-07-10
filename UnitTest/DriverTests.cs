using NewLife.BACnet.Drivers;
using NewLife.IoT.Models;
using Xunit;
using NewLife.IoT.Drivers;
using NewLife.Reflection;
using NewLife.IoT.ThingModels;

namespace UnitTest;

public class DriverTests
{
    [Fact]
    public void GetDefaultParameter()
    {
        var driver = new BACnetDriver();

        var ps = driver.GetDefaultParameter();
        Assert.NotNull(ps);

        var bp = ps as BACnetParameter;
        Assert.NotNull(bp);
        Assert.Equal(0xbac0, bp.Port);
    }

    [Fact]
    public void Open()
    {
        var driver = new BACnetDriver();

        var ps = new BACnetParameter
        {
            Address = "127.0.0.1",
            Port = 47808,
            DeviceId = 12345,
        };

        var node = driver.Open(null, ps);
        Assert.NotNull(node);

        var bacNode = node as BACnetNode;
        Assert.NotNull(bacNode);
        Assert.NotNull(bacNode.Client);

        bacNode.Client.Open();

        var client = driver.GetValue("_client");
        Assert.Equal(client, bacNode.Client);

        driver.Close(node);

        client = driver.GetValue("_client");
        Assert.Null(client);
    }

    [Fact]
    public void Read()
    {
        var driver = new BACnetDriver();

        var ps = new BACnetParameter
        {
            Address = "127.0.0.1",
            Port = 47808,
            DeviceId = 12345,
        };

        var node = driver.Open(null, ps) as BACnetNode;

        var point = new PointModel { };
        var rs = driver.Read(node, new[] { point });
        Assert.NotNull(rs);
        Assert.Single(rs);
    }
}