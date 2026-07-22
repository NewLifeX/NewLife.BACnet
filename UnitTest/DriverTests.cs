using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NewLife.BACnet.Drivers;
using NewLife.BACnet.Protocols;
using NewLife.IoT.Drivers;
using NewLife.IoT.ThingModels;
using NewLife.Log;
using NewLife.UnitTest;
using Xunit;

namespace UnitTest;

[Collection("DriverLoopback")]
[TestCaseOrderer("NewLife.UnitTest.PriorityOrderer", "NewLife.UnitTest")]
public class DriverTests : IDisposable
{
    private const Int32 TestDeviceId = 9002;
    private readonly BacServer _server;
    private readonly Int32 _serverPort;
    private readonly BACnetDriver _driver;
    private readonly BACnetParameter _parameter;

    public DriverTests()
    {
#if DEBUG
        XTrace.Log.Level = LogLevel.Debug;
#endif
        _serverPort = GetFreeUdpPort();
        _server = new BacServer
        {
            DeviceId = TestDeviceId,
            StorageFile = "TestDeviceDescriptor.xml",
            Port = _serverPort,
            Log = XTrace.Log,
        };
        _server.Open();

        var clientPort = GetFreeUdpPort();
        _driver = new BACnetDriver { Log = XTrace.Log };
        _parameter = new BACnetParameter
        {
            Port = clientPort,
            DeviceId = TestDeviceId,
            TargetAddress = $"127.0.0.1:{_serverPort}",
        };
    }

    public void Dispose()
    {
        try { _server?.Dispose(); } catch { }
    }

    private static Int32 GetFreeUdpPort()
    {
        var ep = new IPEndPoint(IPAddress.Loopback, 0);
        using var sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        sock.Bind(ep);
        return ((IPEndPoint)sock.LocalEndPoint).Port;
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
        var dev = new ThingDevice();
        var node = ((IDriver)_driver).Open(dev, _parameter);
        Assert.NotNull(node);

        var bacNode = node as BACnetNode;
        Assert.NotNull(bacNode);
        Assert.NotNull(bacNode.Client);

        var client = _driver.Client;
        Assert.Equal(client, bacNode.Client);

        ((IDriver)_driver).Close(node);

        client = _driver.Client;
        Assert.Null(client);
    }

    [Fact]
    [TestOrder(30)]
    public void Scan()
    {
        var dev = new ThingDevice();
        ((IDriver)_driver).Open(dev, _parameter);

        var client = _driver.Client;
        client.Scan();
        Thread.Sleep(800);

        var nodes = client.Nodes;
        Assert.True(nodes.Count > 0);
    }

    [Fact]
    [TestOrder(40)]
    public void Read()
    {
        var dev = new ThingDevice();
        var node = ((IDriver)_driver).Open(dev, _parameter);
        Thread.Sleep(500);

        var point = new PointModel { Name = "A_value", Address = "0_2" };
        for (var i = 0; i < 5; i++)
        {
            var rs = ((IDriver)_driver).Read(node, new[] { (IPoint)point });
            Assert.NotNull(rs);
            Assert.True(rs.IsSuccess);
            Assert.Single(rs.Points);
            XTrace.WriteLine("{0}={1}", rs.Points[0].Name, rs.Values[0]);
            Thread.Sleep(100);
        }
    }

    #region DRV-7 写入类型转换

    [Fact]
    [TestOrder(50)]
    public async Task Write_Int32ToReal()
    {
        var dev = new ThingDevice();
        var node = ((IDriver)_driver).Open(dev, _parameter);
        Thread.Sleep(500);

        // 写入整数，驱动应自动转换为 Real
        var point = new PointModel { Name = "A_value", Address = "0_2" };
        var requests = new[] { new WriteRequest { Point = point, Value = 123 } };
        var rs = await _driver.WriteAsync(node, requests);
        Assert.True(rs.IsSuccess);
    }

    [Fact]
    [TestOrder(55)]
    public async Task Write_DoubleToReal()
    {
        var dev = new ThingDevice();
        var node = ((IDriver)_driver).Open(dev, _parameter);
        Thread.Sleep(500);

        var point = new PointModel { Name = "A_value", Address = "0_2" };
        var requests = new[] { new WriteRequest { Point = point, Value = 456.78 } };
        var rs = await _driver.WriteAsync(node, requests);
        Assert.True(rs.IsSuccess);
    }

    #endregion

    #region DRV-6 批量读取

    [Fact]
    [TestOrder(60)]
    public async Task Read_Multiple()
    {
        var dev = new ThingDevice();
        var node = ((IDriver)_driver).Open(dev, _parameter);
        Thread.Sleep(500);

        // 批量读取多个点位
        var points = new IPoint[]
        {
            new PointModel { Name = "AI0", Address = "0_0" },
            new PointModel { Name = "AV0", Address = "0_2" },
        };

        var rs = await _driver.ReadAsync(node, points);
        Assert.True(rs.IsSuccess);
        Assert.Equal(2, rs.Points.Length);
    }

    #endregion
}
