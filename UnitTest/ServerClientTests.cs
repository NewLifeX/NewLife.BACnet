using System;
using System.Collections.Generic;
using System.IO.BACnet;
using System.IO.BACnet.Serialize;
using System.IO.BACnet.Storage;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using NewLife;
using NewLife.BACnet.Protocols;
using NewLife.Log;
using NewLife.UnitTest;
using Xunit;

namespace UnitTest;

/// <summary>BacServer + BacClient 本地环回集成测试。
/// 在同一进程内启动服务端和客户端，通过 127.0.0.1 回环通信。
/// 不需要真实 BACnet 设备，可在 CI 中运行。</summary>
[Collection("Loopback")]
[TestCaseOrderer("NewLife.UnitTest.PriorityOrderer", "NewLife.UnitTest")]
public class ServerClientTests : IDisposable
{
    private const Int32 TestDeviceId = 9001;
    private readonly Int32 _serverPort;
    private readonly Int32 _clientPort;
    private readonly BacServer _server;
    private readonly BacClient _client;

    public ServerClientTests()
    {
#if DEBUG
        XTrace.Log.Level = LogLevel.Debug;
#endif
        // 随机分配两个空闲端口，避免与生产 BACnet（47808）冲突
        _serverPort = GetFreeUdpPort();
        _clientPort = GetFreeUdpPort();

        // 启动服务端
        _server = new BacServer
        {
            DeviceId = TestDeviceId,
            StorageFile = "TestDeviceDescriptor.xml",
            Port = _serverPort,
            Log = XTrace.Log,
        };
        _server.Open();

        // 启动客户端，使用单播 WhoIs 定向到服务端，避免广播到真实 BACnet 网络
        _client = new BacClient
        {
            DeviceId = TestDeviceId,
            Port = _clientPort,
            WaitingTime = 3_000,
            TargetAddress = $"127.0.0.1:{_serverPort}",
            Log = XTrace.Log,
        };
        _client.Open();

        // 等待客户端发现服务端
        var node = _client.Scan();
        Assert.NotNull(node);
    }

    public void Dispose()
    {
        _client?.TryDispose();
        _server?.TryDispose();
    }

    #region 辅助
    private static Int32 GetFreeUdpPort()
    {
        using var sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        sock.Bind(new IPEndPoint(IPAddress.Any, 0));
        return ((IPEndPoint)sock.LocalEndPoint).Port;
    }

    private BacNode GetServerNode() => _client.GetNode(TestDeviceId);
    #endregion

    #region 设备发现
    [Fact]
    [TestOrder(10)]
    [System.ComponentModel.DisplayName("环回：客户端发现服务端节点")]
    public void Discovery_FindsServer()
    {
        var nodes = _client.Nodes;
        Assert.NotEmpty(nodes);

        var node = GetServerNode();
        Assert.NotNull(node);
        Assert.Equal((UInt32)TestDeviceId, node.DeviceId);
        XTrace.WriteLine("发现节点: {0}", node);
    }
    #endregion

    #region ReadProperty
    [Fact]
    [TestOrder(20)]
    [System.ComponentModel.DisplayName("环回：读取 AI:0 现在值（只读）")]
    public void ReadProperty_AnalogInput()
    {
        var node = GetServerNode();
        var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 0);
        var value = _client.ReadProperty(node.Address, oid);

        Assert.NotNull(value);
        XTrace.WriteLine("AI:0 PresentValue = {0}", value);
        // XML 中初始值为 42
        Assert.Equal(42.0, Convert.ToDouble(value), precision: 3);
    }

    [Fact]
    [TestOrder(21)]
    [System.ComponentModel.DisplayName("环回：按字符串地址读取 AI:0 现在值")]
    public void ReadProperty_ByStringAddress_AnalogInput()
    {
        var node = GetServerNode();
        var value = _client.ReadProperty(node.Address, "0_0"); // AI:0
        Assert.NotNull(value);
        Assert.Equal(42.0, Convert.ToDouble(value), precision: 3);
    }

    [Fact]
    [TestOrder(22)]
    [System.ComponentModel.DisplayName("环回：读取 AV:0 现在值")]
    public void ReadProperty_AnalogValue()
    {
        var node = GetServerNode();
        var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, 0);
        var value = _client.ReadProperty(node.Address, oid);
        Assert.NotNull(value);
        XTrace.WriteLine("AV:0 PresentValue = {0}", value);
    }
    #endregion

    #region WriteProperty
    [Fact]
    [TestOrder(30)]
    [System.ComponentModel.DisplayName("环回：写入 AV:0 现在值并读回验证")]
    public void WriteProperty_AnalogValue_Roundtrip()
    {
        var node = GetServerNode();
        var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, 0);
        const Single expected = 123.456f;

        var ok = _client.WriteProperty(node.Address, oid, expected);
        Assert.True(ok);

        // 读回验证
        var readBack = _client.ReadProperty(node.Address, oid);
        Assert.NotNull(readBack);
        Assert.Equal(expected, Convert.ToSingle(readBack), precision: 3);
    }

    [Fact]
    [TestOrder(31)]
    [System.ComponentModel.DisplayName("环回：按字符串地址写入 AV:0 并读回验证")]
    public void WriteProperty_ByStringAddress_Roundtrip()
    {
        var node = GetServerNode();
        const Single expected = 88.8f;

        var ok = _client.WriteProperty(node.Address, "0_2", expected);
        Assert.True(ok);

        var readBack = _client.ReadProperty(node.Address, "0_2");
        Assert.NotNull(readBack);
        Assert.Equal(expected, Convert.ToSingle(readBack), precision: 3);
    }

    [Fact]
    [TestOrder(32)]
    [System.ComponentModel.DisplayName("环回：写入只读 AI:0 应返回 false")]
    public void WriteProperty_ReadOnly_Fails()
    {
        var node = GetServerNode();
        var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 0);
        // 服务端拒绝写入 AI（只读对象），WriteProperty 捕获异常后返回 false
        var ok = _client.WriteProperty(node.Address, oid, 99.0f);
        Assert.False(ok);
    }
    #endregion

    #region ReadProperties（批量）
    [Fact]
    [TestOrder(40)]
    [System.ComponentModel.DisplayName("环回：批量读取 AI:0 和 AV:0 现在值")]
    public void ReadProperties_BatchRead()
    {
        var node = GetServerNode();
        var aiOid = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 0);
        var avOid = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, 0);

        var results = _client.ReadProperties(node.Address, new[] { aiOid, avOid });

        Assert.NotNull(results);
        Assert.Equal(2, results.Count);

        Assert.True(results.ContainsKey(aiOid));
        Assert.True(results.ContainsKey(avOid));

        XTrace.WriteLine("AI:0={0}, AV:0={1}", results[aiOid], results[avOid]);
    }

    [Fact]
    [TestOrder(41)]
    [System.ComponentModel.DisplayName("环回：按字符串批量读取两个对象")]
    public void ReadProperties_ByStringAddress_BatchRead()
    {
        var node = GetServerNode();
        var results = _client.ReadProperties(node.Address, new[] { "0_0", "0_2" });

        Assert.NotNull(results);
        Assert.Equal(2, results.Count);
        Assert.True(results.ContainsKey("0_0"));
        Assert.True(results.ContainsKey("0_2"));
    }
    #endregion

    #region WritePropertyMultiple (SRV-6)

    [Fact]
    [TestOrder(45)]
    [System.ComponentModel.DisplayName("环回：WritePropertyMultiple 批量写入 AV:0 优先级+值")]
    public void WritePropertyMultiple_AnalogValue()
    {
        var node = GetServerNode();
        var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, 0);
        var values = new List<BacnetValue>
        {
            new(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, 200.0f),
        };
        var propValues = new List<BacnetPropertyValue>
        {
            new()
            {
                property = new BacnetPropertyReference((UInt32)BacnetPropertyIds.PROP_PRESENT_VALUE, ASN1.BACNET_ARRAY_ALL),
                value = values,
                priority = 8,
            }
        };

        var ok = _client.Client.WritePropertyMultipleRequest(node.Address, oid, propValues);
        Assert.True(ok);

        // 读回验证
        var readBack = _client.ReadProperty(node.Address, oid);
        Assert.NotNull(readBack);
        Assert.Equal(200.0f, Convert.ToSingle(readBack), precision: 3);
    }

    #endregion

    #region WriteObjectMultiple (SRV-7)

    [Fact]
    [TestOrder(46)]
    [System.ComponentModel.DisplayName("环回：WriteObjectMultiple 多对象批量写入")]
    public void WriteObjectMultiple_MultipleObjects()
    {
        var node = GetServerNode();

        // 构造多对象写入参数
        var avOid = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, 0);
        var avValues = new List<BacnetValue>
        {
            new(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, 300.0f),
        };

        var readResults = new List<BacnetReadAccessResult>
        {
            new()
            {
                objectIdentifier = avOid,
                values = new List<BacnetPropertyValue>
                {
                    new()
                    {
                        property = new BacnetPropertyReference((UInt32)BacnetPropertyIds.PROP_PRESENT_VALUE, ASN1.BACNET_ARRAY_ALL),
                        value = avValues,
                        priority = 8,
                    }
                }
            }
        };

        var ok = _client.Client.WritePropertyMultipleRequest(node.Address, readResults);
        Assert.True(ok);

        // 读回验证
        var readBack = _client.ReadProperty(node.Address, avOid);
        Assert.NotNull(readBack);
        Assert.Equal(300.0f, Convert.ToSingle(readBack), precision: 3);
    }

    #endregion

    #region 属性读写事件钩子 (SRV-9)

    [Fact]
    [TestOrder(47)]
    [System.ComponentModel.DisplayName("环回：ReadOverride 事件钩子拦截读取")]
    public void ReadOverride_Hook()
    {
        var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, 6);
        // 注册 ReadOverride 事件：拦截 AV:6 返回固定值
        Boolean handled = false;
        _server.Storage.ReadOverride += (BacnetObjectId objectId, BacnetPropertyIds propertyId,
            UInt32 arrayIndex, out IList<BacnetValue> value, out DeviceStorage.ErrorCodes status, out Boolean h) =>
        {
            if (objectId.type == oid.type && objectId.instance == oid.instance)
            {
                value = new List<BacnetValue>
                {
                    new(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, 999.0f)
                };
                status = DeviceStorage.ErrorCodes.Good;
                h = true;
                handled = true;
                return;
            }
            value = null;
            status = DeviceStorage.ErrorCodes.Good;
            h = false;
        };

        var node = GetServerNode();
        var readBack = _client.ReadProperty(node.Address, oid);

        // 验证读回的是钩子返回的值而不是存储值
        Assert.True(handled, "ReadOverride 应被触发");
        Assert.NotNull(readBack);
        Assert.Equal(999.0f, Convert.ToSingle(readBack), precision: 3);
    }

    [Fact]
    [TestOrder(48)]
    [System.ComponentModel.DisplayName("环回：WriteOverride 事件钩子拦截写入")]
    public void WriteOverride_Hook()
    {
        var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, 0);
        Boolean handled = false;
        _server.Storage.WriteOverride += (BacnetObjectId objectId, BacnetPropertyIds propertyId,
            UInt32 arrayIndex, IList<BacnetValue> value, out DeviceStorage.ErrorCodes status, out Boolean h) =>
        {
            if (objectId.type == oid.type && objectId.instance == oid.instance &&
                propertyId == BacnetPropertyIds.PROP_PRESENT_VALUE)
            {
                // 拒绝写入
                status = DeviceStorage.ErrorCodes.WriteAccessDenied;
                h = true;
                handled = true;
                return;
            }
            status = DeviceStorage.ErrorCodes.Good;
            h = false;
        };

        var node = GetServerNode();
        // 使用优先级 8 确保服务器端 WriteCommandableProperty 正确处理
        _client.Client.WritePriority = 8;
        var ok = _client.WriteProperty(node.Address, oid, 777.0f);
        _client.Client.WritePriority = 0;

        Assert.True(handled, "WriteOverride 应被触发");
    }

    #endregion

    #region 属性变化事件 (SRV-10)

    [Fact]
    [TestOrder(49)]
    [System.ComponentModel.DisplayName("环回：ChangeOfValue 事件在属性变化时触发")]
    public void ChangeOfValue_Event()
    {
        var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, 0);
        Boolean eventFired = false;
        BacnetObjectId changedObj = default;

        _server.Storage.ChangeOfValue += (DeviceStorage sender, BacnetObjectId objectId,
            BacnetPropertyIds propertyId, UInt32 arrayIndex, IList<BacnetValue> value) =>
        {
            eventFired = true;
            changedObj = objectId;
        };

        // 触发写入，应触发 ChangeOfValue 事件
        var node = GetServerNode();
        _client.WriteProperty(node.Address, oid, 456.0f);

        Assert.True(eventFired, "ChangeOfValue 事件应在属性写入后被触发");
        Assert.Equal(oid.type, changedObj.type);
        Assert.Equal(oid.instance, changedObj.instance);
    }

    #endregion

    #region COV 订阅
    [Fact(Skip = "BacServer 示例代码不支持 COV 订阅（SERVICE_CONFIRMED_SUBSCRIBE_COV 未实现），已知限制")]
    [TestOrder(50)]
    [System.ComponentModel.DisplayName("环回：订阅 AV:0 COV，服务端改值后客户端收到通知")]
    public void COV_Subscribe_ReceiveNotification()
    {
        var node = GetServerNode();
        var avOid = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, 0);
        var received = new ManualResetEventSlim(false);
        ICollection<BacnetPropertyValue> covValues = null;

        // 注册 COV 事件处理
        _client.Client.OnCOVNotification += (sender, adr, invokeId2, subscriberProcessId, initiatingDevice,
            monitoredObjectIdentifier, timeRemaining, needConfirm, values, maxSegments) =>
        {
            if (monitoredObjectIdentifier == avOid)
            {
                covValues = values;
                received.Set();
            }
        };

        // 订阅
        var subscribeResult = _client.Client.SubscribeCOVRequest(node.Address, avOid, 1, false, false, 30);
        Assert.True(subscribeResult);

        // 服务端直接改值，触发 COV
        _server.Storage.WriteProperty(avOid, BacnetPropertyIds.PROP_PRESENT_VALUE,
            new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, 55.55f));

        // 等待通知，最多 3 秒
        var notified = received.Wait(TimeSpan.FromSeconds(3));

        // 取消订阅（清理）
        _client.Client.SubscribeCOVRequest(node.Address, avOid, 1, true, false, 0);

        Assert.True(notified, "在超时时间内未收到 COV 通知");
        Assert.NotNull(covValues);
    }
    #endregion
}
