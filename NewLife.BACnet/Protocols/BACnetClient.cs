using System.IO.BACnet;
using NewLife.IoT.ThingModels;
using NewLife.Reflection;
using NewLife.Threading;

namespace NewLife.BACnet.Protocols
{
    public class BACnetClient
    {
        #region 属性
        private readonly List<BacNode> _nodes = new();
        private BacnetClient _client;
        #endregion

        #region 方法
        public void Open()
        {
            var client = new BacnetClient(new BacnetIpUdpProtocolTransport(47808, false));
            client.OnIam += OnIam;

            client.Start();
            client.WhoIs();

            _client = client;

            _timer = new TimerX(DoScan, null, 0, 10_000) { Async = true };
        }

        public BacNode GetNode(String address) => _nodes.FirstOrDefault(e => e.Address + "" == address);
        #endregion

        #region 设备扫描管理
        private TimerX _timer;

        private void DoScan(Object state)
        {
            foreach (var node in _nodes)
            {
                if (node.Address != null)
                {
                    var oid = new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, node.DeviceId);
                    var rs = _client.ReadPropertyRequest(node.Address, oid, BacnetPropertyIds.PROP_OBJECT_LIST, 0);
                    if (rs.Value != null)
                    {
                        var count = rs.ToInt() + 1;

                        var bobj = new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, node.DeviceId);

                        node.Properties.Clear();
                        var prs = new List<BacnetPropertyReference>();
                        for (var i = 0; i < count; i++)
                        {
                            var property = new BacnetPropertyReference((UInt32)BacnetPropertyIds.PROP_OBJECT_LIST, (UInt32)i);
                            prs.Add(property);
                        }
                        for (var i = 0; i < count;)
                        {
                            var batch = prs.Skip(i).Take(16).ToList();
                            if (batch.Count == 0) break;

                            var results = _client.ReadPropertyMultipleRequest(node.Address, bobj, batch);
                            if (results != null)
                            {
                                var ps = BacProperty.Create(results);
                                node.Properties.AddRange(ps);
                            }

                            i += batch.Count;
                        }
                    }
                }
            }
        }
        #endregion

        public Dictionary<String, Object> Read(BacNode node, IPoint[] points)
        {
            if (node.Address == null || node.Properties == null) return null;

            var dic = new Dictionary<String, Object>();

            var rList = new List<BacnetPropertyReference> {
                new BacnetPropertyReference((UInt32)BacnetPropertyIds.PROP_DESCRIPTION, UInt32.MaxValue),
                new BacnetPropertyReference((UInt32)BacnetPropertyIds.PROP_REQUIRED, UInt32.MaxValue),
                new BacnetPropertyReference((UInt32)BacnetPropertyIds.PROP_OBJECT_NAME, UInt32.MaxValue),
                new BacnetPropertyReference((UInt32)BacnetPropertyIds.PROP_PRESENT_VALUE, UInt32.MaxValue)
            };

            var results = new List<BacnetReadAccessResult>();
            for (var i = 0; i < node.Properties.Count; i++)
            {
                var batch = node.Properties.Skip(i).Take(16).ToList();
                var properties = batch.Select(e => new BacnetReadAccessSpecification(e.ObjectId, rList)).ToList();
                results.AddRange(_client.ReadPropertyMultipleRequest(node.Address, properties));
            }

            foreach (var item in results)
            {
                var oid = item.objectIdentifier;
                var pi = node.Properties.FirstOrDefault(t => t.ObjectId.Instance == oid.Instance && t.ObjectId.Type == oid.Type);
                if (pi != null)
                {
                    pi.Fill(item);

                    var key = $"{pi.ObjectId.Instance}_{(Int32)pi.ObjectId.Type}";
                    var point = points.FirstOrDefault(e => e.Address == key);
                    if (point != null) dic[point.Name] = pi.Value;
                }
            }

            return dic;
        }

        public virtual Object Write(String addr, IPoint point, Object value)
        {
            var bacnet = _nodes.FirstOrDefault(e => e.Address + "" == addr);

            var ss = point.Address.Split('_');
            var oid = ss[0].ToInt();
            var type = (BacnetObjectTypes)ss[1].ToInt();

            BacProperty pi = null;
            if (ss.Length == 1)
                pi = bacnet?.Properties.FirstOrDefault(t => t.Name == point.Address);
            else if (ss.Length == 2)
                pi = bacnet?.Properties.FirstOrDefault(t => t.ObjectId.Instance == oid && t.ObjectId.Type == type);

            var values = new[] { new BacnetValue(value.ChangeType(pi.Type)) };
            _client.WritePropertyRequest(bacnet.Address, pi.ObjectId, BacnetPropertyIds.PROP_PRESENT_VALUE, values);

            return null;
        }

        private void OnIam(BacnetClient sender, BacnetAddress addr, UInt32 deviceId, UInt32 maxAPDU, BacnetSegmentations segmentation, UInt16 vendorId)
        {
            lock (_nodes)
            {
                foreach (var bn in _nodes)
                    if (bn.GetAdd(deviceId) != null) return;

                _nodes.Add(new BacNode(addr, deviceId));
            }

        }
    }
}