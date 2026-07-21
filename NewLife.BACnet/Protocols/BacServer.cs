using System.IO.BACnet;
using System.IO.BACnet.Storage;
using NewLife.Log;

namespace NewLife.BACnet.Protocols;

/// <summary>BACnet服务端。默认UDP协议</summary>
public class BacServer : DisposeBase, ITracerFeature, ILogFeature
{
    #region 属性
    /// <summary>共享端口。节点在该端口上监听广播数据，多进程共享，默认0xBAC0，即47808</summary>
    public Int32 Port { get; set; } = 0xBAC0;

    /// <summary>传输层</summary>
    public IBacnetTransport Transport { get; set; }

    /// <summary>设备编号</summary>
    public Int32 DeviceId { get; set; }

    /// <summary>存储</summary>
    public DeviceStorage Storage { get; set; }

    /// <summary>存储文件</summary>
    public String StorageFile { get; set; }

    private readonly List<BacNode> _nodes = new();
    private BacnetClient _client;
    #endregion

    #region 构造
    /// <summary>释放资源</summary>
    /// <param name="disposing"></param>
    protected override void Dispose(Boolean disposing)
    {
        base.Dispose(disposing);

        _client.TryDispose();
    }
    #endregion

    #region 方法
    /// <summary>打开连接</summary>
    public void Open()
    {
        using var span = Tracer?.NewSpan("bac:Open", new { Port, DeviceId, StorageFile });
        try
        {
            if (!StorageFile.IsNullOrEmpty()) Storage = DeviceStorage.Load(StorageFile);

            var store = Storage;
            if (store != null)
            {
                store.DeviceId = (UInt32)DeviceId;
                foreach (var item in store.Objects)
                {
                    if (item.Type == BacnetObjectTypes.OBJECT_DEVICE)
                        item.Instance = (UInt32)DeviceId;
                }
            }

            Transport ??= new BacnetIpUdpProtocolTransport(Port) { Tracer = Tracer };

            var client = new BacnetClient(Transport) { Tracer = Tracer };
            client.OnWhoIs += OnWhoIs;
            client.OnIam += OnIam;
            client.OnWhoHas += OnWhoHas;
            client.OnTimeSynchronize += OnTimeSynchronize;
            client.OnDeviceCommunicationControl += OnDeviceCommunicationControl;
            client.OnReadPropertyRequest += OnReadPropertyRequest;
            client.OnReadPropertyMultipleRequest += OnReadPropertyMultipleRequest;
            client.OnWritePropertyRequest += OnWritePropertyRequest;
            client.OnWritePropertyMultipleRequest += OnWritePropertyMultipleRequest;
            client.OnWriteObjectMultipleRequest += OnWriteObjectMultipleRequest;

            // 监听端口
            client.Start();

            if (Transport is BacnetIpUdpProtocolTransport udp)
                WriteLog("本地：{0}", udp.LocalEndPoint);

            // 广播“我是谁”
            client.Iam((UInt32)DeviceId, new BacnetSegmentations());

            _client = client;
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);
            throw;
        }
    }

    private void OnWhoIs(BacnetClient sender, BacnetAddress addr, Int32 lowLimit, Int32 highLimit)
    {
        if (lowLimit != -1 && DeviceId < lowLimit) return;
        if (highLimit != -1 && DeviceId > highLimit) return;

        using var span = Tracer?.NewSpan("bac:OnWhoIs", new { addr, lowLimit, highLimit });

        sender.Iam((UInt32)DeviceId, new BacnetSegmentations(), addr);
    }

    private void OnIam(BacnetClient sender, BacnetAddress addr, UInt32 deviceId, UInt32 maxAPDU, BacnetSegmentations segmentation, UInt16 vendorId)
    {
        using var span = Tracer?.NewSpan("bac:OnIam", new { addr, deviceId, vendorId });
        XTrace.WriteLine("OnIam [{0}]: {1}", addr, deviceId);

        lock (_nodes)
        {
            foreach (var bn in _nodes)
            {
                if (bn.GetAdd(deviceId) != null) return;
            }

            _nodes.Add(new BacNode(addr, deviceId));
        }

    }
    #endregion

    #region 服务事件
    private static String GetObjectName(System.IO.BACnet.Storage.Object obj)
    {
        if (obj.Properties == null) return null;

        foreach (var p in obj.Properties)
        {
            if (p.Id == BacnetPropertyIds.PROP_OBJECT_NAME && p.Value != null && p.Value.Length > 0)
                return p.Value[0];
        }

        return null;
    }

    private void OnWhoHas(BacnetClient sender, BacnetAddress addr, Int32 lowLimit, Int32 highLimit, BacnetObjectId? objId, String objName)
    {
        using var span = Tracer?.NewSpan("bac:OnWhoHas", new { addr, lowLimit, highLimit, objId, objName });
        WriteLog("WhoHas[{0}]: objId={1} objName={2}", addr, objId, objName);

        // 如果服务器有该对象，回复 I-Have
        var storage = Storage;
        if (storage == null) return;

        lock (storage)
        {
            foreach (var obj in storage.Objects)
            {
                if (objId != null)
                {
                    if (obj.Type == objId.Value.type && obj.Instance == objId.Value.instance)
                    {
                        var deviceObjId = new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, (UInt32)DeviceId);
                        var foundObjId = new BacnetObjectId(obj.Type, obj.Instance);
                        sender.IHave(deviceObjId, foundObjId, GetObjectName(obj));
                        return;
                    }
                }
                else if (!objName.IsNullOrEmpty())
                {
                    var name = GetObjectName(obj);
                    if (name == objName)
                    {
                        var deviceObjId = new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, (UInt32)DeviceId);
                        var foundObjId = new BacnetObjectId(obj.Type, obj.Instance);
                        sender.IHave(deviceObjId, foundObjId, name);
                        return;
                    }
                }
            }
        }
    }

    private void OnTimeSynchronize(BacnetClient sender, BacnetAddress addr, DateTime dateTime, Boolean utc)
    {
        using var span = Tracer?.NewSpan("bac:OnTimeSync", new { addr, dateTime, utc });
        WriteLog("TimeSync[{0}]: {1} UTC={2}", addr, dateTime, utc);

        // 记录收到的时间同步请求，实际系统时间设置由上层应用决定
        // 此处仅做日志记录和事件转发
    }

    private void OnDeviceCommunicationControl(BacnetClient sender, BacnetAddress addr, Byte invokeId, UInt32 timeDuration, UInt32 enableDisable, String password, BacnetMaxSegments maxSegments)
    {
        using var span = Tracer?.NewSpan("bac:OnDCC", new { addr, timeDuration, enableDisable });
        WriteLog("DCC[{0}]: timeDuration={1} enableDisable={2}", addr, timeDuration, enableDisable);

        // 根据 enableDisable 值处理：0=disable, 1=enable, 2=disableInitiation
        // 此处仅记录日志并回复 SimpleAck，实际业务逻辑由上层应用处理
        sender.SimpleAckResponse(addr, BacnetConfirmedServices.SERVICE_CONFIRMED_DEVICE_COMMUNICATION_CONTROL, invokeId);
    }
    #endregion

    #region 读写方法
    private void OnReadPropertyRequest(BacnetClient sender, BacnetAddress addr, Byte invokeId, BacnetObjectId objectId, BacnetPropertyReference property, BacnetMaxSegments maxSegments)
    {
        using var span = Tracer?.NewSpan("bac:OnReadProperty", new { addr, objectId, property });
        WriteLog("ReadProperty[{0}]: {1} {2}", addr, objectId, property);

        var storage = Storage;
        lock (storage)
        {
            try
            {
                var code = storage.ReadProperty(objectId, (BacnetPropertyIds)property.propertyIdentifier, property.propertyArrayIndex, out var value);
                if (code == DeviceStorage.ErrorCodes.Good)
                    sender.ReadPropertyResponse(addr, invokeId, sender.GetSegmentBuffer(maxSegments), objectId, property, value);
                else
                    sender.ErrorResponse(addr, BacnetConfirmedServices.SERVICE_CONFIRMED_READ_PROPERTY, invokeId, BacnetErrorClasses.ERROR_CLASS_DEVICE, BacnetErrorCodes.ERROR_CODE_OTHER);
            }
            catch (Exception ex)
            {
                WriteLog("ReadProperty error: {0}", ex.Message);
                sender.ErrorResponse(addr, BacnetConfirmedServices.SERVICE_CONFIRMED_READ_PROPERTY, invokeId, BacnetErrorClasses.ERROR_CLASS_DEVICE, BacnetErrorCodes.ERROR_CODE_OTHER);
            }
        }
    }

    private void OnReadPropertyMultipleRequest(BacnetClient sender, BacnetAddress addr, Byte invokeId, IList<BacnetReadAccessSpecification> properties, BacnetMaxSegments maxSegments)
    {
        using var span = Tracer?.NewSpan("bac:OnReadPropertyMultiple", new { addr, properties });
        WriteLog("ReadPropertyMultiple[{0}]: {1} {2}", addr, properties[0].objectIdentifier, properties.Join(",", e => e.propertyReferences[0].propertyIdentifier));

        var storage = Storage;
        lock (storage)
        {
            try
            {
                IList<BacnetPropertyValue> value;
                var values = new List<BacnetReadAccessResult>();
                foreach (var p in properties)
                {
                    if (p.propertyReferences.Count == 1 && p.propertyReferences[0].propertyIdentifier == (uint)BacnetPropertyIds.PROP_ALL)
                    {
                        if (!storage.ReadPropertyAll(p.objectIdentifier, out value))
                        {
                            sender.ErrorResponse(addr, BacnetConfirmedServices.SERVICE_CONFIRMED_READ_PROP_MULTIPLE, invokeId, BacnetErrorClasses.ERROR_CLASS_OBJECT, BacnetErrorCodes.ERROR_CODE_UNKNOWN_OBJECT);
                            return;
                        }
                    }
                    else
                        storage.ReadPropertyMultiple(p.objectIdentifier, p.propertyReferences, out value);
                    values.Add(new BacnetReadAccessResult(p.objectIdentifier, value));
                }

                sender.ReadPropertyMultipleResponse(addr, invokeId, sender.GetSegmentBuffer(maxSegments), values);

            }
            catch (Exception)
            {
                sender.ErrorResponse(addr, BacnetConfirmedServices.SERVICE_CONFIRMED_READ_PROP_MULTIPLE, invokeId, BacnetErrorClasses.ERROR_CLASS_DEVICE, BacnetErrorCodes.ERROR_CODE_OTHER);
            }
        }
    }

    private void OnWritePropertyRequest(BacnetClient sender, BacnetAddress addr, Byte invokeId, BacnetObjectId objectId, BacnetPropertyValue value, BacnetMaxSegments maxSegments)
    {
        using var span = Tracer?.NewSpan("bac:OnWriteProperty", new { addr, objectId, value });

        // only OBJECT_ANALOG_VALUE:0.PROP_PRESENT_VALUE could be write in this sample code
        if ((objectId.type != BacnetObjectTypes.OBJECT_ANALOG_VALUE) || (objectId.instance != 0) || ((BacnetPropertyIds)value.property.propertyIdentifier != BacnetPropertyIds.PROP_PRESENT_VALUE))
        {
            sender.ErrorResponse(addr, BacnetConfirmedServices.SERVICE_CONFIRMED_WRITE_PROPERTY, invokeId, BacnetErrorClasses.ERROR_CLASS_DEVICE, BacnetErrorCodes.ERROR_CODE_WRITE_ACCESS_DENIED);
            return;
        }

        var m_storage = Storage;
        lock (m_storage)
        {
            try
            {
                var code = m_storage.WriteCommandableProperty(objectId, (BacnetPropertyIds)value.property.propertyIdentifier, value.value[0], value.priority);
                if (code == DeviceStorage.ErrorCodes.NotForMe)
                    code = m_storage.WriteProperty(objectId, (BacnetPropertyIds)value.property.propertyIdentifier, value.property.propertyArrayIndex, value.value);

                if (code == DeviceStorage.ErrorCodes.Good)
                    sender.SimpleAckResponse(addr, BacnetConfirmedServices.SERVICE_CONFIRMED_WRITE_PROPERTY, invokeId);
                else
                    sender.ErrorResponse(addr, BacnetConfirmedServices.SERVICE_CONFIRMED_WRITE_PROPERTY, invokeId, BacnetErrorClasses.ERROR_CLASS_DEVICE, BacnetErrorCodes.ERROR_CODE_OTHER);
            }
            catch (Exception)
            {
                sender.ErrorResponse(addr, BacnetConfirmedServices.SERVICE_CONFIRMED_WRITE_PROPERTY, invokeId, BacnetErrorClasses.ERROR_CLASS_DEVICE, BacnetErrorCodes.ERROR_CODE_OTHER);
            }
        }
    }
    private void OnWritePropertyMultipleRequest(BacnetClient sender, BacnetAddress addr, Byte invokeId, BacnetObjectId objectId, ICollection<BacnetPropertyValue> values, BacnetMaxSegments maxSegments)
    {
        using var span = Tracer?.NewSpan("bac:OnWritePropertyMultiple", new { addr, objectId });
        WriteLog("WritePropertyMultiple[{0}]: {1}", addr, objectId);

        var storage = Storage;
        lock (storage)
        {
            try
            {
                var errors = storage.WritePropertyMultiple(objectId, values);
                if (errors.Any(e => e != DeviceStorage.ErrorCodes.Good))
                {
                    sender.ErrorResponse(addr, BacnetConfirmedServices.SERVICE_CONFIRMED_WRITE_PROP_MULTIPLE, invokeId, BacnetErrorClasses.ERROR_CLASS_DEVICE, BacnetErrorCodes.ERROR_CODE_WRITE_ACCESS_DENIED);
                    return;
                }

                sender.SimpleAckResponse(addr, BacnetConfirmedServices.SERVICE_CONFIRMED_WRITE_PROP_MULTIPLE, invokeId);
            }
            catch (Exception)
            {
                sender.ErrorResponse(addr, BacnetConfirmedServices.SERVICE_CONFIRMED_WRITE_PROP_MULTIPLE, invokeId, BacnetErrorClasses.ERROR_CLASS_DEVICE, BacnetErrorCodes.ERROR_CODE_OTHER);
            }
        }
    }

    private void OnWriteObjectMultipleRequest(BacnetClient sender, BacnetAddress addr, Byte invokeId, IList<(BacnetObjectId objectId, ICollection<BacnetPropertyValue> values)> valueList, BacnetMaxSegments maxSegments)
    {
        using var span = Tracer?.NewSpan("bac:OnWriteObjectMultiple", new { addr, count = valueList.Count });
        WriteLog("WriteObjectMultiple[{0}]: {1} objects", addr, valueList.Count);

        var storage = Storage;
        lock (storage)
        {
            try
            {
                foreach (var (objectId, values) in valueList)
                {
                    var errors = storage.WritePropertyMultiple(objectId, values);
                    if (errors.Any(e => e != DeviceStorage.ErrorCodes.Good))
                    {
                        WriteLog("WriteObjectMultiple denied for {0}: {1}", objectId, String.Join(",", errors));
                        sender.ErrorResponse(addr, BacnetConfirmedServices.SERVICE_CONFIRMED_WRITE_PROP_MULTIPLE, invokeId, BacnetErrorClasses.ERROR_CLASS_DEVICE, BacnetErrorCodes.ERROR_CODE_WRITE_ACCESS_DENIED);
                        return;
                    }
                }

                sender.SimpleAckResponse(addr, BacnetConfirmedServices.SERVICE_CONFIRMED_WRITE_PROP_MULTIPLE, invokeId);
            }
            catch (Exception ex)
            {
                WriteLog("WriteObjectMultiple error: {0}", ex);
                sender.ErrorResponse(addr, BacnetConfirmedServices.SERVICE_CONFIRMED_WRITE_PROP_MULTIPLE, invokeId, BacnetErrorClasses.ERROR_CLASS_DEVICE, BacnetErrorCodes.ERROR_CODE_OTHER);
            }
        }
    }
    #endregion

    #region 日志
    /// <summary>性能追踪</summary>
    public ITracer Tracer { get; set; }

    /// <summary>日志</summary>
    public ILog Log { get; set; }

    /// <summary>写日志</summary>
    /// <param name="format"></param>
    /// <param name="args"></param>
    public void WriteLog(String format, params System.Object[] args) => Log?.Info(format, args);
    #endregion
}