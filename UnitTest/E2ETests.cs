using System;
using System.Collections.Generic;
using System.IO.BACnet;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using NewLife;
using NewLife.BACnet.Drivers;
using NewLife.BACnet.Protocols;
using NewLife.IoT;
using NewLife.IoT.Drivers;
using NewLife.IoT.ThingModels;
using NewLife.Log;
using NewLife.UnitTest;
using Xunit;

namespace UnitTest;

/// <summary>端到端集成测试：BACnetDriver（驱动层）+ BacServer（服务端）本地回环。
/// 验证完整的"发现 → 读 → 写 → 批量读写"应用场景，模拟真实的 IoT 网关使用方式。</summary>
[Collection("E2E")]
[TestCaseOrderer("NewLife.UnitTest.PriorityOrderer", "NewLife.UnitTest")]
public class E2ETests : IDisposable
{
    private const Int32 TestDeviceId = 9002;
    private readonly Int32 _serverPort;
    private readonly BacServer _server;
    private readonly BACnetDriver _driver;
    private readonly BACnetParameter _parameter;
    private readonly INode _node;

    public E2ETests()
    {
#if DEBUG
        XTrace.Log.Level = LogLevel.Debug;
#endif
        _serverPort = GetFreeUdpPort();

        // 启动 BACnet 服务端（仿真设备）
        _server = new BacServer
        {
            DeviceId = TestDeviceId,
            StorageFile = "TestDeviceDescriptor.xml",
            Port = _serverPort,
            Log = XTrace.Log,
        };
        _server.Open();

        // 配置驱动层（客户端）：通过 TargetAddress 实现单播 WhoIs 定向到服务端
        var clientPort = GetFreeUdpPort();
        _driver = new BACnetDriver { Log = XTrace.Log };
        _parameter = new BACnetParameter
        {
            Port = clientPort,
            DeviceId = TestDeviceId,
            TargetAddress = $"127.0.0.1:{_serverPort}",
        };

        // 通过标准 driver.Open() 流程打开驱动（内部会创建 BacClient 并连接）
        _node = ((IDriver)_driver).Open(new ThingDevice(), _parameter);
        Assert.NotNull(_node);

        // 触发设备发现，等待服务端响应
        var found = _driver.Client?.Scan();
        Assert.NotNull(found);
    }

    public void Dispose()
    {
        if (_node != null) ((IDriver)_driver).Close(_node);
        _server?.TryDispose();
    }

    #region 辅助
    private static Int32 GetFreeUdpPort()
    {
        using var sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        sock.Bind(new IPEndPoint(IPAddress.Any, 0));
        return ((IPEndPoint)sock.LocalEndPoint).Port;
    }

    private BacNode GetServerNode() => _driver.Client?.GetNode(TestDeviceId);
    #endregion

    #region 驱动层读取
    [Fact]
    [TestOrder(10)]
    [System.ComponentModel.DisplayName("E2E：驱动层读取 AI:0 现在值")]
    public void Driver_Read_AnalogInput()
    {
        var point = new PointModel { Name = "AI_0", Address = "0_0" };
        var rs = ((IDriver)_driver).Read(_node, new[] { (IPoint)point });

        Assert.NotNull(rs);
        Assert.True(rs.GetValue("AI_0") != null);
        XTrace.WriteLine("AI:0 = {0}", rs.GetValue("AI_0"));

        // XML 中初始值为 42
        Assert.Equal(42.0, Convert.ToDouble(rs.GetValue("AI_0")), precision: 3);
    }

    [Fact]
    [TestOrder(11)]
    [System.ComponentModel.DisplayName("E2E：驱动层读取 AV:0 现在值")]
    public void Driver_Read_AnalogValue()
    {
        var point = new PointModel { Name = "AV_0", Address = "0_2" };
        var rs = ((IDriver)_driver).Read(_node, new[] { (IPoint)point });

        Assert.NotNull(rs);
        Assert.True(rs.GetValue("AV_0") != null);
        XTrace.WriteLine("AV:0 = {0}", rs.GetValue("AV_0"));
    }
    #endregion

    #region 驱动层写入
    [Fact]
    [TestOrder(20)]
    [System.ComponentModel.DisplayName("E2E：驱动层写入 AV:0 并读回验证")]
    public void Driver_Write_AnalogValue_Roundtrip()
    {
        var point = new PointModel { Name = "AV_0", Address = "0_2" };
        const Single expectedValue = 99.9f;

        // 写入
        var writeResult = ((IDriver)_driver).Write(_node, point, expectedValue);
        XTrace.WriteLine("Write result: {0}", writeResult);

        // 读回验证
        var rs = ((IDriver)_driver).Read(_node, new[] { (IPoint)point });
        Assert.NotNull(rs);
        Assert.True(rs.GetValue("AV_0") != null);
        Assert.Equal(expectedValue, Convert.ToSingle(rs.GetValue("AV_0")), precision: 3);
    }
    #endregion

    #region 批量读取
    [Fact]
    [TestOrder(30)]
    [System.ComponentModel.DisplayName("E2E：批量读取 AI:0 和 AV:0")]
    public void Driver_BatchRead()
    {
        var points = new IPoint[]
        {
            new PointModel { Name = "AI_0", Address = "0_0" },
            new PointModel { Name = "AV_0", Address = "0_2" },
        };

        var rs = ((IDriver)_driver).Read(_node, points);

        Assert.NotNull(rs);
        Assert.True(rs.GetValue("AI_0") != null);
        Assert.True(rs.GetValue("AV_0") != null);
        XTrace.WriteLine("AI:0={0}, AV:0={1}", rs.GetValue("AI_0"), rs.GetValue("AV_0"));
    }
    #endregion

    #region 重复读取稳定性
    [Fact]
    [TestOrder(40)]
    [System.ComponentModel.DisplayName("E2E：连续 10 次读取 AV:0，验证稳定性")]
    public void Driver_Read_Stability()
    {
        var point = new PointModel { Name = "AV_0", Address = "0_2" };
        var successCount = 0;

        for (var i = 0; i < 10; i++)
        {
            var rs = ((IDriver)_driver).Read(_node, new[] { (IPoint)point });
            if (rs != null && rs.GetValue("AV_0") != null)
                successCount++;
            Thread.Sleep(50);
        }

        Assert.True(successCount >= 8, $"连续读取成功率不足：{successCount}/10");
    }
    #endregion

    #region 写后读验证
    [Fact]
    [TestOrder(50)]
    [System.ComponentModel.DisplayName("E2E：多次写入 AV:0 并每次读回验证")]
    public void Driver_WriteAndRead_Multiple()
    {
        var point = new PointModel { Name = "AV_0", Address = "0_2" };
        Single[] testValues = [1.1f, 22.22f, 333.333f, 0.0f, -10.0f];

        foreach (var val in testValues)
        {
            ((IDriver)_driver).Write(_node, point, val);
            Thread.Sleep(100);

            var rs = ((IDriver)_driver).Read(_node, new[] { (IPoint)point });
            Assert.NotNull(rs);
            Assert.True(rs.GetValue("AV_0") != null);
            Assert.Equal(val, Convert.ToSingle(rs.GetValue("AV_0")), precision: 2);
            XTrace.WriteLine("Write {0} → ReadBack {1}", val, rs.GetValue("AV_0"));
        }
    }
    #endregion

    #region 节点发现
    [Fact]
    [TestOrder(5)]
    [System.ComponentModel.DisplayName("E2E：客户端节点列表不为空")]
    public void Discovery_NodeListNotEmpty()
    {
        var nodes = _driver.Client?.Nodes;
        Assert.NotNull(nodes);
        Assert.NotEmpty(nodes);

        var serverNode = GetServerNode();
        Assert.NotNull(serverNode);
        Assert.Equal((UInt32)TestDeviceId, serverNode.DeviceId);
        XTrace.WriteLine("发现服务端节点: DeviceId={0}, Address={1}", serverNode.DeviceId, serverNode.Address);
    }
    #endregion
}
