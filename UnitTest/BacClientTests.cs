using System;
using System.Collections.Generic;
using System.IO.BACnet;
using System.Threading;
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
    static readonly BacClient _client;
    static BacClientTests()
    {
        _client = new BacClient
        {
            DeviceId = 666,

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

        //var nodes = _client.Nodes;
        //Assert.True(nodes.Count > 0);
    }

    [Fact]
    [TestOrder(20)]
    public void GetNode()
    {
        //_client.Open();

        Thread.Sleep(500);

        var nodes = _client.Nodes;
        Assert.True(nodes.Count > 0);

        var node = _client.GetNode(666);
        Assert.NotNull(node);

        var addr = node.Address + "";
        Assert.NotEmpty(addr);

        node = _client.GetNode(addr);
        Assert.NotNull(node);
    }

    [Fact]
    [TestOrder(30)]
    public void ReadProperty()
    {
        _client.Open();
        Thread.Sleep(500);

        var node = _client.GetNode(666);

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

        for (var i = 0; i < 5; i++)
        {
            {
                var rr = ObjectPair.TryParse("0_0", out var oid);
                Assert.True(rr);

                var rs = _client.ReadProperty(node.Address, "0_0");
                Assert.NotNull(rs);
                XTrace.WriteLine("{0}: {1}", ObjectPair.ToObjectId(oid), rs);
            }
            {
                var rr = ObjectPair.TryParse("0_2", out var oid);
                Assert.True(rr);

                var rs = _client.ReadProperty(node.Address, "0_2");
                Assert.NotNull(rs);
                XTrace.WriteLine("{0}: {1}", ObjectPair.ToObjectId(oid), rs);
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

        var node = _client.GetNode(666);

        var rr = ObjectPair.TryParse("0_0", out var oid1);
        rr |= ObjectPair.TryParse("0_2", out var oid2);

        for (var i = 0; i < 5; i++)
        {
            var rs = _client.ReadProperties(node.Address, new[] { oid1, oid2 });
            Assert.NotNull(rs);
            Assert.Equal(2, rs.Count);
            foreach (var item in rs)
            {
                XTrace.WriteLine("{0}: {1}", ObjectPair.ToObjectId(item.Key), item.Value);
                Assert.True(item.Value.ToDouble() > 0);
            }

            Thread.Sleep(100);
        }
    }

    [Fact]
    [TestOrder(50)]
    public void GetProperties()
    {
        var node = _client.GetNode(666);

        _client.GetProperties(node, true);

        Assert.NotEmpty(node.Ids);
        Assert.NotEmpty(node.Properties);
    }

    [Fact]
    [TestOrder(60)]
    public void WriteProperty()
    {
        _client.Open();
        Thread.Sleep(500);

        var node = _client.GetNode(666);

        var v = (UInt32)Rand.Next(1000, 10000);
        {
            var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 0);
            Assert.Throws<Exception>(() => _client.WriteProperty(node.Address, oid, v));
        }
        {
            var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, 0);
            var rr = _client.WriteProperty(node.Address, oid, v);
            Assert.True(rr);

            var rs = _client.ReadProperty(node.Address, oid);
            Assert.Equal(v, rs);
        }

        for (var i = 0; i < 5; i++)
        {
            v = (UInt32)Rand.Next(1000, 10000);
            {
                var id = "0_2";
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

        var node = _client.GetNode(666);

        var rr = ObjectPair.TryParse("0_0", out var oid1);
        rr |= ObjectPair.TryParse("0_2", out var oid2);
        Assert.True(rr);

        for (var i = 0; i < 5; i++)
        {
            var data = new Dictionary<BacnetObjectId, Object>
            {
                [oid1] = Rand.Next(1000, 10000) / 10d,
                [oid2] = Rand.Next(1000, 10000) / 10d,
            };

            var rs = _client.WriteProperties(node.Address, data);
            Assert.True(rs);

            Thread.Sleep(100);
        }
    }
}