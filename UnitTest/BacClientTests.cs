﻿using System;
using System.Collections.Generic;
using System.IO.BACnet;
using System.Threading;
using NewLife;
using NewLife.BACnet.Protocols;
using NewLife.Log;
using NewLife.Security;
using NewLife.UnitTest;
using Xunit;

[assembly: CollectionBehavior(CollectionBehavior.CollectionPerClass, DisableTestParallelization = true)]

namespace UnitTest;

[Collection("Client")]
[TestCaseOrderer("NewLife.UnitTest.PriorityOrderer", "NewLife.UnitTest")]
//[TestCaseOrderer("NewLife.UnitTest.DefaultOrderer", "NewLife.UnitTest")]
public class BacClientTests
{
    static Int32 _DeviceId = 0;
    static readonly BacClient _client;
    static BacClientTests()
    {
#if DEBUG
        XTrace.Log.Level = LogLevel.Debug;
#endif
        _client = new BacClient
        {
            DeviceId = _DeviceId,

            Log = XTrace.Log
        };
    }

    [Fact]
    [TestOrder(10)]
    public void Open()
    {
        _client.Open();

        var udp = _client.Transport as BacnetIpUdpProtocolTransport;
        Assert.NotNull(udp);

        Assert.Equal(0xBAC0, _client.Port);

        //Thread.Sleep(500);

        var nodes = _client.Nodes;
        Assert.True(nodes.Count > 0);
    }

    [Fact]
    [TestOrder(15)]
    public void Scan()
    {
        _client.Open();

        var node = _client.Scan();
        Assert.NotNull(node);
        XTrace.WriteLine("node: {0}", node);
    }

    [Fact]
    [TestOrder(20)]
    public void GetNode()
    {
        _client.Open();
        //Thread.Sleep(500);

        var nodes = _client.Nodes;
        Assert.True(nodes.Count > 0);

        if (_DeviceId == 0) _DeviceId = (Int32)nodes[0].DeviceId;

        var node = _client.GetNode(_DeviceId);
        Assert.NotNull(node);

        var addr = node.Address + "";
        Assert.NotEmpty(addr);
        XTrace.WriteLine("addr: {0}", addr);

        node = _client.GetNode(addr);
        Assert.NotNull(node);
        XTrace.WriteLine("node: {0}", node);
    }

    [Fact]
    [TestOrder(30)]
    public void ReadProperty()
    {
        _client.Open();
        Thread.Sleep(500);

        var node = _client.GetNode(_DeviceId);

        XTrace.WriteLine("按类型和实例读取属性数据");
        {
            var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 0);
            var rs = _client.ReadProperty(node.Address, oid);
            Assert.NotNull(rs);
            XTrace.WriteLine("{0}: {1}", oid, rs);
        }
        {
            var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, 0);
            var rs = _client.ReadProperty(node.Address, oid);
            Assert.NotNull(rs);
            XTrace.WriteLine("{0}: {1}", oid, rs);
        }

        XTrace.WriteLine("按名称读取属性数据");
        for (var i = 0; i < 5; i++)
        {
            {
                var rr = ObjectPair.TryParse("0_0", out var oid);
                Assert.True(rr);

                var rs = _client.ReadProperty(node.Address, "0_0");
                Assert.NotNull(rs);
                XTrace.WriteLine("{0}: {1}", oid.GetKey(), rs);
            }
            {
                var rr = ObjectPair.TryParse("0_2", out var oid);
                Assert.True(rr);

                var rs = _client.ReadProperty(node.Address, "0_2");
                Assert.NotNull(rs);
                XTrace.WriteLine("{0}: {1}", oid.GetKey(), rs);
            }

            Thread.Sleep(100);
        }
    }

    [Fact]
    [TestOrder(40)]
    public void ReadProperties()
    {
        _client.Open();
        Thread.Sleep(500);

        var node = _client.GetNode(_DeviceId);

        var oid1 = node.Properties[2].ObjectId;
        var oid2 = node.Properties[3].ObjectId;

        XTrace.WriteLine("按类型实例批量读取属性数据");
        for (var i = 0; i < 5; i++)
        {
            var rs = _client.ReadProperties(node.Address, new[] { oid1, oid2 });
            Assert.NotNull(rs);
            Assert.Equal(2, rs.Count);
            foreach (var item in rs)
            {
                XTrace.WriteLine("{0}: {1}", item.Key.GetKey(), item.Value);
                Assert.True(item.Value.ToDouble() > 0);
            }

            Thread.Sleep(100);
        }

        XTrace.WriteLine("按名字批量读取属性数据");
        for (var i = 0; i < 5; i++)
        {
            var rs = _client.ReadProperties(node.Address, new[] { oid1.GetKey(), oid2.GetKey() });
            Assert.NotNull(rs);
            Assert.Equal(2, rs.Count);
            foreach (var item in rs)
            {
                XTrace.WriteLine("{0}: {1}", item.Key, item.Value);
                Assert.True(item.Value.ToDouble() > 0);
            }

            Thread.Sleep(100);
        }
    }

    [Fact]
    [TestOrder(50)]
    public void GetProperties()
    {
        _client.Open();
        Thread.Sleep(500);

        var node = _client.GetNode(_DeviceId);

        XTrace.WriteLine("GetProperties: {0}", node);
        _client.GetProperties(node, false);

        Assert.NotEmpty(node.Properties);

        foreach (var item in node.Properties)
        {
            XTrace.WriteLine("{0}: {1}", item, item.Name);
        }
    }

    [Fact]
    [TestOrder(52)]
    public void GetProperties2()
    {
        _client.Open();
        Thread.Sleep(500);

        var node = _client.GetNode(_DeviceId);

        XTrace.WriteLine("GetProperties2: {0}", node);
        _client.GetProperties(node, true);

        Assert.NotEmpty(node.Properties);

        foreach (var item in node.Properties)
        {
            XTrace.WriteLine("{0}: \tvalue={1} \ttype={2} \tname={3} \tdescription={4}", item.ObjectId.GetKey(), item.Value, item.Type?.FullName?.TrimStart("System."), item.Name, item.Description);
        }
    }

    [Fact]
    [TestOrder(60)]
    public void WriteProperty()
    {
        //_client.Open();
        //Thread.Sleep(500);

        var node = _client.GetNode(_DeviceId);

        XTrace.WriteLine("WriteProperty: {0}", node);
        var v = Rand.Next(1000, 10000) / 10f;
        {
            var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 0);
            Assert.Throws<Exception>(() => _client.WriteProperty(node.Address, oid, v));
        }
        {
            var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, 0);
            var rr = _client.WriteProperty(node.Address, oid, v);
            Assert.True(rr);

            var rs = _client.ReadProperty(node.Address, oid);
            Assert.Equal(v.ToDouble(), rs.ToDouble());
        }

        for (var i = 0; i < 5; i++)
        {
            v = Rand.Next(1000, 10000) / 10f;
            {
                var id = "0_2";
                XTrace.WriteLine("{0}={1}", id, v);
                var rr = _client.WriteProperty(node.Address, id, v);
                Assert.True(rr);

                var rs = _client.ReadProperty(node.Address, id);
                Assert.Equal(v, rs);
            }

            Thread.Sleep(100);
        }
    }

    [Fact]
    [TestOrder(70)]
    public void WriteProperties()
    {
        _client.Open();
        Thread.Sleep(500);

        var node = _client.GetNode(_DeviceId);

        var rr = ObjectPair.TryParse("1_2", out var oid1);
        rr |= ObjectPair.TryParse("0_2", out var oid2);
        Assert.True(rr);

        XTrace.WriteLine("WriteProperties: {0}", node);
        for (var i = 0; i < 5; i++)
        {
            var data = new Dictionary<BacnetObjectId, Object>
            {
                [oid1] = Rand.Next(1000, 10000) / 10f,
                [oid2] = Rand.Next(1000, 10000) / 10f,
            };

            XTrace.WriteLine("{0}={1}, {2}={3}", oid1, data[oid1], oid2, data[oid2]);

            var rs = _client.WriteProperties(node.Address, data);
            Assert.True(rs);

            Thread.Sleep(100);
        }
    }
}