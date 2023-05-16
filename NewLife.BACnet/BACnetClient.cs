/**************************************************************************
*                           MIT License
* 
* Copyright (C) 2014 Morten Kvistgaard <mk@pch-engineering.dk>
*
* Permission is hereby granted, free of charge, to any person obtaining
* a copy of this software and associated documentation files (the
* "Software"), to deal in the Software without restriction, including
* without limitation the rights to use, copy, modify, merge, publish,
* distribute, sublicense, and/or sell copies of the Software, and to
* permit persons to whom the Software is furnished to do so, subject to
* the following conditions:
*
* The above copyright notice and this permission notice shall be included
* in all copies or substantial portions of the Software.
*
* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
* EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
* MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
* IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
* CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
* TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
* SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*
*********************************************************************/
using System.IO.BACnet.Serialize;
using System.Net;
using NewLife.Log;

namespace System.IO.BACnet;

public delegate void MessageRecievedHandler(IBacnetTransport sender, Byte[] buffer, Int32 offset, Int32 msgLength, BacnetAddress remoteAddress);

/// <summary>
/// BACnet network client or server
/// </summary>
public class BacnetClient : IDisposable
{
    private Int32 _retries;
    private Byte _invokeId;

    private readonly LastSegmentAck _lastSegmentAck = new();
    private UInt32 _writepriority;

    /// <summary>
    /// Dictionary of List of Tuples with sequence-number and byte[] per invoke-id
    /// TODO: invoke-id should be PER (remote) DEVICE!
    /// </summary>
    private readonly Dictionary<Byte, List<Tuple<Byte, Byte[]>>> _segmentsPerInvokeId = new();
    private readonly Dictionary<Byte, Object> _locksPerInvokeId = new();
    private readonly Dictionary<Byte, Byte> _expectedSegmentsPerInvokeId = new();

    public const Int32 DEFAULT_UDP_PORT = 0xBAC0;
    public const Int32 DEFAULT_TIMEOUT = 1000;
    public const Int32 DEFAULT_RETRIES = 3;

    public IBacnetTransport Transport { get; }
    public UInt16 VendorId { get; set; } = 260;
    public Int32 Timeout { get; set; }
    public Int32 TransmitTimeout { get; set; } = 30000;
    public BacnetMaxSegments MaxSegments { get; set; } = BacnetMaxSegments.MAX_SEG0;
    public Byte ProposedWindowSize { get; set; } = 10;
    public Boolean ForceWindowSize { get; set; }
    public Boolean DefaultSegmentationHandling { get; set; } = true;
    public ILog Log { get; set; } = XTrace.Log;

    /// <summary>
    /// Used as the number of tentatives
    /// </summary>
    public Int32 Retries
    {
        get => _retries;
        set => _retries = Math.Max(1, value);
    }

    public UInt32 WritePriority
    {
        get => _writepriority;
        set { if (value < 17) _writepriority = value; }
    }

    // These members allows to access undecoded buffer by the application
    // layer, when the basic undecoding process is not really able to do the job
    // in particular with application_specific_encoding values
    public Byte[] raw_buffer;
    public Int32 raw_offset, raw_length;

    public class Segmentation
    {
        // ReSharper disable InconsistentNaming
        // was public before refactor so can't change this
        public EncodeBuffer buffer;
        public Byte sequence_number;
        public Byte window_size;
        public Byte max_segments;
        // ReSharper restore InconsistentNaming
    }

    private class LastSegmentAck
    {
        private readonly ManualResetEvent _wait = new(false);
        private readonly Object _lockObject = new();
        private BacnetAddress _address;
        private Byte _invokeId;

        public Byte SequenceNumber;
        public Byte WindowSize;

        public void Set(BacnetAddress adr, Byte invokeId, Byte sequenceNumber, Byte windowSize)
        {
            lock (_lockObject)
            {
                _address = adr;
                _invokeId = invokeId;
                SequenceNumber = sequenceNumber;
                WindowSize = windowSize;
                _wait.Set();
            }
        }

        public Boolean Wait(BacnetAddress adr, Byte invokeId, Int32 timeout)
        {
            Monitor.Enter(_lockObject);
            while (!adr.Equals(this._address) || this._invokeId != invokeId)
            {
                _wait.Reset();
                Monitor.Exit(_lockObject);
                if (!_wait.WaitOne(timeout)) return false;
                Monitor.Enter(_lockObject);
            }
            Monitor.Exit(_lockObject);
            _address = null;
            return true;
        }
    }

    public BacnetClient(Int32 port = DEFAULT_UDP_PORT, Int32 timeout = DEFAULT_TIMEOUT, Int32 retries = DEFAULT_RETRIES)
        : this(new BacnetIpUdpProtocolTransport(port), timeout, retries)
    {
    }

    public BacnetClient(IBacnetTransport transport, Int32 timeout = DEFAULT_TIMEOUT, Int32 retries = DEFAULT_RETRIES)
    {
        Transport = transport;
        Timeout = timeout;
        Retries = retries;
    }

    public override Boolean Equals(Object obj) => Transport.Equals((obj as BacnetClient)?.Transport);

    public override Int32 GetHashCode() => Transport.GetHashCode();

    public override String ToString() => Transport.ToString();

    public EncodeBuffer GetEncodeBuffer(Int32 startOffset) => new(new Byte[Transport.MaxBufferLength], startOffset);

    public void Start()
    {
        Transport.Start();
        Transport.MessageRecieved += OnRecieve;
        //Log.Info("Started communication");
    }

    public delegate void ConfirmedServiceRequestHandler(BacnetClient sender, BacnetAddress adr, BacnetPduTypes type, BacnetConfirmedServices service, BacnetMaxSegments maxSegments, BacnetMaxAdpu maxAdpu, Byte invokeId, Byte[] buffer, Int32 offset, Int32 length);
    public event ConfirmedServiceRequestHandler OnConfirmedServiceRequest;
    public delegate void ReadPropertyRequestHandler(BacnetClient sender, BacnetAddress adr, Byte invokeId, BacnetObjectId objectId, BacnetPropertyReference property, BacnetMaxSegments maxSegments);
    public event ReadPropertyRequestHandler OnReadPropertyRequest;
    public delegate void ReadPropertyMultipleRequestHandler(BacnetClient sender, BacnetAddress adr, Byte invokeId, IList<BacnetReadAccessSpecification> properties, BacnetMaxSegments maxSegments);
    public event ReadPropertyMultipleRequestHandler OnReadPropertyMultipleRequest;
    public delegate void WritePropertyRequestHandler(BacnetClient sender, BacnetAddress adr, Byte invokeId, BacnetObjectId objectId, BacnetPropertyValue value, BacnetMaxSegments maxSegments);
    public event WritePropertyRequestHandler OnWritePropertyRequest;
    public delegate void WritePropertyMultipleRequestHandler(BacnetClient sender, BacnetAddress adr, Byte invokeId, BacnetObjectId objectId, ICollection<BacnetPropertyValue> values, BacnetMaxSegments maxSegments);
    public event WritePropertyMultipleRequestHandler OnWritePropertyMultipleRequest;
    public delegate void AtomicWriteFileRequestHandler(BacnetClient sender, BacnetAddress adr, Byte invokeId, Boolean isStream, BacnetObjectId objectId, Int32 position, UInt32 blockCount, Byte[][] blocks, Int32[] counts, BacnetMaxSegments maxSegments);
    public event AtomicWriteFileRequestHandler OnAtomicWriteFileRequest;
    public delegate void AtomicReadFileRequestHandler(BacnetClient sender, BacnetAddress adr, Byte invokeId, Boolean isStream, BacnetObjectId objectId, Int32 position, UInt32 count, BacnetMaxSegments maxSegments);
    public event AtomicReadFileRequestHandler OnAtomicReadFileRequest;
    public delegate void SubscribeCOVRequestHandler(BacnetClient sender, BacnetAddress adr, Byte invokeId, UInt32 subscriberProcessIdentifier, BacnetObjectId monitoredObjectIdentifier, Boolean cancellationRequest, Boolean issueConfirmedNotifications, UInt32 lifetime, BacnetMaxSegments maxSegments);
    public event SubscribeCOVRequestHandler OnSubscribeCOV;
    public delegate void EventNotificationCallbackHandler(BacnetClient sender, BacnetAddress adr, Byte invokeId, BacnetEventNotificationData eventData, Boolean needConfirm);
    public event EventNotificationCallbackHandler OnEventNotify;
    public delegate void SubscribeCOVPropertyRequestHandler(BacnetClient sender, BacnetAddress adr, Byte invokeId, UInt32 subscriberProcessIdentifier, BacnetObjectId monitoredObjectIdentifier, BacnetPropertyReference monitoredProperty, Boolean cancellationRequest, Boolean issueConfirmedNotifications, UInt32 lifetime, Single covIncrement, BacnetMaxSegments maxSegments);
    public event SubscribeCOVPropertyRequestHandler OnSubscribeCOVProperty;
    public delegate void DeviceCommunicationControlRequestHandler(BacnetClient sender, BacnetAddress adr, Byte invokeId, UInt32 timeDuration, UInt32 enableDisable, String password, BacnetMaxSegments maxSegments);
    public event DeviceCommunicationControlRequestHandler OnDeviceCommunicationControl;
    public delegate void ReinitializedRequestHandler(BacnetClient sender, BacnetAddress adr, Byte invokeId, BacnetReinitializedStates state, String password, BacnetMaxSegments maxSegments);
    public event ReinitializedRequestHandler OnReinitializedDevice;
    public delegate void ReadRangeHandler(BacnetClient sender, BacnetAddress adr, Byte invokeId, BacnetObjectId objectId, BacnetPropertyReference property, BacnetReadRangeRequestTypes requestType, UInt32 position, DateTime time, Int32 count, BacnetMaxSegments maxSegments);
    public event ReadRangeHandler OnReadRange;
    public delegate void CreateObjectRequestHandler(BacnetClient sender, BacnetAddress adr, Byte invokeId, BacnetObjectId objectId, ICollection<BacnetPropertyValue> values, BacnetMaxSegments maxSegments);
    public event CreateObjectRequestHandler OnCreateObjectRequest;
    public delegate void DeleteObjectRequestHandler(BacnetClient sender, BacnetAddress adr, Byte invokeId, BacnetObjectId objectId, BacnetMaxSegments maxSegments);
    public event DeleteObjectRequestHandler OnDeleteObjectRequest;
    public delegate void GetAlarmSummaryOrEventInformationRequestHandler(BacnetClient sender, BacnetAddress adr, Byte invokeId, Boolean getEvent, BacnetObjectId objectId, BacnetMaxAdpu maxApdu, BacnetMaxSegments max_segments);
    public event GetAlarmSummaryOrEventInformationRequestHandler OnGetAlarmSummaryOrEventInformation;
    public delegate void AlarmAcknowledgeRequestHandler(BacnetClient sender, BacnetAddress adr, Byte invokeId, UInt32 ackProcessIdentifier, BacnetObjectId eventObjectIdentifier, UInt32 eventStateAcked, String ackSource, BacnetGenericTime eventTimeStamp, BacnetGenericTime ackTimeStamp);
    public event AlarmAcknowledgeRequestHandler OnAlarmAcknowledge;

    protected void ProcessConfirmedServiceRequest(BacnetAddress address, BacnetPduTypes type, BacnetConfirmedServices service, BacnetMaxSegments maxSegments, BacnetMaxAdpu maxAdpu, Byte invokeId, Byte[] buffer, Int32 offset, Int32 length)
    {
        try
        {
            Log.Debug($"ConfirmedServiceRequest {service}");

            raw_buffer = buffer;
            raw_length = length;
            raw_offset = offset;

            OnConfirmedServiceRequest?.Invoke(this, address, type, service, maxSegments, maxAdpu, invokeId, buffer, offset, length);

            //don't send segmented messages, if client don't want it
            if ((type & BacnetPduTypes.SEGMENTED_RESPONSE_ACCEPTED) == 0)
                maxSegments = BacnetMaxSegments.MAX_SEG0;

            if (service == BacnetConfirmedServices.SERVICE_CONFIRMED_READ_PROPERTY && OnReadPropertyRequest != null)
            {
                Int32 thsRejectReason;

                if ((thsRejectReason = Services.DecodeReadProperty(buffer, offset, length, out var objectId, out var property)) >= 0)
                {
                    OnReadPropertyRequest(this, address, invokeId, objectId, property, maxSegments);
                }
                else
                {
                    switch (thsRejectReason)
                    {
                        case -1:
                            SendConfirmedServiceReject(address, invokeId, BacnetRejectReason.MISSING_REQUIRED_PARAMETER);
                            break;
                        case -2:
                            SendConfirmedServiceReject(address, invokeId, BacnetRejectReason.INVALID_TAG);
                            break;
                        case -3:
                            SendConfirmedServiceReject(address, invokeId, BacnetRejectReason.TOO_MANY_ARGUMENTS);
                            break;
                    }
                    Log.Warn("Couldn't decode DecodeReadProperty");
                }
            }
            else if (service == BacnetConfirmedServices.SERVICE_CONFIRMED_WRITE_PROPERTY && OnWritePropertyRequest != null)
            {
                if (Services.DecodeWriteProperty(address, buffer, offset, length, out var objectId, out var value) >= 0)
                    OnWritePropertyRequest(this, address, invokeId, objectId, value, maxSegments);
                else
                {
                    // DAL
                    SendAbort(address, invokeId, BacnetAbortReason.OTHER);
                    //ErrorResponse(address, service, invokeId, BacnetErrorClasses.ERROR_CLASS_SERVICES, BacnetErrorCodes.ERROR_CODE_ABORT_OTHER);
                    //SendConfirmedServiceReject(adr, invokeId, BacnetRejectReason.OTHER); 
                    Log.Warn("Couldn't decode DecodeWriteProperty");
                }
            }
            else if (service == BacnetConfirmedServices.SERVICE_CONFIRMED_READ_PROP_MULTIPLE && OnReadPropertyMultipleRequest != null)
            {
                if (Services.DecodeReadPropertyMultiple(buffer, offset, length, out var properties) >= 0)
                    OnReadPropertyMultipleRequest(this, address, invokeId, properties, maxSegments);
                else
                {
                    // DAL
                    SendAbort(address, invokeId, BacnetAbortReason.OTHER);
                    //ErrorResponse(address, service, invokeId, BacnetErrorClasses.ERROR_CLASS_SERVICES, BacnetErrorCodes.ERROR_CODE_ABORT_OTHER);
                    Log.Warn("Couldn't decode DecodeReadPropertyMultiple");
                }
            }
            else if (service == BacnetConfirmedServices.SERVICE_CONFIRMED_WRITE_PROP_MULTIPLE && OnWritePropertyMultipleRequest != null)
            {
                if (Services.DecodeWritePropertyMultiple(address, buffer, offset, length, out var objectId, out var values) >= 0)
                    OnWritePropertyMultipleRequest(this, address, invokeId, objectId, values, maxSegments);
                else
                {
                    // DAL
                    SendAbort(address, invokeId, BacnetAbortReason.OTHER);
                    //ErrorResponse(address, service, invokeId, BacnetErrorClasses.ERROR_CLASS_SERVICES, BacnetErrorCodes.ERROR_CODE_ABORT_OTHER);
                    Log.Warn("Couldn't decode DecodeWritePropertyMultiple");
                }
            }
            else if (service == BacnetConfirmedServices.SERVICE_CONFIRMED_COV_NOTIFICATION && OnCOVNotification != null)
            {
                if (Services.DecodeCOVNotifyUnconfirmed(address, buffer, offset, length, out var subscriberProcessIdentifier, out var initiatingDeviceIdentifier, out var monitoredObjectIdentifier, out var timeRemaining, out var values) >= 0)
                    OnCOVNotification(this, address, invokeId, subscriberProcessIdentifier, initiatingDeviceIdentifier, monitoredObjectIdentifier, timeRemaining, true, values, maxSegments);
                else
                {
                    // DAL
                    SendAbort(address, invokeId, BacnetAbortReason.OTHER);
                    //ErrorResponse(address, service, invokeId, BacnetErrorClasses.ERROR_CLASS_SERVICES, BacnetErrorCodes.ERROR_CODE_ABORT_OTHER);
                    Log.Warn("Couldn't decode COVNotify");
                }
            }
            else if (service == BacnetConfirmedServices.SERVICE_CONFIRMED_ATOMIC_WRITE_FILE && OnAtomicWriteFileRequest != null)
            {
                if (Services.DecodeAtomicWriteFile(buffer, offset, length, out var isStream, out var objectId, out var position, out var blockCount, out var blocks, out var counts) >= 0)
                    OnAtomicWriteFileRequest(this, address, invokeId, isStream, objectId, position, blockCount, blocks, counts, maxSegments);
                else
                {
                    // DAL
                    SendAbort(address, invokeId, BacnetAbortReason.OTHER);
                    //ErrorResponse(address, service, invokeId, BacnetErrorClasses.ERROR_CLASS_SERVICES, BacnetErrorCodes.ERROR_CODE_ABORT_OTHER);
                    Log.Warn("Couldn't decode AtomicWriteFile");
                }
            }
            else if (service == BacnetConfirmedServices.SERVICE_CONFIRMED_ATOMIC_READ_FILE && OnAtomicReadFileRequest != null)
            {
                if (Services.DecodeAtomicReadFile(buffer, offset, length, out var isStream, out var objectId, out var position, out var count) >= 0)
                    OnAtomicReadFileRequest(this, address, invokeId, isStream, objectId, position, count, maxSegments);
                else
                {
                    // DAL
                    SendAbort(address, invokeId, BacnetAbortReason.OTHER);
                    //ErrorResponse(address, service, invokeId, BacnetErrorClasses.ERROR_CLASS_SERVICES, BacnetErrorCodes.ERROR_CODE_ABORT_OTHER);
                    Log.Warn("Couldn't decode AtomicReadFile");
                }
            }
            else if (service == BacnetConfirmedServices.SERVICE_CONFIRMED_SUBSCRIBE_COV && OnSubscribeCOV != null)
            {
                if (Services.DecodeSubscribeCOV(buffer, offset, length, out var subscriberProcessIdentifier, out var monitoredObjectIdentifier, out var cancellationRequest, out var issueConfirmedNotifications, out var lifetime) >= 0)
                    OnSubscribeCOV(this, address, invokeId, subscriberProcessIdentifier, monitoredObjectIdentifier, cancellationRequest, issueConfirmedNotifications, lifetime, maxSegments);
                else
                {
                    // DAL
                    SendAbort(address, invokeId, BacnetAbortReason.OTHER);
                    //ErrorResponse(address, service, invokeId, BacnetErrorClasses.ERROR_CLASS_SERVICES, BacnetErrorCodes.ERROR_CODE_ABORT_OTHER);
                    Log.Warn("Couldn't decode SubscribeCOV");
                }
            }
            else if (service == BacnetConfirmedServices.SERVICE_CONFIRMED_SUBSCRIBE_COV_PROPERTY && OnSubscribeCOVProperty != null)
            {
                if (Services.DecodeSubscribeProperty(buffer, offset, length, out var subscriberProcessIdentifier, out var monitoredObjectIdentifier, out var monitoredProperty, out var cancellationRequest, out var issueConfirmedNotifications, out var lifetime, out var covIncrement) >= 0)
                    OnSubscribeCOVProperty(this, address, invokeId, subscriberProcessIdentifier, monitoredObjectIdentifier, monitoredProperty, cancellationRequest, issueConfirmedNotifications, lifetime, covIncrement, maxSegments);
                else
                {
                    // DAL
                    SendAbort(address, invokeId, BacnetAbortReason.OTHER);
                    //ErrorResponse(address, service, invokeId, BacnetErrorClasses.ERROR_CLASS_SERVICES, BacnetErrorCodes.ERROR_CODE_ABORT_OTHER);
                    Log.Warn("Couldn't decode SubscribeCOVProperty");
                }
            }
            else if (service == BacnetConfirmedServices.SERVICE_CONFIRMED_DEVICE_COMMUNICATION_CONTROL && OnDeviceCommunicationControl != null)
            {
                // DAL
                if (Services.DecodeDeviceCommunicationControl(buffer, offset, length, out var timeDuration, out var enableDisable, out var password) >= 0)
                    OnDeviceCommunicationControl(this, address, invokeId, timeDuration, enableDisable, password, maxSegments);
                else
                {
                    // DAL
                    SendAbort(address, invokeId, BacnetAbortReason.OTHER);
                    //ErrorResponse(address, service, invokeId, BacnetErrorClasses.ERROR_CLASS_SERVICES, BacnetErrorCodes.ERROR_CODE_ABORT_OTHER);
                    Log.Warn("Couldn't decode DeviceCommunicationControl");
                }
            }
            else if (service == BacnetConfirmedServices.SERVICE_CONFIRMED_REINITIALIZE_DEVICE && OnReinitializedDevice != null)
            {
                // DAL
                if (Services.DecodeReinitializeDevice(buffer, offset, length, out var state, out var password) >= 0)
                    OnReinitializedDevice(this, address, invokeId, state, password, maxSegments);
                else
                {
                    // DAL
                    SendAbort(address, invokeId, BacnetAbortReason.OTHER);
                    //ErrorResponse(address, service, invokeId, BacnetErrorClasses.ERROR_CLASS_SERVICES, BacnetErrorCodes.ERROR_CODE_ABORT_OTHER);
                    Log.Warn("Couldn't decode ReinitializeDevice");
                }
            }
            else if (service == BacnetConfirmedServices.SERVICE_CONFIRMED_EVENT_NOTIFICATION && OnEventNotify != null) // F. Chaxel
            {
                if (Services.DecodeEventNotifyData(buffer, offset, length, out var eventData) >= 0)
                {
                    OnEventNotify(this, address, invokeId, eventData, true);
                }
                else
                {
                    // DAL
                    SendAbort(address, invokeId, BacnetAbortReason.OTHER);
                    //ErrorResponse(address, service, invokeId, BacnetErrorClasses.ERROR_CLASS_SERVICES, BacnetErrorCodes.ERROR_CODE_ABORT_OTHER);
                    Log.Warn("Couldn't decode confirmed Event/Alarm Notification");
                }
            }
            else if (service == BacnetConfirmedServices.SERVICE_CONFIRMED_READ_RANGE && OnReadRange != null)
            {
                if (Services.DecodeReadRange(buffer, offset, length, out var objectId, out var property, out var requestType, out var position, out var time, out var count) >= 0)
                    OnReadRange(this, address, invokeId, objectId, property, requestType, position, time, count, maxSegments);
                else
                {
                    // DAL
                    SendAbort(address, invokeId, BacnetAbortReason.OTHER);
                    //ErrorResponse(address, service, invokeId, BacnetErrorClasses.ERROR_CLASS_SERVICES, BacnetErrorCodes.ERROR_CODE_ABORT_OTHER);
                    Log.Warn("Couldn't decode ReadRange");
                }
            }
            else if (service == BacnetConfirmedServices.SERVICE_CONFIRMED_CREATE_OBJECT && OnCreateObjectRequest != null)
            {
                if (Services.DecodeCreateObject(address, buffer, offset, length, out var objectId, out var values) >= 0)
                    OnCreateObjectRequest(this, address, invokeId, objectId, values, maxSegments);
                else
                {
                    // DAL
                    SendAbort(address, invokeId, BacnetAbortReason.OTHER);
                    //ErrorResponse(address, service, invokeId, BacnetErrorClasses.ERROR_CLASS_SERVICES, BacnetErrorCodes.ERROR_CODE_ABORT_OTHER);
                    Log.Warn("Couldn't decode CreateObject");
                }
            }
            else if (service == BacnetConfirmedServices.SERVICE_CONFIRMED_DELETE_OBJECT && OnDeleteObjectRequest != null)
            {
                if (Services.DecodeDeleteObject(buffer, offset, length, out var objectId) >= 0)
                    OnDeleteObjectRequest(this, address, invokeId, objectId, maxSegments);
                else
                {
                    // DAL
                    SendAbort(address, invokeId, BacnetAbortReason.OTHER);
                    //ErrorResponse(address, service, invokeId, BacnetErrorClasses.ERROR_CLASS_SERVICES, BacnetErrorCodes.ERROR_CODE_ABORT_OTHER);
                    Log.Warn("Couldn't decode DecodeDeleteObject");
                }
            }
            else if (service == BacnetConfirmedServices.SERVICE_CONFIRMED_GET_ALARM_SUMMARY && OnGetAlarmSummaryOrEventInformation != null)
            {
                // DAL -- added the core code required but since I couldn't test it we just reject this service
                // rejecting it shouldn't be too bad a thing since GetAlarmSummary has been retired anyway...
                // if someone needs it they can uncomment the related code and test.
#if false
                    BacnetObjectId objectId = default(BacnetObjectId);
                    objectId.Type = BacnetObjectTypes.MAX_BACNET_OBJECT_TYPE;
                    if (Services.DecodeAlarmSummaryOrEventRequest(buffer, offset, length, false, ref objectId) >= 0)
                    {
                        OnGetAlarmSummaryOrEventInformation(this, address, invokeId, false, objectId, maxAdpu, maxSegments);
                    }
                    else
                    {
                        // DAL
                        SendAbort(address, invokeId, BacnetAbortReason.OTHER);
                        //ErrorResponse(address, service, invokeId, BacnetErrorClasses.ERROR_CLASS_SERVICES, BacnetErrorCodes.ERROR_CODE_ABORT_OTHER);
                        Log.Warn("Couldn't decode GetAlarmSummary");
                    }
#else
                SendConfirmedServiceReject(address, invokeId, BacnetRejectReason.RECOGNIZED_SERVICE); // should be unrecognized but this is the way it was spelled..
#endif
            }
            else if (service == BacnetConfirmedServices.SERVICE_CONFIRMED_GET_EVENT_INFORMATION && OnGetAlarmSummaryOrEventInformation != null)
            {
                // DAL
                BacnetObjectId objectId = default;
                objectId.Type = BacnetObjectTypes.MAX_BACNET_OBJECT_TYPE;
                if (Services.DecodeAlarmSummaryOrEventRequest(buffer, offset, length, true, ref objectId) >= 0)
                {
                    OnGetAlarmSummaryOrEventInformation(this, address, invokeId, true, objectId, maxAdpu, maxSegments);
                }
                else
                {
                    // DAL
                    SendAbort(address, invokeId, BacnetAbortReason.OTHER);
                    //ErrorResponse(address, service, invokeId, BacnetErrorClasses.ERROR_CLASS_SERVICES, BacnetErrorCodes.ERROR_CODE_ABORT_OTHER);
                    Log.Warn("Couldn't decode GetEventInformation");
                }
            }
            else if (service == BacnetConfirmedServices.SERVICE_CONFIRMED_ACKNOWLEDGE_ALARM && OnAlarmAcknowledge != null)
            {
                // DAL
                if (Services.DecodeAlarmAcknowledge(buffer, offset, length, out var ackProcessIdentifier, out var eventObjectIdentifier, out var eventStateAcked, out var ackSource, out var eventTimeStamp, out var ackTimeStamp) >= 0)
                {
                    OnAlarmAcknowledge(this, address, invokeId, ackProcessIdentifier, eventObjectIdentifier, eventStateAcked, ackSource, eventTimeStamp, ackTimeStamp);
                }
                else
                {
                    // DAL
                    SendAbort(address, invokeId, BacnetAbortReason.OTHER);
                    //ErrorResponse(address, service, invokeId, BacnetErrorClasses.ERROR_CLASS_SERVICES, BacnetErrorCodes.ERROR_CODE_ABORT_OTHER);
                    Log.Warn("Couldn't decode AlarmAcknowledge");
                }
            }
            else
            {
                // DAL
                SendConfirmedServiceReject(address, invokeId, BacnetRejectReason.RECOGNIZED_SERVICE); // should be unrecognized but this is the way it was spelled..
                Log.Debug($"Confirmed service not handled: {service}");
            }
        }
        catch (Exception ex)
        {
            // DAL
            SendAbort(address, invokeId, BacnetAbortReason.OTHER);
            //ErrorResponse(address, service, invokeId, BacnetErrorClasses.ERROR_CLASS_DEVICE, BacnetErrorCodes.ERROR_CODE_ABORT_OTHER);
            Log.Error("Error in ProcessConfirmedServiceRequest", ex);
        }
    }

    public delegate void UnconfirmedServiceRequestHandler(BacnetClient sender, BacnetAddress adr, BacnetPduTypes type, BacnetUnconfirmedServices service, Byte[] buffer, Int32 offset, Int32 length);
    public event UnconfirmedServiceRequestHandler OnUnconfirmedServiceRequest;
    public delegate void WhoHasHandler(BacnetClient sender, BacnetAddress adr, Int32 lowLimit, Int32 highLimit, BacnetObjectId? objId, String objName);
    public event WhoHasHandler OnWhoHas;
    public delegate void IamHandler(BacnetClient sender, BacnetAddress adr, UInt32 deviceId, UInt32 maxAPDU, BacnetSegmentations segmentation, UInt16 vendorId);
    public event IamHandler OnIam;
    public delegate void WhoIsHandler(BacnetClient sender, BacnetAddress adr, Int32 lowLimit, Int32 highLimit);
    public event WhoIsHandler OnWhoIs;
    public delegate void TimeSynchronizeHandler(BacnetClient sender, BacnetAddress adr, DateTime dateTime, Boolean utc);
    public event TimeSynchronizeHandler OnTimeSynchronize;

    //used by both 'confirmed' and 'unconfirmed' notify
    public delegate void COVNotificationHandler(BacnetClient sender, BacnetAddress adr, Byte invokeId, UInt32 subscriberProcessIdentifier, BacnetObjectId initiatingDeviceIdentifier, BacnetObjectId monitoredObjectIdentifier, UInt32 timeRemaining, Boolean needConfirm, ICollection<BacnetPropertyValue> values, BacnetMaxSegments maxSegments);
    public event COVNotificationHandler OnCOVNotification;

    protected void ProcessUnconfirmedServiceRequest(BacnetAddress address, BacnetPduTypes type, BacnetUnconfirmedServices service, Byte[] buffer, Int32 offset, Int32 length)
    {
        try
        {
            Log.Debug("<=[{0}]: {1}", address, service);
            OnUnconfirmedServiceRequest?.Invoke(this, address, type, service, buffer, offset, length);
            if (service == BacnetUnconfirmedServices.SERVICE_UNCONFIRMED_I_AM && OnIam != null)
            {
                if (Services.DecodeIamBroadcast(buffer, offset, out var deviceId, out var maxAdpu, out var segmentation, out var vendorId) >= 0)
                    OnIam(this, address, deviceId, maxAdpu, segmentation, vendorId);
                else
                    Log.Warn("Couldn't decode IamBroadcast");
            }
            else if (service == BacnetUnconfirmedServices.SERVICE_UNCONFIRMED_WHO_IS && OnWhoIs != null)
            {
                if (Services.DecodeWhoIsBroadcast(buffer, offset, length, out var lowLimit, out var highLimit) >= 0)
                    OnWhoIs(this, address, lowLimit, highLimit);
                else
                    Log.Warn("Couldn't decode WhoIsBroadcast");
            }
            // added by thamersalek
            else if (service == BacnetUnconfirmedServices.SERVICE_UNCONFIRMED_WHO_HAS && OnWhoHas != null)
            {
                if (Services.DecodeWhoHasBroadcast(buffer, offset, length, out var lowLimit, out var highLimit, out var objId, out var objName) >= 0)
                    OnWhoHas(this, address, lowLimit, highLimit, objId, objName);
                else
                    Log.Warn("Couldn't decode WhoHasBroadcast");
            }
            else if (service == BacnetUnconfirmedServices.SERVICE_UNCONFIRMED_COV_NOTIFICATION && OnCOVNotification != null)
            {
                if (Services.DecodeCOVNotifyUnconfirmed(address, buffer, offset, length, out var subscriberProcessIdentifier, out var initiatingDeviceIdentifier, out var monitoredObjectIdentifier, out var timeRemaining, out var values) >= 0)
                    OnCOVNotification(this, address, 0, subscriberProcessIdentifier, initiatingDeviceIdentifier, monitoredObjectIdentifier, timeRemaining, false, values, BacnetMaxSegments.MAX_SEG0);
                else
                    Log.Warn("Couldn't decode COVNotifyUnconfirmed");
            }
            else if (service == BacnetUnconfirmedServices.SERVICE_UNCONFIRMED_TIME_SYNCHRONIZATION && OnTimeSynchronize != null)
            {
                if (Services.DecodeTimeSync(buffer, offset, length, out var dateTime) >= 0)
                    OnTimeSynchronize(this, address, dateTime, false);
                else
                    Log.Warn("Couldn't decode TimeSynchronize");
            }
            else if (service == BacnetUnconfirmedServices.SERVICE_UNCONFIRMED_UTC_TIME_SYNCHRONIZATION && OnTimeSynchronize != null)
            {
                if (Services.DecodeTimeSync(buffer, offset, length, out var dateTime) >= 0)
                    OnTimeSynchronize(this, address, dateTime, true);
                else
                    Log.Warn("Couldn't decode TimeSynchronize");
            }
            else if (service == BacnetUnconfirmedServices.SERVICE_UNCONFIRMED_EVENT_NOTIFICATION && OnEventNotify != null) // F. Chaxel
            {
                if (Services.DecodeEventNotifyData(buffer, offset, length, out var eventData) >= 0)
                    OnEventNotify(this, address, 0, eventData, false);
                else
                    Log.Warn("Couldn't decode unconfirmed Event/Alarm Notification");
            }
            else
            {
                Log.Debug($"Unconfirmed service not handled: {service}");
                // SendUnConfirmedServiceReject(adr); ? exists ?
            }
        }
        catch (Exception ex)
        {
            Log.Error("Error in ProcessUnconfirmedServiceRequest", ex);
        }
    }

    public delegate void SimpleAckHandler(BacnetClient sender, BacnetAddress adr, BacnetPduTypes type, BacnetConfirmedServices service, Byte invokeId, Byte[] data, Int32 dataOffset, Int32 dataLength);
    public event SimpleAckHandler OnSimpleAck;

    protected void ProcessSimpleAck(BacnetAddress adr, BacnetPduTypes type, BacnetConfirmedServices service, Byte invokeId, Byte[] buffer, Int32 offset, Int32 length)
    {
        try
        {
            Log.Debug($"Received SimpleAck for {service}");
            OnSimpleAck?.Invoke(this, adr, type, service, invokeId, buffer, offset, length);
        }
        catch (Exception ex)
        {
            Log.Error("Error in ProcessSimpleAck", ex);
        }
    }

    public delegate void ComplexAckHandler(BacnetClient sender, BacnetAddress adr, BacnetPduTypes type, BacnetConfirmedServices service, Byte invokeId, Byte[] buffer, Int32 offset, Int32 length);
    public event ComplexAckHandler OnComplexAck;

    protected void ProcessComplexAck(BacnetAddress adr, BacnetPduTypes type, BacnetConfirmedServices service, Byte invokeId, Byte[] buffer, Int32 offset, Int32 length)
    {
        try
        {
            Log.Debug($"Received ComplexAck for {service}");
            OnComplexAck?.Invoke(this, adr, type, service, invokeId, buffer, offset, length);
        }
        catch (Exception ex)
        {
            Log.Error($"Error in {nameof(ProcessComplexAck)}", ex);
        }
    }

    public delegate void ErrorHandler(BacnetClient sender, BacnetAddress adr, BacnetPduTypes type, BacnetConfirmedServices service, Byte invokeId, BacnetErrorClasses errorClass, BacnetErrorCodes errorCode, Byte[] buffer, Int32 offset, Int32 length);
    public event ErrorHandler OnError;

    protected void ProcessError(BacnetAddress adr, BacnetPduTypes type, BacnetConfirmedServices service, Byte invokeId, Byte[] buffer, Int32 offset, Int32 length)
    {
        try
        {
            if (Services.DecodeError(buffer, offset, length, out var errorClass, out var errorCode) < 0)
                Log.Warn("Couldn't decode received Error");

            Log.Debug($"Received Error {errorClass} {errorCode}");
            OnError?.Invoke(this, adr, type, service, invokeId, errorClass, errorCode, buffer, offset, length);
        }
        catch (Exception ex)
        {
            Log.Error($"Error in {nameof(ProcessError)}", ex);
        }
    }

    public delegate void AbortHandler(BacnetClient sender, BacnetAddress adr, BacnetPduTypes type, Byte invokeId, BacnetAbortReason reason, Byte[] buffer, Int32 offset, Int32 length);
    public event AbortHandler OnAbort;

    protected void ProcessAbort(BacnetAddress adr, BacnetPduTypes type, Byte invokeId, BacnetAbortReason reason, Byte[] buffer, Int32 offset, Int32 length)
    {
        try
        {
            Log.Debug($"Received Abort, reason: {reason}");
            OnAbort?.Invoke(this, adr, type, invokeId, reason, buffer, offset, length);
        }
        catch (Exception ex)
        {
            Log.Error("Error in ProcessAbort", ex);
        }
    }

    public delegate void RejectHandler(BacnetClient sender, BacnetAddress adr, BacnetPduTypes type, Byte invokeId, BacnetRejectReason reason, Byte[] buffer, Int32 offset, Int32 length);
    public event RejectHandler OnReject;

    protected void ProcessReject(BacnetAddress adr, BacnetPduTypes type, Byte invokeId, BacnetRejectReason reason, Byte[] buffer, Int32 offset, Int32 length)
    {
        try
        {
            Log.Debug($"Received Reject, reason: {reason}");
            OnReject?.Invoke(this, adr, type, invokeId, reason, buffer, offset, length);
        }
        catch (Exception ex)
        {
            Log.Error("Error in ProcessReject", ex);
        }
    }

    public delegate void SegmentAckHandler(BacnetClient sender, BacnetAddress adr, BacnetPduTypes type, Byte originalInvokeId, Byte sequenceNumber, Byte actualWindowSize, Byte[] buffer, Int32 offset, Int32 length);
    public event SegmentAckHandler OnSegmentAck;

    protected void ProcessSegmentAck(BacnetAddress adr, BacnetPduTypes type, Byte originalInvokeId, Byte sequenceNumber, Byte actualWindowSize, Byte[] buffer, Int32 offset, Int32 length)
    {
        try
        {
            Log.Debug("Received SegmentAck");
            OnSegmentAck?.Invoke(this, adr, type, originalInvokeId, sequenceNumber, actualWindowSize, buffer, offset, length);
        }
        catch (Exception ex)
        {
            Log.Error("Error in ProcessSegmentAck", ex);
        }
    }

    public delegate void SegmentHandler(BacnetClient sender, BacnetAddress adr, BacnetPduTypes type, BacnetConfirmedServices service, Byte invokeId, BacnetMaxSegments maxSegments, BacnetMaxAdpu maxAdpu, Byte sequenceNumber, Byte[] buffer, Int32 offset, Int32 length);
    public event SegmentHandler OnSegment;

    private void ProcessSegment(BacnetAddress address, BacnetPduTypes type, BacnetConfirmedServices service, Byte invokeId, BacnetMaxSegments maxSegments, BacnetMaxAdpu maxAdpu, Boolean server, Byte sequenceNumber, Byte proposedWindowNumber, Byte[] buffer, Int32 offset, Int32 length)
    {
        if (!_locksPerInvokeId.TryGetValue(invokeId, out var lockObj))
        {
            lockObj = new Object();
            _locksPerInvokeId[invokeId] = lockObj;
        }

        lock (lockObj)
        {
            ProcessSegmentLocked(address, type, service, invokeId, maxSegments, maxAdpu, server, sequenceNumber,
                proposedWindowNumber, buffer, offset, length);
        }
    }

    private void ProcessSegmentLocked(BacnetAddress adr, BacnetPduTypes type, BacnetConfirmedServices service,
        Byte invokeId, BacnetMaxSegments maxSegments, BacnetMaxAdpu maxAdpu, Boolean server, Byte sequenceNumber,
        Byte proposedWindowNumber, Byte[] buffer, Int32 offset, Int32 length)
    {
        Log.Info($@"Processing Segment #{sequenceNumber} of invoke-id #{invokeId}");

        if (!_segmentsPerInvokeId.ContainsKey(invokeId))
            _segmentsPerInvokeId[invokeId] = new List<Tuple<Byte, Byte[]>>();

        if (!_expectedSegmentsPerInvokeId.ContainsKey(invokeId))
            _expectedSegmentsPerInvokeId[invokeId] = Byte.MaxValue;

        var moreFollows = (type & BacnetPduTypes.MORE_FOLLOWS) == BacnetPduTypes.MORE_FOLLOWS;

        if (!moreFollows)
            _expectedSegmentsPerInvokeId[invokeId] = (Byte)(sequenceNumber + 1);

        //send ACK
        if (sequenceNumber % proposedWindowNumber == 0 || !moreFollows)
        {
            if (ForceWindowSize)
                proposedWindowNumber = ProposedWindowSize;

            SegmentAckResponse(adr, false, server, invokeId, sequenceNumber, proposedWindowNumber);
        }

        //Send on
        OnSegment?.Invoke(this, adr, type, service, invokeId, maxSegments, maxAdpu, sequenceNumber, buffer, offset, length);

        //default segment assembly. We run this seperately from the above handler, to make sure that it comes after!
        if (DefaultSegmentationHandling)
            PerformDefaultSegmentHandling(adr, type, service, invokeId, maxSegments, maxAdpu, sequenceNumber, buffer, offset, length);
    }

    /// <summary>
    /// This is a simple handling that stores all segments in memory and assembles them when done
    /// </summary>
    private void PerformDefaultSegmentHandling(BacnetAddress adr, BacnetPduTypes type, BacnetConfirmedServices service, Byte invokeId, BacnetMaxSegments maxSegments, BacnetMaxAdpu maxAdpu, Byte sequenceNumber, Byte[] buffer, Int32 offset, Int32 length)
    {
        var segments = _segmentsPerInvokeId[invokeId];

        if (sequenceNumber == 0)
        {
            //copy buffer + encode new adpu header
            type &= ~BacnetPduTypes.SEGMENTED_MESSAGE;
            var confirmedServiceRequest = (type & BacnetPduTypes.PDU_TYPE_MASK) == BacnetPduTypes.PDU_TYPE_CONFIRMED_SERVICE_REQUEST;
            var adpuHeaderLen = confirmedServiceRequest ? 4 : 3;

            var copy = new Byte[length + adpuHeaderLen];
            Array.Copy(buffer, offset, copy, adpuHeaderLen, length);
            var encodedBuffer = new EncodeBuffer(copy, 0);

            if (confirmedServiceRequest)
                APDU.EncodeConfirmedServiceRequest(encodedBuffer, type, service, maxSegments, maxAdpu, invokeId);
            else
                APDU.EncodeComplexAck(encodedBuffer, type, service, invokeId);

            segments.Add(Tuple.Create(sequenceNumber, copy)); // doesn't include BVLC or NPDU
        }
        else
        {
            //copy only content part
            segments.Add(Tuple.Create(sequenceNumber, buffer.Skip(offset).Take(length).ToArray()));
        }

        //process when finished
        if (segments.Count < _expectedSegmentsPerInvokeId[invokeId])
            return;

        //assemble whole part
        var apduBuffer = segments.OrderBy(s => s.Item1).SelectMany(s => s.Item2).ToArray();
        segments.Clear();
        _expectedSegmentsPerInvokeId[invokeId] = Byte.MaxValue;

        //process
        ProcessApdu(adr, type, apduBuffer, 0, apduBuffer.Length);
    }

    private void ProcessApdu(BacnetAddress adr, BacnetPduTypes type, Byte[] buffer, Int32 offset, Int32 length)
    {
        switch (type & BacnetPduTypes.PDU_TYPE_MASK)
        {
            case BacnetPduTypes.PDU_TYPE_UNCONFIRMED_SERVICE_REQUEST:
                {
                    var apduHeaderLen = APDU.DecodeUnconfirmedServiceRequest(buffer, offset, out type, out var service);
                    offset += apduHeaderLen;
                    length -= apduHeaderLen;
                    ProcessUnconfirmedServiceRequest(adr, type, service, buffer, offset, length);
                }
                break;

            case BacnetPduTypes.PDU_TYPE_SIMPLE_ACK:
                {
                    var apduHeaderLen = APDU.DecodeSimpleAck(buffer, offset, out type, out var service, out var invokeId);
                    offset += apduHeaderLen;
                    length -= apduHeaderLen;
                    ProcessSimpleAck(adr, type, service, invokeId, buffer, offset, length);
                }
                break;

            case BacnetPduTypes.PDU_TYPE_COMPLEX_ACK:
                {
                    var apduHeaderLen = APDU.DecodeComplexAck(buffer, offset, out type, out var service, out var invokeId,
                        out var sequenceNumber, out var proposedWindowNumber);

                    offset += apduHeaderLen;
                    length -= apduHeaderLen;
                    if ((type & BacnetPduTypes.SEGMENTED_MESSAGE) == 0) //don't process segmented messages here
                    {
                        ProcessComplexAck(adr, type, service, invokeId, buffer, offset, length);
                    }
                    else
                    {
                        ProcessSegment(adr, type, service, invokeId, BacnetMaxSegments.MAX_SEG0, BacnetMaxAdpu.MAX_APDU50, false,
                            sequenceNumber, proposedWindowNumber, buffer, offset, length);
                    }
                }
                break;

            case BacnetPduTypes.PDU_TYPE_SEGMENT_ACK:
                {
                    var apduHeaderLen = APDU.DecodeSegmentAck(buffer, offset, out type, out var originalInvokeId,
                        out var sequenceNumber, out var actualWindowSize);

                    offset += apduHeaderLen;
                    length -= apduHeaderLen;
                    _lastSegmentAck.Set(adr, originalInvokeId, sequenceNumber, actualWindowSize);
                    ProcessSegmentAck(adr, type, originalInvokeId, sequenceNumber, actualWindowSize, buffer, offset, length);
                }
                break;

            case BacnetPduTypes.PDU_TYPE_ERROR:
                {
                    var apduHeaderLen = APDU.DecodeError(buffer, offset, out type, out var service, out var invokeId);
                    offset += apduHeaderLen;
                    length -= apduHeaderLen;
                    ProcessError(adr, type, service, invokeId, buffer, offset, length);
                }
                break;

            case BacnetPduTypes.PDU_TYPE_ABORT:
                {
                    var apduHeaderLen = APDU.DecodeAbort(buffer, offset, out type, out var invokeId, out var reason);
                    offset += apduHeaderLen;
                    length -= apduHeaderLen;
                    ProcessAbort(adr, type, invokeId, reason, buffer, offset, length);
                }
                break;

            case BacnetPduTypes.PDU_TYPE_REJECT:
                {
                    var apduHeaderLen = APDU.DecodeReject(buffer, offset, out type, out var invokeId, out var reason);
                    offset += apduHeaderLen;
                    length -= apduHeaderLen;
                    ProcessReject(adr, type, invokeId, reason, buffer, offset, length);
                }
                break;

            case BacnetPduTypes.PDU_TYPE_CONFIRMED_SERVICE_REQUEST:
                {
                    var apduHeaderLen = APDU.DecodeConfirmedServiceRequest(buffer, offset, out type, out var service,
                        out var maxSegments, out var maxAdpu, out var invokeId, out var sequenceNumber, out var proposedWindowNumber);

                    offset += apduHeaderLen;
                    length -= apduHeaderLen;

                    if ((type & BacnetPduTypes.SEGMENTED_MESSAGE) == 0) //don't process segmented messages here
                    {
                        ProcessConfirmedServiceRequest(adr, type, service, maxSegments, maxAdpu, invokeId, buffer, offset, length);
                    }
                    else
                    {
                        ProcessSegment(adr, type, service, invokeId, maxSegments, maxAdpu, true, sequenceNumber, proposedWindowNumber, buffer, offset, length);
                    }
                }
                break;

            default:
                Log.Warn($"Something else arrived: {type}");
                break;
        }
    }

    // DAL
    public void SendNetworkMessage(BacnetAddress adr, Byte[] buffer, Int32 bufLen, BacnetNetworkMessageTypes messageType, UInt16 vendorId = 0)
    {
        adr ??= Transport.GetBroadcastAddress();
        var b = GetEncodeBuffer(Transport.HeaderLength);
        NPDU.Encode(b, BacnetNpduControls.NetworkLayerMessage, adr, null, 255, messageType, vendorId);
        b.Add(buffer, bufLen);
        Transport.Send(b.buffer, Transport.HeaderLength, b.offset - Transport.HeaderLength, adr, false, 0);
    }
    public void SendIAmRouterToNetwork(UInt16[] networks)
    {
        var b = GetEncodeBuffer(0);
        for (var i = 0; i < networks.Length; i++)
        {
            ASN1.encode_unsigned16(b, networks[i]);
        }
        SendNetworkMessage(null, b.buffer, b.offset, BacnetNetworkMessageTypes.NETWORK_MESSAGE_I_AM_ROUTER_TO_NETWORK);
    }

    public void SendInitializeRoutingTableAck(BacnetAddress adr, UInt16[] networks)
    {
        var b = GetEncodeBuffer(0);
        if (networks != null)
        {
            for (var i = 0; i < networks.Length; i++)
            {
                ASN1.encode_unsigned16(b, networks[i]);
            }
        }
        SendNetworkMessage(adr, b.buffer, b.offset, BacnetNetworkMessageTypes.NETWORK_MESSAGE_INIT_RT_TABLE_ACK);
    }
    public void SendRejectToNetwork(BacnetAddress adr, UInt16[] networks)
    {
        var b = GetEncodeBuffer(0);
        /* Sending our DNET doesn't make a lot of sense, does it? */
        for (var i = 0; i < networks.Length; i++)
        {
            ASN1.encode_unsigned16(b, networks[i]);
        }
        SendNetworkMessage(adr, b.buffer, b.offset, BacnetNetworkMessageTypes.NETWORK_MESSAGE_REJECT_MESSAGE_TO_NETWORK);
    }
    public delegate void NetworkMessageHandler(BacnetClient sender, BacnetAddress adr, BacnetNpduControls npduFunction, BacnetNetworkMessageTypes npduMessageType, Byte[] buffer, Int32 offset, Int32 messageLength);
    public event NetworkMessageHandler OnNetworkMessage;
    public delegate void WhoIsRouterToNetworkHandler(BacnetClient sender, BacnetAddress adr, BacnetNpduControls npduFunction, Byte[] buffer, Int32 offset, Int32 messageLength);
    public event WhoIsRouterToNetworkHandler OnWhoIsRouterToNetworkMessage;
    public delegate void IAmRouterToNetworkHandler(BacnetClient sender, BacnetAddress adr, BacnetNpduControls npduFunction, Byte[] buffer, Int32 offset, Int32 messageLength);
    public event IAmRouterToNetworkHandler OnIAmRouterToNetworkMessage;
    public delegate void ICouldBeRouterToNetworkHandler(BacnetClient sender, BacnetAddress adr, BacnetNpduControls npduFunction, Byte[] buffer, Int32 offset, Int32 messageLength);
    public event ICouldBeRouterToNetworkHandler OnICouldBeRouterToNetworkMessage;
    public delegate void RejectMessageToNetworkHandler(BacnetClient sender, BacnetAddress adr, BacnetNpduControls npduFunction, Byte[] buffer, Int32 offset, Int32 messageLength);
    public event RejectMessageToNetworkHandler OnRejectMessageToNetworkMessage;
    public delegate void RouterBusyToNetworkHandler(BacnetClient sender, BacnetAddress adr, BacnetNpduControls npduFunction, Byte[] buffer, Int32 offset, Int32 messageLength);
    public event RouterBusyToNetworkHandler OnRouterBusyToNetworkMessage;
    public delegate void RouterAvailableToNetworkHandler(BacnetClient sender, BacnetAddress adr, BacnetNpduControls npduFunction, Byte[] buffer, Int32 offset, Int32 messageLength);
    public event RouterAvailableToNetworkHandler OnRouterAvailableToNetworkMessage;
    public delegate void InitRtTableToNetworkHandler(BacnetClient sender, BacnetAddress adr, BacnetNpduControls npduFunction, Byte[] buffer, Int32 offset, Int32 messageLength);
    public event InitRtTableToNetworkHandler OnInitRtTableToNetworkMessage;
    public delegate void InitRtTableAckToNetworkHandler(BacnetClient sender, BacnetAddress adr, BacnetNpduControls npduFunction, Byte[] buffer, Int32 offset, Int32 messageLength);
    public event InitRtTableAckToNetworkHandler OnInitRtTableAckToNetworkMessage;
    public delegate void EstablishConnectionToNetworkHandler(BacnetClient sender, BacnetAddress adr, BacnetNpduControls npduFunction, Byte[] buffer, Int32 offset, Int32 messageLength);
    public event EstablishConnectionToNetworkHandler OnEstablishConnectionToNetworkMessage;
    public delegate void DisconnectConnectionToNetworkHandler(BacnetClient sender, BacnetAddress adr, BacnetNpduControls npduFunction, Byte[] buffer, Int32 offset, Int32 messageLength);
    public event DisconnectConnectionToNetworkHandler OnDisconnectConnectionToNetworkMessage;
    public delegate void UnrecognizedNetworkHandler(BacnetClient sender, BacnetAddress adr, BacnetNpduControls npduFunction, Byte[] buffer, Int32 offset, Int32 messageLength);
    public event UnrecognizedNetworkHandler OnUnrecognizedNetworkMessage;

    private void ProcessNetworkMessage(BacnetAddress adr, BacnetNpduControls npduFunction, BacnetNetworkMessageTypes npduMessageType, Byte[] buffer, Int32 offset, Int32 messageLength)
    {
        // DAL I don't want to make a generic router, but I do want to put in enough infrastructure
        // that I can build on it to route multiple devices in the normal bacnet way.
        OnNetworkMessage?.Invoke(this, adr, npduFunction, npduMessageType, buffer, offset, messageLength);
        switch (npduMessageType)
        {
            case BacnetNetworkMessageTypes.NETWORK_MESSAGE_WHO_IS_ROUTER_TO_NETWORK:
                OnWhoIsRouterToNetworkMessage?.Invoke(this, adr, npduFunction, buffer, offset, messageLength);
                break;
            case BacnetNetworkMessageTypes.NETWORK_MESSAGE_I_AM_ROUTER_TO_NETWORK:
                OnIAmRouterToNetworkMessage?.Invoke(this, adr, npduFunction, buffer, offset, messageLength);
                break;
            case BacnetNetworkMessageTypes.NETWORK_MESSAGE_I_COULD_BE_ROUTER_TO_NETWORK:
                OnICouldBeRouterToNetworkMessage?.Invoke(this, adr, npduFunction, buffer, offset, messageLength);
                break;
            case BacnetNetworkMessageTypes.NETWORK_MESSAGE_REJECT_MESSAGE_TO_NETWORK:
                OnRejectMessageToNetworkMessage?.Invoke(this, adr, npduFunction, buffer, offset, messageLength);
                break;
            case BacnetNetworkMessageTypes.NETWORK_MESSAGE_ROUTER_BUSY_TO_NETWORK:
                OnRouterBusyToNetworkMessage?.Invoke(this, adr, npduFunction, buffer, offset, messageLength);
                break;
            case BacnetNetworkMessageTypes.NETWORK_MESSAGE_ROUTER_AVAILABLE_TO_NETWORK:
                OnRouterAvailableToNetworkMessage?.Invoke(this, adr, npduFunction, buffer, offset, messageLength);
                break;
            case BacnetNetworkMessageTypes.NETWORK_MESSAGE_INIT_RT_TABLE:
                OnInitRtTableToNetworkMessage?.Invoke(this, adr, npduFunction, buffer, offset, messageLength);
                break;
            case BacnetNetworkMessageTypes.NETWORK_MESSAGE_INIT_RT_TABLE_ACK:
                OnInitRtTableAckToNetworkMessage?.Invoke(this, adr, npduFunction, buffer, offset, messageLength);
                break;
            case BacnetNetworkMessageTypes.NETWORK_MESSAGE_ESTABLISH_CONNECTION_TO_NETWORK:
                OnEstablishConnectionToNetworkMessage?.Invoke(this, adr, npduFunction, buffer, offset, messageLength);
                break;
            case BacnetNetworkMessageTypes.NETWORK_MESSAGE_DISCONNECT_CONNECTION_TO_NETWORK:
                OnDisconnectConnectionToNetworkMessage?.Invoke(this, adr, npduFunction, buffer, offset, messageLength);
                break;
            default:
                /* An unrecognized message is bad; send an error response. */
                OnUnrecognizedNetworkMessage?.Invoke(this, adr, npduFunction, buffer, offset, messageLength);
                break;
        }
    }

    private void OnRecieve(IBacnetTransport sender, Byte[] buffer, Int32 offset, Int32 msgLength, BacnetAddress remoteAddress)
    {
        try
        {
            if (Transport == null)
                return; //we're disposed 

            if (msgLength <= 0)
                return;

            // parse
            var npduLen = NPDU.Decode(buffer, offset, out var npduFunction, out var destination, out var source, out _, out var npduMessageType, out _);

            // Modif FC
            remoteAddress.RoutedSource = source;

            // DAL
            remoteAddress.RoutedDestination = destination;

            if (npduLen <= 0)
                return;

            offset += npduLen;
            msgLength -= npduLen;

            if (msgLength < 0) // could be 0 for an already parsed
                return;

            if (npduFunction.HasFlag(BacnetNpduControls.NetworkLayerMessage))
            {
                Log.Info("Network Layer message received");
                // DAL
                ProcessNetworkMessage(remoteAddress, npduFunction, npduMessageType, buffer, offset, msgLength);
                return;
            }

            if (msgLength <= 0)
                return;

            var apduType = APDU.GetDecodedType(buffer, offset);
            ProcessApdu(remoteAddress, apduType, buffer, offset, msgLength);
        }
        catch (Exception ex)
        {
            Log.Error("Error in OnRecieve", ex);
        }
    }

    // Modif FC
    public void RegisterAsForeignDevice(String bbmdIp, Int16 ttl, Int32 port = DEFAULT_UDP_PORT)
    {
        try
        {
            var ep = new IPEndPoint(IPAddress.Parse(bbmdIp), port);
            var sent = false;

            switch (Transport)
            {
                case BacnetIpUdpProtocolTransport t:
                    sent = t.SendRegisterAsForeignDevice(ep, ttl);
                    break;

                case BacnetIpV6UdpProtocolTransport t:
                    sent = t.SendRegisterAsForeignDevice(ep, ttl);
                    break;
            }

            if (sent)
                Log.Debug($"Sending Register as a Foreign Device to {bbmdIp}");
            else
                Log.Warn("The given address do not match with the IP version");
        }
        catch (Exception ex)
        {
            Log.Error("Error on RegisterAsForeignDevice (Wrong Transport, not IP ?)", ex);
        }
    }

    public void RemoteWhoIs(String bbmdIP, Int32 port = DEFAULT_UDP_PORT, Int32 lowLimit = -1, Int32 highLimit = -1, BacnetAddress source = null)
    {
        try
        {
            var ep = new IPEndPoint(IPAddress.Parse(bbmdIP), port);

            var b = GetEncodeBuffer(Transport.HeaderLength);
            var broadcast = Transport.GetBroadcastAddress();
            NPDU.Encode(b, BacnetNpduControls.PriorityNormalMessage, broadcast, source);
            APDU.EncodeUnconfirmedServiceRequest(b, BacnetPduTypes.PDU_TYPE_UNCONFIRMED_SERVICE_REQUEST, BacnetUnconfirmedServices.SERVICE_UNCONFIRMED_WHO_IS);
            Services.EncodeWhoIsBroadcast(b, lowLimit, highLimit);

            var sent = false;

            switch (Transport)
            {
                case BacnetIpUdpProtocolTransport t:
                    sent = t.SendRemoteWhois(b.buffer, ep, b.offset);
                    break;

                case BacnetIpV6UdpProtocolTransport t:
                    sent = t.SendRemoteWhois(b.buffer, ep, b.offset);
                    break;
            }

            if (sent)
                Log.Debug($"Sending Remote Whois to {bbmdIP}");
            else
                Log.Warn("The given address do not match with the IP version");
        }
        catch (Exception ex)
        {
            Log.Error("Error on Sending Whois to remote BBMD (Wrong Transport, not IP ?)", ex);
        }

    }

    public void WhoIs(Int32 lowLimit = -1, Int32 highLimit = -1, BacnetAddress receiver = null, BacnetAddress source = null)
    {
        if (receiver == null)
        {
            // _receiver could be an unicast @ : for direct acces 
            // usefull on BIP for a known IP:Port, unknown device Id
            receiver = Transport.GetBroadcastAddress();
            Log.Debug("Broadcasting WhoIs");
        }
        else
        {
            Log.Debug($"Sending WhoIs to {receiver}");
        }

        var b = GetEncodeBuffer(Transport.HeaderLength);
        NPDU.Encode(b, BacnetNpduControls.PriorityNormalMessage, receiver, source);
        APDU.EncodeUnconfirmedServiceRequest(b, BacnetPduTypes.PDU_TYPE_UNCONFIRMED_SERVICE_REQUEST, BacnetUnconfirmedServices.SERVICE_UNCONFIRMED_WHO_IS);
        Services.EncodeWhoIsBroadcast(b, lowLimit, highLimit);

        Transport.Send(b.buffer, Transport.HeaderLength, b.offset - Transport.HeaderLength, receiver, false, 0);
    }

    public void Iam(UInt32 deviceId, BacnetSegmentations segmentation = BacnetSegmentations.SEGMENTATION_BOTH, BacnetAddress receiver = null, BacnetAddress source = null)
    {
        if (receiver == null)
        {
            receiver = Transport.GetBroadcastAddress();
            Log.Debug($"Broadcasting Iam {deviceId}");
        }
        else
        {
            Log.Debug($"Sending Iam {deviceId} to {receiver}");
        }

        var b = GetEncodeBuffer(Transport.HeaderLength);
        NPDU.Encode(b, BacnetNpduControls.PriorityNormalMessage, receiver, source);
        APDU.EncodeUnconfirmedServiceRequest(b, BacnetPduTypes.PDU_TYPE_UNCONFIRMED_SERVICE_REQUEST, BacnetUnconfirmedServices.SERVICE_UNCONFIRMED_I_AM);
        Services.EncodeIamBroadcast(b, deviceId, (UInt32)GetMaxApdu(), segmentation, VendorId);

        Transport.Send(b.buffer, Transport.HeaderLength, b.offset - Transport.HeaderLength, receiver, false, 0);
    }

    public void WhoHas(BacnetObjectId objId, Int32 lowLimit = -1, Int32 highLimit = -1, BacnetAddress receiver = null, BacnetAddress source = null) => WhoHasCore(objId, null, lowLimit, highLimit, receiver, source);

    public void WhoHas(String objName, Int32 lowLimit = -1, Int32 highLimit = -1, BacnetAddress receiver = null, BacnetAddress source = null) => WhoHasCore(null, objName, lowLimit, highLimit, receiver, source);

    private void WhoHasCore(BacnetObjectId? objId, String objName, Int32 lowLimit, Int32 highLimit, BacnetAddress receiver, BacnetAddress source)
    {
        if (receiver == null)
        {
            receiver = Transport.GetBroadcastAddress();
            Log.Debug($"Broadcasting WhoHas {objId?.ToString() ?? objName}");
        }
        else
        {
            Log.Debug($"Sending WhoHas {objId?.ToString() ?? objName} to {receiver}");
        }

        var b = GetEncodeBuffer(Transport.HeaderLength);
        NPDU.Encode(b, BacnetNpduControls.PriorityNormalMessage, receiver, source);
        APDU.EncodeUnconfirmedServiceRequest(b, BacnetPduTypes.PDU_TYPE_UNCONFIRMED_SERVICE_REQUEST, BacnetUnconfirmedServices.SERVICE_UNCONFIRMED_WHO_HAS);
        Services.EncodeWhoHasBroadcast(b, lowLimit, highLimit, objId, objName);

        Transport.Send(b.buffer, Transport.HeaderLength, b.offset - Transport.HeaderLength, receiver, false, 0);
    }

    // ReSharper disable once InconsistentNaming
    public void IHave(BacnetObjectId deviceId, BacnetObjectId objId, String objName, BacnetAddress source = null)
    {
        Log.Debug($"Broadcasting IHave {objName} {objId}");

        var b = GetEncodeBuffer(Transport.HeaderLength);
        var broadcast = Transport.GetBroadcastAddress();
        NPDU.Encode(b, BacnetNpduControls.PriorityNormalMessage, broadcast, source);
        APDU.EncodeUnconfirmedServiceRequest(b, BacnetPduTypes.PDU_TYPE_UNCONFIRMED_SERVICE_REQUEST, BacnetUnconfirmedServices.SERVICE_UNCONFIRMED_I_HAVE);
        Services.EncodeIhaveBroadcast(b, deviceId, objId, objName);

        Transport.Send(b.buffer, Transport.HeaderLength, b.offset - Transport.HeaderLength, broadcast, false, 0);
    }

    public void SendUnconfirmedEventNotification(BacnetAddress adr, BacnetEventNotificationData eventData, BacnetAddress source = null)
    {
        Log.Debug($"Sending Event Notification {eventData.eventType} {eventData.eventObjectIdentifier}");

        var b = GetEncodeBuffer(Transport.HeaderLength);
        NPDU.Encode(b, BacnetNpduControls.PriorityNormalMessage, adr, source);
        APDU.EncodeUnconfirmedServiceRequest(b, BacnetPduTypes.PDU_TYPE_UNCONFIRMED_SERVICE_REQUEST, BacnetUnconfirmedServices.SERVICE_UNCONFIRMED_EVENT_NOTIFICATION);
        Services.EncodeEventNotifyUnconfirmed(b, eventData);
        Transport.Send(b.buffer, Transport.HeaderLength, b.offset - Transport.HeaderLength, adr, false, 0);
    }

    public void SendConfirmedServiceReject(BacnetAddress adr, Byte invokeId, BacnetRejectReason reason)
    {
        Log.Debug($"Sending Service reject: {reason}");

        var b = GetEncodeBuffer(Transport.HeaderLength);

        NPDU.Encode(b, BacnetNpduControls.PriorityNormalMessage, adr.RoutedSource, adr.RoutedDestination);
        APDU.EncodeError(b, BacnetPduTypes.PDU_TYPE_REJECT, (BacnetConfirmedServices)reason, invokeId);
        Transport.Send(b.buffer, Transport.HeaderLength, b.offset - Transport.HeaderLength, adr, false, 0);
    }

    public void SendAbort(BacnetAddress adr, Byte invokeId, BacnetAbortReason reason)
    {
        // DAL
        Log.Debug($"Sending Service reject: {reason}");

        var b = GetEncodeBuffer(Transport.HeaderLength);

        NPDU.Encode(b, BacnetNpduControls.PriorityNormalMessage, adr.RoutedSource, adr.RoutedDestination);
        APDU.EncodeError(b, BacnetPduTypes.PDU_TYPE_ABORT, (BacnetConfirmedServices)reason, invokeId);
        Transport.Send(b.buffer, Transport.HeaderLength, b.offset - Transport.HeaderLength, adr, false, 0);
    }

    public void SynchronizeTime(BacnetAddress adr, DateTime dateTime, BacnetAddress source = null)
    {
        Log.Debug($"Sending Time Synchronize: {dateTime} {dateTime.Kind.ToString().ToUpper()}");

        var buffer = GetEncodeBuffer(Transport.HeaderLength);
        NPDU.Encode(buffer, BacnetNpduControls.PriorityNormalMessage, adr, source);
        APDU.EncodeUnconfirmedServiceRequest(buffer, BacnetPduTypes.PDU_TYPE_UNCONFIRMED_SERVICE_REQUEST, dateTime.Kind == DateTimeKind.Utc
                ? BacnetUnconfirmedServices.SERVICE_UNCONFIRMED_UTC_TIME_SYNCHRONIZATION
                : BacnetUnconfirmedServices.SERVICE_UNCONFIRMED_TIME_SYNCHRONIZATION);
        Services.EncodeTimeSync(buffer, dateTime);
        Transport.Send(buffer.buffer, Transport.HeaderLength, buffer.offset - Transport.HeaderLength, adr, false, 0);
    }


    public Int32 GetMaxApdu() => GetMaxApdu(Transport.MaxAdpuLength);
    // DAL
    public Int32 GetMaxApdu(BacnetMaxAdpu apduLength)
    {
        var maxAPDU = apduLength switch
        {
            BacnetMaxAdpu.MAX_APDU1476 => 1476,
            BacnetMaxAdpu.MAX_APDU1024 => 1024,
            BacnetMaxAdpu.MAX_APDU480 => 480,
            BacnetMaxAdpu.MAX_APDU206 => 206,
            BacnetMaxAdpu.MAX_APDU128 => 128,
            BacnetMaxAdpu.MAX_APDU50 => 50,
            _ => throw new NotImplementedException(),
        };

        //max udp payload IRL seems to differ from the expectations in BACnet
        //so we have to adjust it. (In order to fulfill the standard)
        const Int32 maxNPDUHeaderLength = 4;       //usually it's '2', but it can also be more than '4'. Beware!
        return Math.Min(maxAPDU, Transport.MaxBufferLength - Transport.HeaderLength - maxNPDUHeaderLength);
    }

    public Int32 GetFileBufferMaxSize() =>
        //6 should be the max_apdu_header_length for Confirmed (with segmentation)
        //12 should be the max_atomic_write_file
        GetMaxApdu() - 18;

    public Boolean WriteFileRequest(BacnetAddress adr, BacnetObjectId objectId, ref Int32 position, Int32 count, Byte[] fileBuffer, Byte invokeId = 0)
    {
        using (var result = (BacnetAsyncResult)BeginWriteFileRequest(adr, objectId, position, count, fileBuffer, true, invokeId))
        {
            for (var r = 0; r < _retries; r++)
            {
                if (result.WaitForDone(Timeout))
                {
                    EndWriteFileRequest(result, out position, out var ex);
                    return ex != null ? throw ex : true;
                }
                if (r < Retries - 1)
                    result.Resend();
            }
        }
        return false;
    }

    public IAsyncResult BeginWriteFileRequest(BacnetAddress adr, BacnetObjectId objectId, Int32 position, Int32 count, Byte[] fileBuffer, Boolean waitForTransmit, Byte invokeId = 0)
    {
        Log.Debug("Sending AtomicWriteFileRequest");
        if (invokeId == 0)
            invokeId = unchecked(_invokeId++);

        var buffer = GetEncodeBuffer(Transport.HeaderLength);
        NPDU.Encode(buffer, BacnetNpduControls.PriorityNormalMessage | BacnetNpduControls.ExpectingReply, adr.RoutedSource, adr.RoutedDestination);
        APDU.EncodeConfirmedServiceRequest(buffer, PduConfirmedServiceRequest(), BacnetConfirmedServices.SERVICE_CONFIRMED_ATOMIC_WRITE_FILE, MaxSegments, Transport.MaxAdpuLength, invokeId);
        Services.EncodeAtomicWriteFile(buffer, true, objectId, position, 1, new[] { fileBuffer }, new[] { count });

        //send
        var ret = new BacnetAsyncResult(this, adr, invokeId, buffer.buffer, buffer.offset - Transport.HeaderLength, waitForTransmit, TransmitTimeout);
        ret.Resend();

        return ret;
    }

    public void EndWriteFileRequest(IAsyncResult result, out Int32 position, out Exception ex)
    {
        var res = (BacnetAsyncResult)result;
        ex = res.Error;
        if (ex == null && !res.WaitForDone(Timeout))
            ex = new Exception("Wait Timeout");

        if (ex == null)
        {
            //decode
            if (Services.DecodeAtomicWriteFileAcknowledge(res.Result, 0, res.Result.Length, out _, out position) < 0)
                ex = new Exception("Decode");
        }
        else
        {
            position = -1;
        }

        res.Dispose();
    }

    public IAsyncResult BeginReadFileRequest(BacnetAddress adr, BacnetObjectId objectId, Int32 position, UInt32 count, Boolean waitForTransmit, Byte invokeId = 0)
    {
        Log.Debug("Sending AtomicReadFileRequest");
        if (invokeId == 0)
            invokeId = unchecked(_invokeId++);

        //encode
        var buffer = GetEncodeBuffer(Transport.HeaderLength);
        NPDU.Encode(buffer, BacnetNpduControls.PriorityNormalMessage | BacnetNpduControls.ExpectingReply, adr.RoutedSource, adr.RoutedDestination);
        APDU.EncodeConfirmedServiceRequest(buffer, PduConfirmedServiceRequest(), BacnetConfirmedServices.SERVICE_CONFIRMED_ATOMIC_READ_FILE, MaxSegments, Transport.MaxAdpuLength, invokeId);
        Services.EncodeAtomicReadFile(buffer, true, objectId, position, count);

        //send
        var ret = new BacnetAsyncResult(this, adr, invokeId, buffer.buffer, buffer.offset - Transport.HeaderLength, waitForTransmit, TransmitTimeout);
        ret.Resend();

        return ret;
    }

    public void EndReadFileRequest(IAsyncResult result, out UInt32 count, out Int32 position, out Boolean endOfFile, out Byte[] fileBuffer, out Int32 fileBufferOffset, out Exception ex)
    {
        var res = (BacnetAsyncResult)result;
        ex = res.Error;
        if (ex == null && !res.WaitForDone(Timeout))
            ex = new Exception("Wait Timeout");

        if (ex == null)
        {
            //decode
            if (Services.DecodeAtomicReadFileAcknowledge(res.Result, 0, res.Result.Length, out endOfFile, out _, out position, out count, out fileBuffer, out fileBufferOffset) < 0)
                ex = new Exception("Decode");
        }
        else
        {
            count = 0;
            endOfFile = true;
            position = -1;
            fileBufferOffset = -1;
            fileBuffer = new Byte[0];
        }

        res.Dispose();
    }

    public Boolean ReadFileRequest(BacnetAddress adr, BacnetObjectId objectId, ref Int32 position, ref UInt32 count, out Boolean endOfFile, out Byte[] fileBuffer, out Int32 fileBufferOffset, Byte invokeId = 0)
    {
        using (var result = (BacnetAsyncResult)BeginReadFileRequest(adr, objectId, position, count, true, invokeId))
        {
            for (var r = 0; r < _retries; r++)
            {
                if (result.WaitForDone(Timeout))
                {
                    EndReadFileRequest(result, out count, out position, out endOfFile, out fileBuffer, out fileBufferOffset, out var ex);
                    return ex != null ? throw ex : true;
                }
                if (r < Retries - 1)
                    result.Resend();
            }
        }
        position = -1;
        count = 0;
        fileBuffer = null;
        endOfFile = true;
        fileBufferOffset = -1;
        return false;
    }

    // Read range by postion
    public IAsyncResult BeginReadRangeRequest(BacnetAddress adr, BacnetObjectId objectId, UInt32 idxBegin, UInt32 quantity, Boolean waitForTransmit, Byte invokeId = 0) => BeginReadRangeRequestCore(adr, objectId, BacnetReadRangeRequestTypes.RR_BY_POSITION, DateTime.Now, idxBegin, quantity, waitForTransmit, invokeId);

    // Read range by start time
    public IAsyncResult BeginReadRangeRequest(BacnetAddress adr, BacnetObjectId objectId, DateTime readFrom, UInt32 quantity, Boolean waitForTransmit, Byte invokeId = 0) => BeginReadRangeRequestCore(adr, objectId, BacnetReadRangeRequestTypes.RR_BY_TIME, readFrom, 1, quantity, waitForTransmit, invokeId);

    private IAsyncResult BeginReadRangeRequestCore(BacnetAddress adr, BacnetObjectId objectId, BacnetReadRangeRequestTypes bacnetReadRangeRequestTypes, DateTime readFrom, UInt32 idxBegin, UInt32 quantity, Boolean waitForTransmit, Byte invokeId = 0)
    {
        Log.Debug("Sending ReadRangeRequest");
        if (invokeId == 0)
            invokeId = unchecked(_invokeId++);

        //encode
        var buffer = GetEncodeBuffer(Transport.HeaderLength);
        NPDU.Encode(buffer, BacnetNpduControls.PriorityNormalMessage | BacnetNpduControls.ExpectingReply, adr.RoutedSource, adr.RoutedDestination);
        APDU.EncodeConfirmedServiceRequest(buffer, PduConfirmedServiceRequest(), BacnetConfirmedServices.SERVICE_CONFIRMED_READ_RANGE, MaxSegments, Transport.MaxAdpuLength, invokeId);
        Services.EncodeReadRange(buffer, objectId, (UInt32)BacnetPropertyIds.PROP_LOG_BUFFER, ASN1.BACNET_ARRAY_ALL, bacnetReadRangeRequestTypes, idxBegin, readFrom, (Int32)quantity);
        //send
        var ret = new BacnetAsyncResult(this, adr, invokeId, buffer.buffer, buffer.offset - Transport.HeaderLength, waitForTransmit, TransmitTimeout);
        ret.Resend();

        return ret;
    }

    // Fc
    public void EndReadRangeRequest(IAsyncResult result, out Byte[] trendbuffer, out UInt32 itemCount, out Exception ex)
    {
        var res = (BacnetAsyncResult)result;
        itemCount = 0;
        trendbuffer = null;

        ex = res.Error;
        if (ex == null && !res.WaitForDone(40 * 1000))
            ex = new Exception("Wait Timeout");

        if (ex == null)
        {
            itemCount = Services.DecodeReadRangeAcknowledge(res.Result, 0, res.Result.Length, out trendbuffer);
            if (itemCount == 0)
                ex = new Exception("Decode");
        }

        res.Dispose();
    }

    // Fc
    public Boolean ReadRangeRequest(BacnetAddress adr, BacnetObjectId objectId, DateTime readFrom, ref UInt32 quantity, out Byte[] range, Byte invokeId = 0) => ReadRangeRequestCore(BacnetReadRangeRequestTypes.RR_BY_TIME, adr, objectId, 1, readFrom, ref quantity, out range, invokeId);
    public Boolean ReadRangeRequest(BacnetAddress adr, BacnetObjectId objectId, UInt32 idxBegin, ref UInt32 quantity, out Byte[] range, Byte invokeId = 0) => ReadRangeRequestCore(BacnetReadRangeRequestTypes.RR_BY_POSITION, adr, objectId, idxBegin, DateTime.Now, ref quantity, out range, invokeId);

    private Boolean ReadRangeRequestCore(BacnetReadRangeRequestTypes requestType, BacnetAddress adr, BacnetObjectId objectId, UInt32 idxBegin, DateTime readFrom, ref UInt32 quantity, out Byte[] range, Byte invokeId = 0)
    {
        var quantityCopy = quantity;
        Func<IAsyncResult> getResult = requestType switch
        {
            BacnetReadRangeRequestTypes.RR_BY_TIME => () => BeginReadRangeRequest(adr, objectId, readFrom, quantityCopy, true, invokeId),
            BacnetReadRangeRequestTypes.RR_BY_POSITION => () => BeginReadRangeRequest(adr, objectId, idxBegin, quantityCopy, true, invokeId),
            _ => throw new NotImplementedException($"BacnetReadRangeRequestTypes-Type {requestType} not supported in {nameof(ReadRangeRequestCore)}!"),
        };
        range = null;
        using (var result = getResult() as BacnetAsyncResult)
        {
            for (var r = 0; r < _retries; r++)
            {
                if (result.WaitForDone(Timeout))
                {
                    EndReadRangeRequest(result, out range, out quantity, out var ex); // quantity read could be less than demanded
                    return ex != null ? throw ex : true;
                }
                if (r < Retries - 1)
                    result.Resend();
            }
        }
        return false;
    }

    public Boolean SubscribeCOVRequest(BacnetAddress adr, BacnetObjectId objectId, UInt32 subscribeId, Boolean cancel, Boolean issueConfirmedNotifications, UInt32 lifetime, Byte invokeId = 0)
    {
        using (var result = (BacnetAsyncResult)BeginSubscribeCOVRequest(adr, objectId, subscribeId, cancel, issueConfirmedNotifications, lifetime, true, invokeId))
        {
            for (var r = 0; r < _retries; r++)
            {
                if (result.WaitForDone(Timeout))
                {
                    EndSubscribeCOVRequest(result, out var ex);
                    return ex != null ? throw ex : true;
                }
                if (r < Retries - 1)
                    result.Resend();
            }
        }
        return false;
    }

    public IAsyncResult BeginSubscribeCOVRequest(BacnetAddress adr, BacnetObjectId objectId, UInt32 subscribeId, Boolean cancel, Boolean issueConfirmedNotifications, UInt32 lifetime, Boolean waitForTransmit, Byte invokeId = 0)
    {
        Log.Debug($"Sending SubscribeCOVRequest {objectId}");
        if (invokeId == 0)
            invokeId = unchecked(_invokeId++);

        var buffer = GetEncodeBuffer(Transport.HeaderLength);
        NPDU.Encode(buffer, BacnetNpduControls.PriorityNormalMessage | BacnetNpduControls.ExpectingReply, adr.RoutedSource, adr.RoutedDestination);
        APDU.EncodeConfirmedServiceRequest(buffer, PduConfirmedServiceRequest(), BacnetConfirmedServices.SERVICE_CONFIRMED_SUBSCRIBE_COV, MaxSegments, Transport.MaxAdpuLength, invokeId);
        Services.EncodeSubscribeCOV(buffer, subscribeId, objectId, cancel, issueConfirmedNotifications, lifetime);

        //send
        var ret = new BacnetAsyncResult(this, adr, invokeId, buffer.buffer, buffer.offset - Transport.HeaderLength, waitForTransmit, TransmitTimeout);
        ret.Resend();

        return ret;
    }

    public void EndSubscribeCOVRequest(IAsyncResult result, out Exception ex)
    {
        var res = (BacnetAsyncResult)result;
        ex = res.Error;
        if (ex == null && !res.WaitForDone(Timeout))
            ex = new Exception("Wait Timeout");

        res.Dispose();
    }

    // DAL
    public Boolean SendConfirmedEventNotificationRequest(BacnetAddress adr, BacnetEventNotificationData eventData, Byte invokeId = 0, BacnetAddress source = null)
    {
        using (var result = (BacnetAsyncResult)BeginSendConfirmedEventNotificationRequest(adr, eventData, true, invokeId, source))
        {
            for (var r = 0; r < _retries; r++)
            {
                if (result.WaitForDone(Timeout))
                {
                    EndSendConfirmedEventNotificationRequest(result, out var ex);
                    return ex != null ? throw ex : true;
                }
                if (r < Retries - 1)
                    result.Resend();
            }
        }
        return false;
    }

    // DAL
    public IAsyncResult BeginSendConfirmedEventNotificationRequest(BacnetAddress adr, BacnetEventNotificationData eventData, Boolean waitForTransmit, Byte invokeId = 0, BacnetAddress source = null)
    {
        Log.Debug($"Sending Confirmed Event Notification {eventData.eventType} {eventData.eventObjectIdentifier}");
        if (invokeId == 0)
            invokeId = unchecked(_invokeId++);

        var buffer = GetEncodeBuffer(Transport.HeaderLength);
        NPDU.Encode(buffer, BacnetNpduControls.PriorityNormalMessage | BacnetNpduControls.ExpectingReply, adr, source);
        APDU.EncodeConfirmedServiceRequest(buffer, PduConfirmedServiceRequest(), BacnetConfirmedServices.SERVICE_CONFIRMED_EVENT_NOTIFICATION, MaxSegments, Transport.MaxAdpuLength, invokeId);
        Services.EncodeEventNotifyConfirmed(buffer, eventData);

        //send
        var ret = new BacnetAsyncResult(this, adr, invokeId, buffer.buffer, buffer.offset - Transport.HeaderLength, waitForTransmit, TransmitTimeout);
        ret.Resend();

        return ret;
    }

    // DAL
    public void EndSendConfirmedEventNotificationRequest(IAsyncResult result, out Exception ex)
    {
        var res = (BacnetAsyncResult)result;
        ex = res.Error;
        if (ex == null && !res.WaitForDone(Timeout))
            ex = new Exception("Wait Timeout");

        res.Dispose();
    }

    public Boolean SubscribePropertyRequest(BacnetAddress adr, BacnetObjectId objectId, BacnetPropertyReference monitoredProperty, UInt32 subscribeId, Boolean cancel, Boolean issueConfirmedNotifications, Byte invokeId = 0)
    {
        using (var result = (BacnetAsyncResult)BeginSubscribePropertyRequest(adr, objectId, monitoredProperty, subscribeId, cancel, issueConfirmedNotifications, true, invokeId))
        {
            for (var r = 0; r < _retries; r++)
            {
                if (result.WaitForDone(Timeout))
                {
                    EndSubscribePropertyRequest(result, out var ex);
                    return ex != null ? throw ex : true;
                }
                if (r < Retries - 1)
                    result.Resend();
            }
        }
        return false;
    }

    public IAsyncResult BeginSubscribePropertyRequest(BacnetAddress adr, BacnetObjectId objectId, BacnetPropertyReference monitoredProperty, UInt32 subscribeId, Boolean cancel, Boolean issueConfirmedNotifications, Boolean waitForTransmit, Byte invokeId = 0)
    {
        Log.Debug($"Sending SubscribePropertyRequest {objectId}.{monitoredProperty}");
        if (invokeId == 0)
            invokeId = unchecked(_invokeId++);

        var buffer = GetEncodeBuffer(Transport.HeaderLength);
        NPDU.Encode(buffer, BacnetNpduControls.PriorityNormalMessage | BacnetNpduControls.ExpectingReply, adr.RoutedSource, adr.RoutedDestination);
        APDU.EncodeConfirmedServiceRequest(buffer, PduConfirmedServiceRequest(), BacnetConfirmedServices.SERVICE_CONFIRMED_SUBSCRIBE_COV_PROPERTY, MaxSegments, Transport.MaxAdpuLength, invokeId);
        Services.EncodeSubscribeProperty(buffer, subscribeId, objectId, cancel, issueConfirmedNotifications, 0, monitoredProperty, false, 0f);

        //send
        var ret = new BacnetAsyncResult(this, adr, invokeId, buffer.buffer, buffer.offset - Transport.HeaderLength, waitForTransmit, TransmitTimeout);
        ret.Resend();

        return ret;
    }

    public void EndSubscribePropertyRequest(IAsyncResult result, out Exception ex)
    {
        var res = (BacnetAsyncResult)result;
        ex = res.Error;
        if (ex == null && !res.WaitForDone(Timeout))
            ex = new Exception("Wait Timeout");

        res.Dispose();
    }

    public Boolean ReadPropertyRequest(BacnetAddress adr, BacnetObjectId objectId, BacnetPropertyIds propertyId, out IList<BacnetValue> valueList, Byte invokeId = 0, UInt32 arrayIndex = ASN1.BACNET_ARRAY_ALL)
    {
        using (var result = (BacnetAsyncResult)BeginReadPropertyRequest(adr, objectId, propertyId, true, invokeId, arrayIndex))
        {
            for (var r = 0; r < _retries; r++)
            {
                if (result.WaitForDone(Timeout))
                {
                    EndReadPropertyRequest(result, out valueList, out var ex);
                    return ex != null ? throw ex : true;
                }
                if (r < Retries - 1)
                    result.Resend();
            }
        }
        valueList = null;
        return false;
    }

    public Task<IList<BacnetValue>> ReadPropertyAsync(BacnetAddress address, BacnetObjectTypes objType, UInt32 objInstance,
        BacnetPropertyIds propertyId, Byte invokeId = 0, UInt32 arrayIndex = ASN1.BACNET_ARRAY_ALL)
    {
        var objectId = new BacnetObjectId(objType, objInstance);
        return ReadPropertyAsync(address, objectId, propertyId, invokeId, arrayIndex);
    }

    public Task<IList<BacnetValue>> ReadPropertyAsync(BacnetAddress address, BacnetObjectId objectId,
        BacnetPropertyIds propertyId, Byte invokeId = 0, UInt32 arrayIndex = ASN1.BACNET_ARRAY_ALL)
    {
        return Task<IList<BacnetValue>>.Factory.StartNew(() =>
        {
            return !ReadPropertyRequest(address, objectId, propertyId, out var result, invokeId, arrayIndex)
                ? throw new Exception($"Failed to read property {propertyId} of {objectId} from {address}")
                : result;
        });
    }

    public IAsyncResult BeginReadPropertyRequest(BacnetAddress address, BacnetObjectId objectId, BacnetPropertyIds propertyId, Boolean waitForTransmit, Byte invokeId = 0, UInt32 arrayIndex = ASN1.BACNET_ARRAY_ALL)
    {
        Log.Debug($"Sending ReadPropertyRequest {objectId} {propertyId}");
        if (invokeId == 0)
            invokeId = unchecked(_invokeId++);

        var buffer = GetEncodeBuffer(Transport.HeaderLength);
        NPDU.Encode(buffer, BacnetNpduControls.PriorityNormalMessage | BacnetNpduControls.ExpectingReply, address.RoutedSource, address.RoutedDestination);
        APDU.EncodeConfirmedServiceRequest(buffer, PduConfirmedServiceRequest(), BacnetConfirmedServices.SERVICE_CONFIRMED_READ_PROPERTY, MaxSegments, Transport.MaxAdpuLength, invokeId);
        Services.EncodeReadProperty(buffer, objectId, (UInt32)propertyId, arrayIndex);

        //send
        var ret = new BacnetAsyncResult(this, address, invokeId, buffer.buffer, buffer.offset - Transport.HeaderLength, waitForTransmit, TransmitTimeout);
        ret.Resend();

        return ret;
    }

    public void EndReadPropertyRequest(IAsyncResult result, out IList<BacnetValue> valueList, out Exception ex)
    {
        var res = (BacnetAsyncResult)result;
        ex = res.Error;
        if (ex == null && !res.WaitForDone(Timeout))
            ex = new Exception("Wait Timeout");

        if (ex == null)
        {
            //decode
            if (Services.DecodeReadPropertyAcknowledge(res.Address, res.Result, 0, res.Result.Length, out _, out _, out valueList) < 0)
                ex = new Exception("Decode");
        }
        else
        {
            valueList = null;
        }

        res.Dispose();
    }

    public Boolean WritePropertyRequest(BacnetAddress adr, BacnetObjectId objectId, BacnetPropertyIds propertyId, IEnumerable<BacnetValue> valueList, Byte invokeId = 0)
    {
        using (var result = (BacnetAsyncResult)BeginWritePropertyRequest(adr, objectId, propertyId, valueList, true, invokeId))
        {
            for (var r = 0; r < _retries; r++)
            {
                if (result.WaitForDone(Timeout))
                {
                    EndWritePropertyRequest(result, out var ex);
                    return ex != null ? throw ex : true;
                }
                if (r < Retries - 1)
                    result.Resend();
            }
        }
        return false;
    }

    public Boolean WritePropertyMultipleRequest(BacnetAddress adr, BacnetObjectId objectId, ICollection<BacnetPropertyValue> valueList, Byte invokeId = 0)
    {
        using (var result = (BacnetAsyncResult)BeginWritePropertyMultipleRequest(adr, objectId, valueList, true, invokeId))
        {
            for (var r = 0; r < _retries; r++)
            {
                if (result.WaitForDone(Timeout))
                {
                    EndWritePropertyRequest(result, out var ex); // Share the same with single write
                    return ex != null ? throw ex : true;
                }
                if (r < Retries - 1)
                    result.Resend();
            }
        }
        return false;
    }

    public IAsyncResult BeginWritePropertyRequest(BacnetAddress adr, BacnetObjectId objectId, BacnetPropertyIds propertyId, IEnumerable<BacnetValue> valueList, Boolean waitForTransmit, Byte invokeId = 0)
    {
        Log.Debug($"Sending WritePropertyRequest {objectId} {propertyId}");
        if (invokeId == 0)
            invokeId = unchecked(_invokeId++);

        var buffer = GetEncodeBuffer(Transport.HeaderLength);
        NPDU.Encode(buffer, BacnetNpduControls.PriorityNormalMessage | BacnetNpduControls.ExpectingReply, adr.RoutedSource, adr.RoutedDestination);
        APDU.EncodeConfirmedServiceRequest(buffer, BacnetPduTypes.PDU_TYPE_CONFIRMED_SERVICE_REQUEST, BacnetConfirmedServices.SERVICE_CONFIRMED_WRITE_PROPERTY, MaxSegments, Transport.MaxAdpuLength, invokeId);
        Services.EncodeWriteProperty(buffer, objectId, (UInt32)propertyId, ASN1.BACNET_ARRAY_ALL, _writepriority, valueList);

        //send
        var ret = new BacnetAsyncResult(this, adr, invokeId, buffer.buffer, buffer.offset - Transport.HeaderLength, waitForTransmit, TransmitTimeout);
        ret.Resend();

        return ret;
    }

    public IAsyncResult BeginWritePropertyMultipleRequest(BacnetAddress adr, BacnetObjectId objectId, ICollection<BacnetPropertyValue> valueList, Boolean waitForTransmit, Byte invokeId = 0)
    {
        Log.Debug($"Sending WritePropertyMultipleRequest {objectId}");
        if (invokeId == 0) invokeId = unchecked(_invokeId++);

        var buffer = GetEncodeBuffer(Transport.HeaderLength);
        //BacnetNpduControls.PriorityNormalMessage 
        NPDU.Encode(buffer, BacnetNpduControls.PriorityNormalMessage | BacnetNpduControls.ExpectingReply, adr.RoutedSource, adr.RoutedDestination);

        APDU.EncodeConfirmedServiceRequest(buffer, BacnetPduTypes.PDU_TYPE_CONFIRMED_SERVICE_REQUEST, BacnetConfirmedServices.SERVICE_CONFIRMED_WRITE_PROP_MULTIPLE, MaxSegments, Transport.MaxAdpuLength, invokeId);
        Services.EncodeWritePropertyMultiple(buffer, objectId, valueList);

        //send
        var ret = new BacnetAsyncResult(this, adr, invokeId, buffer.buffer, buffer.offset - Transport.HeaderLength, waitForTransmit, TransmitTimeout);
        ret.Resend();

        return ret;
    }

    public void EndWritePropertyRequest(IAsyncResult result, out Exception ex)
    {
        var res = (BacnetAsyncResult)result;
        ex = res.Error;
        if (ex == null && !res.WaitForDone(Timeout))
            ex = new Exception("Wait Timeout");

        res.Dispose();
    }

    // By Chritopher Günter : Write multiple properties on multiple objects
    public Boolean WritePropertyMultipleRequest(BacnetAddress adr, ICollection<BacnetReadAccessResult> valueList, Byte invokeId = 0)
    {
        using (var result = (BacnetAsyncResult)BeginWritePropertyMultipleRequest(adr, valueList, true, invokeId))
        {
            for (var r = 0; r < _retries; r++)
            {
                if (result.WaitForDone(Timeout))
                {
                    EndWritePropertyRequest(result, out var ex); // Share the same with single write
                    return ex != null ? throw ex : true;
                }
                if (r < Retries - 1)
                    result.Resend();
            }
        }
        return false;
    }

    public IAsyncResult BeginWritePropertyMultipleRequest(BacnetAddress adr, ICollection<BacnetReadAccessResult> valueList, Boolean waitForTransmit, Byte invokeId = 0)
    {
        var objectIds = String.Join(", ", valueList.Select(v => v.objectIdentifier));
        Log.Debug($"Sending WritePropertyMultipleRequest {objectIds}");

        if (invokeId == 0)
            invokeId = unchecked(_invokeId++);

        var buffer = GetEncodeBuffer(Transport.HeaderLength);
        //BacnetNpduControls.PriorityNormalMessage 
        NPDU.Encode(buffer, BacnetNpduControls.PriorityNormalMessage | BacnetNpduControls.ExpectingReply, adr.RoutedSource, adr.RoutedDestination);

        APDU.EncodeConfirmedServiceRequest(buffer, BacnetPduTypes.PDU_TYPE_CONFIRMED_SERVICE_REQUEST, BacnetConfirmedServices.SERVICE_CONFIRMED_WRITE_PROP_MULTIPLE, MaxSegments, Transport.MaxAdpuLength, invokeId);
        Services.EncodeWriteObjectMultiple(buffer, valueList);

        //send
        var ret = new BacnetAsyncResult(this, adr, invokeId, buffer.buffer, buffer.offset - Transport.HeaderLength, waitForTransmit, TransmitTimeout);
        ret.Resend();

        return ret;
    }

    public Boolean ReadPropertyMultipleRequest(BacnetAddress address, BacnetObjectId objectId, IList<BacnetPropertyReference> propertyIdAndArrayIndex, out IList<BacnetReadAccessResult> values, Byte invokeId = 0)
    {
        using (var result = (BacnetAsyncResult)BeginReadPropertyMultipleRequest(address, objectId, propertyIdAndArrayIndex, true, invokeId))
        {
            for (var r = 0; r < _retries; r++)
            {
                if (result.WaitForDone(Timeout))
                {
                    EndReadPropertyMultipleRequest(result, out values, out var ex);
                    return ex != null ? throw ex : true;
                }
                if (r < Retries - 1)
                    result.Resend();
            }
        }
        values = null;
        return false;
    }

    public Task<IList<BacnetPropertyValue>> ReadPropertyMultipleAsync(BacnetAddress address,
        BacnetObjectTypes objType, UInt32 objInstance, params BacnetPropertyIds[] propertyIds)
    {
        var objectId = new BacnetObjectId(objType, objInstance);
        return ReadPropertyMultipleAsync(address, objectId, propertyIds);
    }

    public Task<IList<BacnetPropertyValue>> ReadPropertyMultipleAsync(BacnetAddress address,
        BacnetObjectId objectId, params BacnetPropertyIds[] propertyIds)
    {
        var propertyReferences = propertyIds.Select(p =>
            new BacnetPropertyReference((UInt32)p, ASN1.BACNET_ARRAY_ALL));

        return Task<IList<BacnetPropertyValue>>.Factory.StartNew(() =>
        {
            return !ReadPropertyMultipleRequest(address, objectId, propertyReferences.ToList(), out var result)
                ? throw new Exception($"Failed to read multiple properties of {objectId} from {address}")
                : result.Single().values;
        });
    }

    public IAsyncResult BeginReadPropertyMultipleRequest(BacnetAddress adr, BacnetObjectId objectId, IList<BacnetPropertyReference> propertyIdAndArrayIndex, Boolean waitForTransmit, Byte invokeId = 0)
    {
        var propertyIds = String.Join(", ", propertyIdAndArrayIndex.Select(v => (BacnetPropertyIds)v.propertyIdentifier));
        Log.Debug($"Sending ReadPropertyMultipleRequest {objectId} {propertyIds}");
        if (invokeId == 0)
            invokeId = unchecked(_invokeId++);

        var buffer = GetEncodeBuffer(Transport.HeaderLength);
        NPDU.Encode(buffer, BacnetNpduControls.PriorityNormalMessage | BacnetNpduControls.ExpectingReply, adr.RoutedSource, adr.RoutedDestination);
        APDU.EncodeConfirmedServiceRequest(buffer, PduConfirmedServiceRequest(), BacnetConfirmedServices.SERVICE_CONFIRMED_READ_PROP_MULTIPLE, MaxSegments, Transport.MaxAdpuLength, invokeId);
        Services.EncodeReadPropertyMultiple(buffer, objectId, propertyIdAndArrayIndex);

        //send
        var ret = new BacnetAsyncResult(this, adr, invokeId, buffer.buffer, buffer.offset - Transport.HeaderLength, waitForTransmit, TransmitTimeout);
        ret.Resend();

        return ret;
    }

    // Another way to read multiple properties on multiples objects, if supported by devices
    public Boolean ReadPropertyMultipleRequest(BacnetAddress address, IList<BacnetReadAccessSpecification> properties, out IList<BacnetReadAccessResult> values, Byte invokeId = 0)
    {
        using (var result = (BacnetAsyncResult)BeginReadPropertyMultipleRequest(address, properties, true, invokeId))
        {
            for (var r = 0; r < _retries; r++)
            {
                if (result.WaitForDone(Timeout))
                {
                    EndReadPropertyMultipleRequest(result, out values, out var ex);
                    return ex != null ? throw ex : true;
                }
                if (r < Retries - 1)
                    result.Resend();
            }
        }
        values = null;
        return false;
    }

    public IAsyncResult BeginReadPropertyMultipleRequest(BacnetAddress adr, IList<BacnetReadAccessSpecification> properties, Boolean waitForTransmit, Byte invokeId = 0)
    {
        var objectIds = String.Join(", ", properties.Select(v => v.objectIdentifier));
        Log.Debug($"Sending ReadPropertyMultipleRequest {objectIds}");
        if (invokeId == 0)
            invokeId = unchecked(_invokeId++);

        var buffer = GetEncodeBuffer(Transport.HeaderLength);
        NPDU.Encode(buffer, BacnetNpduControls.PriorityNormalMessage | BacnetNpduControls.ExpectingReply, adr.RoutedSource, adr.RoutedDestination);
        APDU.EncodeConfirmedServiceRequest(buffer, PduConfirmedServiceRequest(), BacnetConfirmedServices.SERVICE_CONFIRMED_READ_PROP_MULTIPLE, MaxSegments, Transport.MaxAdpuLength, invokeId);
        Services.EncodeReadPropertyMultiple(buffer, properties);

        //send
        var ret = new BacnetAsyncResult(this, adr, invokeId, buffer.buffer, buffer.offset - Transport.HeaderLength, waitForTransmit, TransmitTimeout);
        ret.Resend();

        return ret;
    }

    public void EndReadPropertyMultipleRequest(IAsyncResult result, out IList<BacnetReadAccessResult> values, out Exception ex)
    {
        var res = (BacnetAsyncResult)result;
        ex = res.Error;
        if (ex == null && !res.WaitForDone(Timeout))
            ex = new Exception("Wait Timeout");

        if (ex == null)
        {
            //decode
            if (Services.DecodeReadPropertyMultipleAcknowledge(res.Address, res.Result, 0, res.Result.Length, out values) < 0)
                ex = new Exception("Decode");
        }
        else
        {
            values = null;
        }

        res.Dispose();
    }

    private BacnetPduTypes PduConfirmedServiceRequest()
    {
        return MaxSegments != BacnetMaxSegments.MAX_SEG0
            ? BacnetPduTypes.PDU_TYPE_CONFIRMED_SERVICE_REQUEST | BacnetPduTypes.SEGMENTED_RESPONSE_ACCEPTED
            : BacnetPduTypes.PDU_TYPE_CONFIRMED_SERVICE_REQUEST;
    }

    // By Christopher Günter
    public Boolean CreateObjectRequest(BacnetAddress adr, BacnetObjectId objectId, ICollection<BacnetPropertyValue> valueList = null, Byte invokeId = 0)
    {
        using (var result = (BacnetAsyncResult)BeginCreateObjectRequest(adr, objectId, valueList, true, invokeId))
        {
            for (var r = 0; r < _retries; r++)
            {
                if (result.WaitForDone(Timeout))
                {
                    EndCreateObjectRequest(result, out var ex);
                    return ex != null ? throw ex : true;
                }
                if (r < Retries - 1)
                    result.Resend();
            }
        }
        return false;
    }

    public IAsyncResult BeginCreateObjectRequest(BacnetAddress adr, BacnetObjectId objectId, ICollection<BacnetPropertyValue> valueList, Boolean waitForTransmit, Byte invokeId = 0)
    {
        Log.Debug("Sending CreateObjectRequest");
        if (invokeId == 0) invokeId = unchecked(_invokeId++);

        var buffer = GetEncodeBuffer(Transport.HeaderLength);

        NPDU.Encode(buffer, BacnetNpduControls.PriorityNormalMessage | BacnetNpduControls.ExpectingReply, adr.RoutedSource, adr.RoutedDestination);
        APDU.EncodeConfirmedServiceRequest(buffer, PduConfirmedServiceRequest(), BacnetConfirmedServices.SERVICE_CONFIRMED_CREATE_OBJECT, MaxSegments, Transport.MaxAdpuLength, invokeId);
        Services.EncodeCreateProperty(buffer, objectId, valueList);

        //send
        var ret = new BacnetAsyncResult(this, adr, invokeId, buffer.buffer, buffer.offset - Transport.HeaderLength, waitForTransmit, TransmitTimeout);
        ret.Resend();

        return ret;
    }

    public void EndCreateObjectRequest(IAsyncResult result, out Exception ex)
    {
        var res = (BacnetAsyncResult)result;
        ex = res.Error;
        if (ex == null && !res.WaitForDone(Timeout))
            ex = new Exception("Wait Timeout");

        res.Dispose();
    }

    public Boolean DeleteObjectRequest(BacnetAddress adr, BacnetObjectId objectId, Byte invokeId = 0)
    {
        using (var result = (BacnetAsyncResult)BeginDeleteObjectRequest(adr, objectId, true, invokeId))
        {
            for (var r = 0; r < _retries; r++)
            {
                if (result.WaitForDone(Timeout))
                {
                    EndDeleteObjectRequest(result, out var ex);
                    return ex != null ? throw ex : true;
                }
                if (r < Retries - 1)
                    result.Resend();
            }
        }

        return false;
    }

    public IAsyncResult BeginDeleteObjectRequest(BacnetAddress adr, BacnetObjectId objectId, Boolean waitForTransmit, Byte invokeId = 0)
    {
        Log.Debug("Sending DeleteObjectRequest");
        if (invokeId == 0) invokeId = unchecked(_invokeId++);

        var buffer = GetEncodeBuffer(Transport.HeaderLength);

        NPDU.Encode(buffer, BacnetNpduControls.PriorityNormalMessage | BacnetNpduControls.ExpectingReply, adr.RoutedSource, adr.RoutedDestination);
        //NPDU.Encode(b, BacnetNpduControls.PriorityNormalMessage | BacnetNpduControls.ExpectingReply , adr.RoutedSource);

        APDU.EncodeConfirmedServiceRequest(buffer, BacnetPduTypes.PDU_TYPE_CONFIRMED_SERVICE_REQUEST, BacnetConfirmedServices.SERVICE_CONFIRMED_DELETE_OBJECT, MaxSegments, Transport.MaxAdpuLength, invokeId);
        ASN1.encode_application_object_id(buffer, objectId.type, objectId.instance);

        //send
        var ret = new BacnetAsyncResult(this, adr, invokeId, buffer.buffer, buffer.offset - Transport.HeaderLength, waitForTransmit, TransmitTimeout);
        ret.Resend();

        return ret;
    }

    public void EndDeleteObjectRequest(IAsyncResult result, out Exception ex)
    {
        var res = (BacnetAsyncResult)result;
        ex = res.Error;
        if (ex == null && !res.WaitForDone(Timeout))
            ex = new Exception("Wait Timeout");

        res.Dispose();
    }

    public Boolean AddListElementRequest(BacnetAddress adr, BacnetObjectId objectId, BacnetPropertyReference reference, IList<BacnetValue> valueList, Byte invokeId = 0)
    {
        using (var result = (BacnetAsyncResult)BeginAddListElementRequest(adr, objectId, reference, valueList, true, invokeId))
        {
            for (var r = 0; r < _retries; r++)
            {

                if (result.WaitForDone(Timeout))
                {
                    EndAddListElementRequest(result, out var ex);
                    return ex != null ? throw ex : true;
                }
                if (r < Retries - 1)
                    result.Resend();
            }
        }
        //values = null;
        return false;
    }

    public Boolean RemoveListElementRequest(BacnetAddress adr, BacnetObjectId objectId, BacnetPropertyReference reference, IList<BacnetValue> valueList, Byte invokeId = 0)
    {
        using (var result = (BacnetAsyncResult)BeginRemoveListElementRequest(adr, objectId, reference, valueList, true, invokeId))
        {
            for (var r = 0; r < _retries; r++)
            {
                if (result.WaitForDone(Timeout))
                {
                    EndAddListElementRequest(result, out var ex);
                    return ex != null ? throw ex : true;
                }
                if (r < Retries - 1)
                    result.Resend();
            }
        }
        //values = null;
        return false;
    }

    public IAsyncResult BeginRemoveListElementRequest(BacnetAddress adr, BacnetObjectId objectId, BacnetPropertyReference reference, IList<BacnetValue> valueList, Boolean waitForTransmit, Byte invokeId = 0)
    {
        Log.Debug("Sending RemoveListElementRequest");
        if (invokeId == 0)
            invokeId = unchecked(_invokeId++);

        var buffer = GetEncodeBuffer(Transport.HeaderLength);
        NPDU.Encode(buffer, BacnetNpduControls.PriorityNormalMessage | BacnetNpduControls.ExpectingReply, adr.RoutedSource, adr.RoutedDestination);
        APDU.EncodeConfirmedServiceRequest(buffer, BacnetPduTypes.PDU_TYPE_CONFIRMED_SERVICE_REQUEST, BacnetConfirmedServices.SERVICE_CONFIRMED_REMOVE_LIST_ELEMENT, MaxSegments, Transport.MaxAdpuLength, invokeId);
        Services.EncodeAddListElement(buffer, objectId, reference.propertyIdentifier, reference.propertyArrayIndex, valueList);

        //send
        var ret = new BacnetAsyncResult(this, adr, invokeId, buffer.buffer, buffer.offset - Transport.HeaderLength, waitForTransmit, TransmitTimeout);
        ret.Resend();

        return ret;
    }

    public IAsyncResult BeginAddListElementRequest(BacnetAddress adr, BacnetObjectId objectId, BacnetPropertyReference reference, IList<BacnetValue> valueList, Boolean waitForTransmit, Byte invokeId = 0)
    {
        Log.Debug($"Sending AddListElementRequest {objectId} {(BacnetPropertyIds)reference.propertyIdentifier}");
        if (invokeId == 0)
            invokeId = unchecked(_invokeId++);

        var buffer = GetEncodeBuffer(Transport.HeaderLength);
        NPDU.Encode(buffer, BacnetNpduControls.PriorityNormalMessage | BacnetNpduControls.ExpectingReply, adr.RoutedSource, adr.RoutedDestination);
        APDU.EncodeConfirmedServiceRequest(buffer, BacnetPduTypes.PDU_TYPE_CONFIRMED_SERVICE_REQUEST, BacnetConfirmedServices.SERVICE_CONFIRMED_ADD_LIST_ELEMENT, MaxSegments, Transport.MaxAdpuLength, invokeId);
        Services.EncodeAddListElement(buffer, objectId, reference.propertyIdentifier, reference.propertyArrayIndex, valueList);

        //send
        var ret = new BacnetAsyncResult(this, adr, invokeId, buffer.buffer, buffer.offset - Transport.HeaderLength, waitForTransmit, TransmitTimeout);
        ret.Resend();

        return ret;
    }

    public void EndAddListElementRequest(IAsyncResult result, out Exception ex)
    {
        var res = (BacnetAsyncResult)result;
        ex = res.Error;
        if (ex == null && !res.WaitForDone(Timeout))
            ex = new Exception("Wait Timeout");

        res.Dispose();
    }

    // Fc
    // Read or Write without APDU Data encoding nor Decoding (just Request type, Object id and Property id)
    // Data is given by the caller starting with the Tag 3 (or maybe another one), and ending with it
    // return buffer start also with the Tag 3
    public Boolean RawEncodedDecodedPropertyConfirmedRequest(BacnetAddress adr, BacnetObjectId objectId, BacnetPropertyIds propertyId, BacnetConfirmedServices serviceId, ref Byte[] inOutBuffer, Byte invokeId = 0)
    {
        using (var result = (BacnetAsyncResult)BeginRawEncodedDecodedPropertyConfirmedRequest(adr, objectId, propertyId, serviceId, inOutBuffer, true, invokeId))
        {
            for (var r = 0; r < _retries; r++)
            {
                if (result.WaitForDone(Timeout))
                {
                    EndRawEncodedDecodedPropertyConfirmedRequest(result, serviceId, out inOutBuffer, out var ex);
                    return ex != null ? throw ex : true;
                }
                if (r < Retries - 1)
                    result.Resend();
            }
        }
        inOutBuffer = null;
        return false;
    }

    // Fc
    public IAsyncResult BeginRawEncodedDecodedPropertyConfirmedRequest(BacnetAddress adr, BacnetObjectId objectId, BacnetPropertyIds propertyId, BacnetConfirmedServices serviceId, Byte[] inOutBuffer, Boolean waitForTransmit, Byte invokeId = 0)
    {
        Log.Debug("Sending RawEncodedRequest");
        if (invokeId == 0)
            invokeId = unchecked(_invokeId++);

        var buffer = GetEncodeBuffer(Transport.HeaderLength);
        NPDU.Encode(buffer, BacnetNpduControls.PriorityNormalMessage | BacnetNpduControls.ExpectingReply, adr.RoutedSource, adr.RoutedDestination);
        APDU.EncodeConfirmedServiceRequest(buffer, PduConfirmedServiceRequest(), serviceId, MaxSegments, Transport.MaxAdpuLength, invokeId);

        ASN1.encode_context_object_id(buffer, 0, objectId.type, objectId.instance);
        ASN1.encode_context_enumerated(buffer, 1, (Byte)propertyId);

        // No content encoding to do
        if (inOutBuffer != null)
            buffer.Add(inOutBuffer, inOutBuffer.Length);

        //send
        var ret = new BacnetAsyncResult(this, adr, invokeId, buffer.buffer, buffer.offset - Transport.HeaderLength, waitForTransmit, TransmitTimeout);
        ret.Resend();

        return ret;
    }

    // Fc
    public void EndRawEncodedDecodedPropertyConfirmedRequest(IAsyncResult result, BacnetConfirmedServices serviceId, out Byte[] inOutBuffer, out Exception ex)
    {
        var res = (BacnetAsyncResult)result;
        ex = res.Error;
        if (ex == null && !res.WaitForDone(Timeout))
            ex = new Exception("Wait Timeout");

        inOutBuffer = null;

        if (ex == null)
        {
            if (serviceId == BacnetConfirmedServices.SERVICE_CONFIRMED_READ_PROPERTY)
            {
                //decode
                const Int32 offset = 0;
                var buffer = res.Result;

                ex = new Exception("Decode");

                if (!ASN1.decode_is_context_tag(buffer, offset, 0))
                    return;
                var len = 1;
                len += ASN1.decode_object_id(buffer, offset + len, out BacnetObjectTypes _, out _);
                /* Tag 1: Property ID */
                len += ASN1.decode_tag_number_and_value(buffer, offset + len, out var tagNumber, out var lenValueType);
                if (tagNumber != 1)
                    return;
                len += ASN1.decode_enumerated(buffer, offset + len, lenValueType, out _);

                inOutBuffer = new Byte[buffer.Length - len];
                Array.Copy(buffer, len, inOutBuffer, 0, inOutBuffer.Length);

                ex = null;
            }
        }

        res.Dispose();
    }

    public Boolean DeviceCommunicationControlRequest(BacnetAddress adr, UInt32 timeDuration, UInt32 enableDisable, String password, Byte invokeId = 0)
    {
        using (var result = (BacnetAsyncResult)BeginDeviceCommunicationControlRequest(adr, timeDuration, enableDisable, password, true, invokeId))
        {
            for (var r = 0; r < _retries; r++)
            {
                if (result.WaitForDone(Timeout))
                {
                    EndDeviceCommunicationControlRequest(result, out var ex);
                    return ex == null;
                }
                if (r < Retries - 1)
                    result.Resend();
            }
        }
        return false;
    }

    public IAsyncResult BeginDeviceCommunicationControlRequest(BacnetAddress adr, UInt32 timeDuration, UInt32 enableDisable, String password, Boolean waitForTransmit, Byte invokeId = 0)
    {
        Log.Debug("Sending DeviceCommunicationControlRequest");
        if (invokeId == 0)
            invokeId = unchecked(_invokeId++);

        var buffer = GetEncodeBuffer(Transport.HeaderLength);
        NPDU.Encode(buffer, BacnetNpduControls.PriorityNormalMessage | BacnetNpduControls.ExpectingReply, adr.RoutedSource, adr.RoutedDestination);
        APDU.EncodeConfirmedServiceRequest(buffer, BacnetPduTypes.PDU_TYPE_CONFIRMED_SERVICE_REQUEST, BacnetConfirmedServices.SERVICE_CONFIRMED_DEVICE_COMMUNICATION_CONTROL, MaxSegments, Transport.MaxAdpuLength, invokeId);
        Services.EncodeDeviceCommunicationControl(buffer, timeDuration, enableDisable, password);

        //send
        var ret = new BacnetAsyncResult(this, adr, invokeId, buffer.buffer, buffer.offset - Transport.HeaderLength, waitForTransmit, TransmitTimeout);
        ret.Resend();

        return ret;
    }

    public void EndDeviceCommunicationControlRequest(IAsyncResult result, out Exception ex)
    {
        var res = (BacnetAsyncResult)result;
        ex = res.Error;
        if (ex == null && !res.WaitForDone(Timeout))
            ex = new Exception("Wait Timeout");

        res.Dispose();
    }

    // FChaxel
    public Boolean GetAlarmSummaryOrEventRequest(BacnetAddress adr, Boolean getEvent, ref IList<BacnetGetEventInformationData> alarms, Byte invokeId = 0)
    {
        using (var result = (BacnetAsyncResult)BeginGetAlarmSummaryOrEventRequest(adr, getEvent, alarms, true, invokeId))
        {
            for (var r = 0; r < _retries; r++)
            {
                if (result.WaitForDone(Timeout))
                {
                    EndGetAlarmSummaryOrEventRequest(result, getEvent, ref alarms, out var moreEvent, out var ex);
                    return ex == null && (!moreEvent || GetAlarmSummaryOrEventRequest(adr, getEvent, ref alarms));
                }

                if (r < Retries - 1)
                    result.Resend();
            }
        }
        return false;
    }

    public Task<IList<BacnetGetEventInformationData>> GetEventsAsync(BacnetAddress address, Byte invokeId = 0)
    {
        IList<BacnetGetEventInformationData> result = new List<BacnetGetEventInformationData>();

        return Task<IList<BacnetGetEventInformationData>>.Factory.StartNew(() =>
        {
            return !GetAlarmSummaryOrEventRequest(address, true, ref result, invokeId)
                ? throw new Exception($"Failed to get events from {address}")
                : result;
        });
    }

    public IAsyncResult BeginGetAlarmSummaryOrEventRequest(BacnetAddress adr, Boolean getEvent, IList<BacnetGetEventInformationData> alarms, Boolean waitForTransmit, Byte invokeId = 0)
    {
        Log.Debug("Sending Alarm summary request");
        if (invokeId == 0)
            invokeId = unchecked(_invokeId++);

        var buffer = GetEncodeBuffer(Transport.HeaderLength);
        NPDU.Encode(buffer, BacnetNpduControls.PriorityNormalMessage | BacnetNpduControls.ExpectingReply, adr.RoutedSource, adr.RoutedDestination);

        var service = getEvent
            ? BacnetConfirmedServices.SERVICE_CONFIRMED_GET_EVENT_INFORMATION
            : BacnetConfirmedServices.SERVICE_CONFIRMED_GET_ALARM_SUMMARY;

        APDU.EncodeConfirmedServiceRequest(buffer, PduConfirmedServiceRequest(), service, MaxSegments, Transport.MaxAdpuLength, invokeId);

        // Get Next, never true if GetAlarmSummary is usee
        if (alarms.Count != 0)
            ASN1.encode_context_object_id(buffer, 0, alarms[alarms.Count - 1].objectIdentifier.type, alarms[alarms.Count - 1].objectIdentifier.instance);

        //send
        var ret = new BacnetAsyncResult(this, adr, invokeId, buffer.buffer, buffer.offset - Transport.HeaderLength, waitForTransmit, TransmitTimeout);
        ret.Resend();

        return ret;
    }

    public void EndGetAlarmSummaryOrEventRequest(IAsyncResult result, Boolean getEvent, ref IList<BacnetGetEventInformationData> alarms, out Boolean moreEvent, out Exception ex)
    {
        moreEvent = false;
        var res = (BacnetAsyncResult)result;
        ex = res.Error;
        if (ex == null && !res.WaitForDone(Timeout))
            ex = new Exception("Wait Timeout");

        if (ex == null)
        {
            if (Services.DecodeAlarmSummaryOrEvent(res.Result, 0, res.Result.Length, getEvent, ref alarms, out moreEvent) < 0)
                ex = new Exception("Decode");
        }
        else
        {
            ex = new Exception("Service not available");
        }

        res.Dispose();
    }
    // DAL
    public void GetAlarmSummaryOrEventInformationResponse(BacnetAddress adr, Boolean getEvent, Byte invoke_id, Segmentation segmentation, BacnetGetEventInformationData[] data, Boolean more_events)
    {
        // 'getEvent' is not currently used.   Can be used if ever implementing GetAlarmSummary.
        // response could be segmented
        // but if you don't want it segmented (which would be normal usage)
        // you have to compute the message data and the 'more' flag
        // outside this function.
        HandleSegmentationResponse(adr, invoke_id, segmentation, (o) =>
        {
            SendComplexAck(adr, invoke_id, segmentation, BacnetConfirmedServices.SERVICE_CONFIRMED_GET_EVENT_INFORMATION, (b) =>
            {
                Services.EncodeGetEventInformationAcknowledge(b, data, more_events);
            });
        });
    }

    // FChaxel
    public Boolean AlarmAcknowledgement(BacnetAddress adr, BacnetObjectId objId, BacnetEventStates eventState, String ackText, BacnetGenericTime evTimeStamp, BacnetGenericTime ackTimeStamp, Byte invokeId = 0)
    {
        using (var result = (BacnetAsyncResult)BeginAlarmAcknowledgement(adr, objId, eventState, ackText, evTimeStamp, ackTimeStamp, true, invokeId))
        {
            for (var r = 0; r < _retries; r++)
            {
                if (result.WaitForDone(Timeout))
                {
                    EndAlarmAcknowledgement(result, out var ex);
                    return ex == null;
                }
                if (r < Retries - 1)
                    result.Resend();
            }
        }
        return false;
    }

    public IAsyncResult BeginAlarmAcknowledgement(BacnetAddress adr, BacnetObjectId objId, BacnetEventStates eventState, String ackText, BacnetGenericTime evTimeStamp, BacnetGenericTime ackTimeStamp, Boolean waitForTransmit, Byte invokeId = 0)
    {
        Log.Debug("Sending AlarmAcknowledgement");
        if (invokeId == 0)
            invokeId = unchecked(_invokeId++);

        var buffer = GetEncodeBuffer(Transport.HeaderLength);
        NPDU.Encode(buffer, BacnetNpduControls.PriorityNormalMessage, adr.RoutedSource, adr.RoutedDestination);
        APDU.EncodeConfirmedServiceRequest(buffer, BacnetPduTypes.PDU_TYPE_CONFIRMED_SERVICE_REQUEST, BacnetConfirmedServices.SERVICE_CONFIRMED_ACKNOWLEDGE_ALARM, MaxSegments, Transport.MaxAdpuLength, invokeId);
        Services.EncodeAlarmAcknowledge(buffer, 57, objId, (UInt32)eventState, ackText, evTimeStamp, ackTimeStamp);

        //send
        var ret = new BacnetAsyncResult(this, adr, invokeId, buffer.buffer, buffer.offset - Transport.HeaderLength, waitForTransmit, TransmitTimeout);
        ret.Resend();

        return ret;
    }

    public void EndAlarmAcknowledgement(IAsyncResult result, out Exception ex)
    {
        var res = (BacnetAsyncResult)result;
        ex = res.Error;
        if (!res.WaitForDone(Timeout))
            ex = new Exception("Wait Timeout");
    }

    public Boolean ReinitializeRequest(BacnetAddress adr, BacnetReinitializedStates state, String password, Byte invokeId = 0)
    {
        using (var result = (BacnetAsyncResult)BeginReinitializeRequest(adr, state, password, true, invokeId))
        {
            for (var r = 0; r < _retries; r++)
            {
                if (result.WaitForDone(Timeout))
                {
                    EndReinitializeRequest(result, out var ex);
                    return ex == null;
                }
                if (r < Retries - 1)
                    result.Resend();
            }
        }
        return false;
    }

    public IAsyncResult BeginReinitializeRequest(BacnetAddress adr, BacnetReinitializedStates state, String password, Boolean waitForTransmit, Byte invokeId = 0)
    {
        Log.Debug("Sending ReinitializeRequest");
        if (invokeId == 0)
            invokeId = unchecked(_invokeId++);

        var buffer = GetEncodeBuffer(Transport.HeaderLength);
        NPDU.Encode(buffer, BacnetNpduControls.PriorityNormalMessage | BacnetNpduControls.ExpectingReply, adr.RoutedSource, adr.RoutedDestination);
        APDU.EncodeConfirmedServiceRequest(buffer, BacnetPduTypes.PDU_TYPE_CONFIRMED_SERVICE_REQUEST, BacnetConfirmedServices.SERVICE_CONFIRMED_REINITIALIZE_DEVICE, MaxSegments, Transport.MaxAdpuLength, invokeId);
        Services.EncodeReinitializeDevice(buffer, state, password);

        //send
        var ret = new BacnetAsyncResult(this, adr, invokeId, buffer.buffer, buffer.offset - Transport.HeaderLength, waitForTransmit, TransmitTimeout);
        ret.Resend();

        return ret;
    }

    public void EndReinitializeRequest(IAsyncResult result, out Exception ex)
    {
        var res = (BacnetAsyncResult)result;
        ex = res.Error;
        if (ex == null && !res.WaitForDone(Timeout))
            ex = new Exception("Wait Timeout");

        res.Dispose();
    }

    public IAsyncResult BeginConfirmedNotify(BacnetAddress adr, UInt32 subscriberProcessIdentifier, UInt32 initiatingDeviceIdentifier, BacnetObjectId monitoredObjectIdentifier, UInt32 timeRemaining, IList<BacnetPropertyValue> values, Boolean waitForTransmit, Byte invokeId = 0)
    {
        Log.Debug("Sending Notify (confirmed)");
        if (invokeId == 0) invokeId = unchecked(_invokeId++);

        var buffer = GetEncodeBuffer(Transport.HeaderLength);
        NPDU.Encode(buffer, BacnetNpduControls.PriorityNormalMessage | BacnetNpduControls.ExpectingReply, adr.RoutedSource, adr.RoutedDestination);
        APDU.EncodeConfirmedServiceRequest(buffer, BacnetPduTypes.PDU_TYPE_CONFIRMED_SERVICE_REQUEST, BacnetConfirmedServices.SERVICE_CONFIRMED_COV_NOTIFICATION, MaxSegments, Transport.MaxAdpuLength, invokeId);
        Services.EncodeCOVNotifyConfirmed(buffer, subscriberProcessIdentifier, initiatingDeviceIdentifier, monitoredObjectIdentifier, timeRemaining, values);

        //send
        var ret = new BacnetAsyncResult(this, adr, invokeId, buffer.buffer, buffer.offset - Transport.HeaderLength, waitForTransmit, TransmitTimeout);
        ret.Resend();

        return ret;
    }

    public void EndConfirmedNotify(IAsyncResult result, out Exception ex)
    {
        var res = (BacnetAsyncResult)result;
        ex = res.Error;
        if (!res.WaitForDone(Timeout))
            ex = new Exception("Wait Timeout");
    }

    public Boolean Notify(BacnetAddress adr, UInt32 subscriberProcessIdentifier, UInt32 initiatingDeviceIdentifier, BacnetObjectId monitoredObjectIdentifier, UInt32 timeRemaining, Boolean issueConfirmedNotifications, IList<BacnetPropertyValue> values)
    {
        if (!issueConfirmedNotifications)
        {
            Log.Debug("Sending Notify (unconfirmed)");
            var buffer = GetEncodeBuffer(Transport.HeaderLength);
            NPDU.Encode(buffer, BacnetNpduControls.PriorityNormalMessage, adr.RoutedSource, adr.RoutedDestination);
            APDU.EncodeUnconfirmedServiceRequest(buffer, BacnetPduTypes.PDU_TYPE_UNCONFIRMED_SERVICE_REQUEST, BacnetUnconfirmedServices.SERVICE_UNCONFIRMED_COV_NOTIFICATION);
            Services.EncodeCOVNotifyUnconfirmed(buffer, subscriberProcessIdentifier, initiatingDeviceIdentifier, monitoredObjectIdentifier, timeRemaining, values);
            // Modif F. Chaxel

            var sendbytes = Transport.Send(buffer.buffer, Transport.HeaderLength, buffer.offset - Transport.HeaderLength, adr, false, 0);
            return sendbytes == buffer.offset;
        }

        using (var result = (BacnetAsyncResult)BeginConfirmedNotify(adr, subscriberProcessIdentifier, initiatingDeviceIdentifier, monitoredObjectIdentifier, timeRemaining, values, true))
        {
            for (var r = 0; r < _retries; r++)
            {
                if (result.WaitForDone(Timeout))
                {
                    EndConfirmedNotify(result, out var ex);
                    return ex != null ? throw ex : true;
                }
                if (r < Retries - 1)
                    result.Resend();
            }
        }

        return false;
    }

    public Boolean LifeSafetyOperationRequest(BacnetAddress address, BacnetObjectId objectId, String requestingSrc, BacnetLifeSafetyOperations operation, Byte invokeId = 0)
    {
        using (var result = (BacnetAsyncResult)BeginLifeSafetyOperationRequest(address, objectId, 0, requestingSrc, operation, true, invokeId))
        {
            for (var r = 0; r < _retries; r++)
            {
                if (result.WaitForDone(Timeout))
                {
                    EndLifeSafetyOperationRequest(result, out var ex);
                    return ex == null;
                }
                if (r < Retries - 1)
                    result.Resend();
            }
        }
        return false;
    }

    public IAsyncResult BeginLifeSafetyOperationRequest(BacnetAddress address, BacnetObjectId objectId, UInt32 processId, String requestingSrc, BacnetLifeSafetyOperations operation, Boolean waitForTransmit, Byte invokeId = 0)
    {
        Log.Debug($"Sending {ToTitleCase(operation)} {objectId}");
        if (invokeId == 0)
            invokeId = unchecked(_invokeId++);

        var buffer = GetEncodeBuffer(Transport.HeaderLength);
        NPDU.Encode(buffer, BacnetNpduControls.PriorityNormalMessage | BacnetNpduControls.ExpectingReply, address.RoutedSource, address.RoutedDestination);
        APDU.EncodeConfirmedServiceRequest(buffer, BacnetPduTypes.PDU_TYPE_CONFIRMED_SERVICE_REQUEST, BacnetConfirmedServices.SERVICE_CONFIRMED_LIFE_SAFETY_OPERATION, MaxSegments, Transport.MaxAdpuLength, invokeId);
        Services.EncodeLifeSafetyOperation(buffer, processId, requestingSrc, (UInt32)operation, objectId);

        //send
        var ret = new BacnetAsyncResult(this, address, invokeId, buffer.buffer, buffer.offset - Transport.HeaderLength, waitForTransmit, TransmitTimeout);
        ret.Resend();

        return ret;
    }

    public void EndLifeSafetyOperationRequest(IAsyncResult result, out Exception ex)
    {
        var res = (BacnetAsyncResult)result;
        ex = res.Error;
        if (ex == null && !res.WaitForDone(Timeout))
            ex = new Exception("Wait Timeout");

        res.Dispose();
    }

    public static Byte GetSegmentsCount(BacnetMaxSegments maxSegments)
    {
        return maxSegments switch
        {
            BacnetMaxSegments.MAX_SEG0 => 0,
            BacnetMaxSegments.MAX_SEG2 => 2,
            BacnetMaxSegments.MAX_SEG4 => 4,
            BacnetMaxSegments.MAX_SEG8 => 8,
            BacnetMaxSegments.MAX_SEG16 => 16,
            BacnetMaxSegments.MAX_SEG32 => 32,
            BacnetMaxSegments.MAX_SEG64 => 64,
            BacnetMaxSegments.MAX_SEG65 => 0xFF,
            _ => throw new Exception("Not an option"),
        };
    }

    public static BacnetMaxSegments GetSegmentsCount(Byte maxSegments)
    {
        if (maxSegments == 0)
            return BacnetMaxSegments.MAX_SEG0;
        if (maxSegments <= 2)
            return BacnetMaxSegments.MAX_SEG2;
        if (maxSegments <= 4)
            return BacnetMaxSegments.MAX_SEG4;
        if (maxSegments <= 8)
            return BacnetMaxSegments.MAX_SEG8;
        if (maxSegments <= 16)
            return BacnetMaxSegments.MAX_SEG16;
        if (maxSegments <= 32)
            return BacnetMaxSegments.MAX_SEG32;
        return maxSegments <= 64 ? BacnetMaxSegments.MAX_SEG64 : BacnetMaxSegments.MAX_SEG65;
    }

    public Segmentation GetSegmentBuffer(BacnetMaxSegments maxSegments)
    {
        return maxSegments == BacnetMaxSegments.MAX_SEG0
            ? null
            : new Segmentation
            {
                buffer = GetEncodeBuffer(Transport.HeaderLength),
                max_segments = GetSegmentsCount(maxSegments),
                window_size = ProposedWindowSize
            };
    }

    private EncodeBuffer EncodeSegmentHeader(BacnetAddress adr, Byte invokeId, Segmentation segmentation, BacnetConfirmedServices service, Boolean moreFollows)
    {
        EncodeBuffer buffer;
        var isSegmented = false;
        if (segmentation == null)
            buffer = GetEncodeBuffer(Transport.HeaderLength);
        else
        {
            buffer = segmentation.buffer;
            isSegmented = segmentation.sequence_number > 0 | moreFollows;
        }
        buffer.Reset(Transport.HeaderLength);

        //encode
        NPDU.Encode(buffer, BacnetNpduControls.PriorityNormalMessage, adr.RoutedSource, adr.RoutedDestination);

        //set segments limits
        buffer.max_offset = buffer.offset + GetMaxApdu();
        var apduHeader = APDU.EncodeComplexAck(buffer, BacnetPduTypes.PDU_TYPE_COMPLEX_ACK | (isSegmented ? BacnetPduTypes.SEGMENTED_MESSAGE | BacnetPduTypes.SERVER : 0) | (moreFollows ? BacnetPduTypes.MORE_FOLLOWS : 0), service, invokeId, segmentation?.sequence_number ?? 0, segmentation?.window_size ?? 0);
        buffer.min_limit = (GetMaxApdu() - apduHeader) * (segmentation?.sequence_number ?? 0);

        return buffer;
    }

    private Boolean EncodeSegment(BacnetAddress adr, Byte invokeId, Segmentation segmentation, BacnetConfirmedServices service, out EncodeBuffer buffer, Action<EncodeBuffer> apduContentEncode)
    {
        //encode (regular)
        buffer = EncodeSegmentHeader(adr, invokeId, segmentation, service, false);
        apduContentEncode(buffer);

        var moreFollows = (buffer.result & EncodeResult.NotEnoughBuffer) > 0;
        if (segmentation != null && moreFollows)
        {
            //reencode in segmented
            EncodeSegmentHeader(adr, invokeId, segmentation, service, true);
            apduContentEncode(buffer);
            return true;
        }

        return moreFollows || segmentation != null && segmentation.sequence_number > 0;
    }

    /// <summary>
    /// Handle the segmentation of several too hugh response (if it's accepted by the client) 
    /// used by ReadRange, ReadProperty, ReadPropertyMultiple & ReadFile responses
    /// </summary>
    private void HandleSegmentationResponse(BacnetAddress adr, Byte invokeId, Segmentation segmentation, Action<Segmentation> transmit)
    {
        // send first
        transmit(segmentation);

        if (segmentation == null || segmentation.buffer.result == EncodeResult.Good)
            return;

        // start new thread to handle the segment sequence (if required)
        ThreadPool.QueueUserWorkItem(o =>
        {
            var oldMaxInfoFrames = Transport.MaxInfoFrames;
            Transport.MaxInfoFrames = segmentation.window_size; // increase max_info_frames, to increase throughput. This might be against 'standard'

            while (true)
            {
                var moreFollows = (segmentation.buffer.result & EncodeResult.NotEnoughBuffer) > 0;

                // wait for segmentACK
                if ((segmentation.sequence_number - 1) % segmentation.window_size == 0 || !moreFollows)
                {
                    if (!WaitForAllTransmits(TransmitTimeout))
                    {
                        Log.Warn("Transmit timeout");
                        break;
                    }

                    var currentNumber = segmentation.sequence_number;

                    if (!WaitForSegmentAck(adr, invokeId, segmentation, Timeout))
                    {
                        Log.Warn("Didn't get segmentACK");
                        break;
                    }

                    if (segmentation.sequence_number != currentNumber)
                    {
                        Log.Debug("Oh, a retransmit");
                        moreFollows = true;
                    }
                }
                else
                {
                    // a negative segmentACK perhaps
                    var currentNumber = segmentation.sequence_number;
                    WaitForSegmentAck(adr, invokeId, segmentation, 0); // don't wait

                    if (segmentation.sequence_number != currentNumber)
                        Log.Debug("Oh, a retransmit");
                }

                if (moreFollows)
                    // lock (m_lockObject) transmit(segmentation);
                    transmit(segmentation);
                else
                    break;
            }

            Transport.MaxInfoFrames = oldMaxInfoFrames;
        });
    }

    private void SendComplexAck(BacnetAddress adr, Byte invokeId, Segmentation segmentation, BacnetConfirmedServices service, Action<EncodeBuffer> apduContentEncode)
    {
        Log.Debug($"Sending {ToTitleCase(service)}");

        //encode
        if (EncodeSegment(adr, invokeId, segmentation, service, out var buffer, apduContentEncode))
        {
            //client doesn't support segments
            if (segmentation == null)
            {
                Log.Info("Segmentation denied");
                // DAL
                SendAbort(adr, invokeId, BacnetAbortReason.SEGMENTATION_NOT_SUPPORTED);
                //ErrorResponse(adr, service, invokeId, BacnetErrorClasses.ERROR_CLASS_SERVICES, BacnetErrorCodes.ERROR_CODE_ABORT_APDU_TOO_LONG);
                buffer.result = EncodeResult.Good;     //don't continue the segmentation
                return;
            }

            //first segment? validate max segments
            if (segmentation.sequence_number == 0)  //only validate first segment
            {
                if (segmentation.max_segments != 0xFF && segmentation.buffer.offset > segmentation.max_segments * (GetMaxApdu() - 5))      //5 is adpu header
                {
                    Log.Info("Too much segmenation");
                    // DAL
                    SendAbort(adr, invokeId, BacnetAbortReason.APDU_TOO_LONG);
                    //ErrorResponse(adr, service, invokeId, BacnetErrorClasses.ERROR_CLASS_SERVICES, BacnetErrorCodes.ERROR_CODE_ABORT_APDU_TOO_LONG);
                    buffer.result = EncodeResult.Good;     //don't continue the segmentation
                    return;
                }
                Log.Debug("Segmentation required");
            }

            //increment before ack can do so (race condition)
            unchecked { segmentation.sequence_number++; };
        }

        //send
        Transport.Send(buffer.buffer, Transport.HeaderLength, buffer.GetLength() - Transport.HeaderLength, adr, false, 0);
    }

    public void ReadPropertyResponse(BacnetAddress adr, Byte invokeId, Segmentation segmentation, BacnetObjectId objectId, BacnetPropertyReference property, IEnumerable<BacnetValue> value)
    {
        HandleSegmentationResponse(adr, invokeId, segmentation, o =>
        {
            SendComplexAck(adr, invokeId, segmentation, BacnetConfirmedServices.SERVICE_CONFIRMED_READ_PROPERTY, b =>
            {
                Services.EncodeReadPropertyAcknowledge(b, objectId, property.propertyIdentifier, property.propertyArrayIndex, value);
            });
        });
    }

    public void CreateObjectResponse(BacnetAddress adr, Byte invokeId, Segmentation segmentation, BacnetObjectId objectId)
    {
        SendComplexAck(adr, invokeId, segmentation, BacnetConfirmedServices.SERVICE_CONFIRMED_CREATE_OBJECT, b =>
        {
            Services.EncodeCreateObjectAcknowledge(b, objectId);
        });
    }

    public void ReadPropertyMultipleResponse(BacnetAddress adr, Byte invokeId, Segmentation segmentation, IList<BacnetReadAccessResult> values)
    {
        HandleSegmentationResponse(adr, invokeId, segmentation, o =>
        {
            SendComplexAck(adr, invokeId, segmentation, BacnetConfirmedServices.SERVICE_CONFIRMED_READ_PROP_MULTIPLE, b =>
            {
                Services.EncodeReadPropertyMultipleAcknowledge(b, values);
            });
        });
    }

    public void ReadRangeResponse(BacnetAddress adr, Byte invokeId, Segmentation segmentation, BacnetObjectId objectId, BacnetPropertyReference property, BacnetResultFlags status, UInt32 itemCount, Byte[] applicationData, BacnetReadRangeRequestTypes requestType, UInt32 firstSequenceNo)
    {
        HandleSegmentationResponse(adr, invokeId, segmentation, o =>
        {
            SendComplexAck(adr, invokeId, segmentation, BacnetConfirmedServices.SERVICE_CONFIRMED_READ_RANGE, b =>
            {
                Services.EncodeReadRangeAcknowledge(b, objectId, property.propertyIdentifier, property.propertyArrayIndex, BacnetBitString.ConvertFromInt((UInt32)status), itemCount, applicationData, requestType, firstSequenceNo);
            });
        });
    }

    public void ReadFileResponse(BacnetAddress adr, Byte invokeId, Segmentation segmentation, Int32 position, UInt32 count, Boolean endOfFile, Byte[] fileBuffer)
    {
        HandleSegmentationResponse(adr, invokeId, segmentation, o =>
        {
            SendComplexAck(adr, invokeId, segmentation, BacnetConfirmedServices.SERVICE_CONFIRMED_ATOMIC_READ_FILE, b =>
            {
                Services.EncodeAtomicReadFileAcknowledge(b, true, endOfFile, position, 1, new[] { fileBuffer }, new[] { (Int32)count });
            });
        });
    }

    public void WriteFileResponse(BacnetAddress adr, Byte invokeId, Segmentation segmentation, Int32 position)
    {
        SendComplexAck(adr, invokeId, segmentation, BacnetConfirmedServices.SERVICE_CONFIRMED_ATOMIC_WRITE_FILE, b =>
        {
            Services.EncodeAtomicWriteFileAcknowledge(b, true, position);
        });
    }

    public void ErrorResponse(BacnetAddress adr, BacnetConfirmedServices service, Byte invokeId, BacnetErrorClasses errorClass, BacnetErrorCodes errorCode)
    {
        Log.Debug($"Sending ErrorResponse for {service}: {errorCode}");
        var buffer = GetEncodeBuffer(Transport.HeaderLength);
        NPDU.Encode(buffer, BacnetNpduControls.PriorityNormalMessage, adr.RoutedSource, adr.RoutedDestination);
        APDU.EncodeError(buffer, BacnetPduTypes.PDU_TYPE_ERROR, service, invokeId);
        Services.EncodeError(buffer, errorClass, errorCode);
        Transport.Send(buffer.buffer, Transport.HeaderLength, buffer.offset - Transport.HeaderLength, adr, false, 0);
    }

    public void SimpleAckResponse(BacnetAddress adr, BacnetConfirmedServices service, Byte invokeId)
    {
        Log.Debug($"Sending SimpleAckResponse for {service}");
        var buffer = GetEncodeBuffer(Transport.HeaderLength);
        NPDU.Encode(buffer, BacnetNpduControls.PriorityNormalMessage, adr.RoutedSource, adr.RoutedDestination);
        APDU.EncodeSimpleAck(buffer, BacnetPduTypes.PDU_TYPE_SIMPLE_ACK, service, invokeId);
        Transport.Send(buffer.buffer, Transport.HeaderLength, buffer.offset - Transport.HeaderLength, adr, false, 0);
    }

    public void SegmentAckResponse(BacnetAddress adr, Boolean negative, Boolean server, Byte originalInvokeId, Byte sequenceNumber, Byte actualWindowSize)
    {
        Log.Debug("Sending SegmentAckResponse");
        var buffer = GetEncodeBuffer(Transport.HeaderLength);
        NPDU.Encode(buffer, BacnetNpduControls.PriorityNormalMessage, adr.RoutedSource, adr.RoutedDestination);
        APDU.EncodeSegmentAck(buffer, BacnetPduTypes.PDU_TYPE_SEGMENT_ACK | (negative ? BacnetPduTypes.NEGATIVE_ACK : 0) | (server ? BacnetPduTypes.SERVER : 0), originalInvokeId, sequenceNumber, actualWindowSize);
        Transport.Send(buffer.buffer, Transport.HeaderLength, buffer.offset - Transport.HeaderLength, adr, false, 0);
    }

    public Boolean WaitForAllTransmits(Int32 timeout) => Transport.WaitForAllTransmits(timeout);

    public Boolean WaitForSegmentAck(BacnetAddress adr, Byte invokeId, Segmentation segmentation, Int32 timeout)
    {
        if (!_lastSegmentAck.Wait(adr, invokeId, timeout))
            return false;

        segmentation.sequence_number = (Byte)((_lastSegmentAck.SequenceNumber + 1) % 256);
        segmentation.window_size = _lastSegmentAck.WindowSize;
        return true;
    }

    private static String ToTitleCase(Object obj)
    {
        var cultureTextInfo = Thread.CurrentThread.CurrentCulture.TextInfo;
        return cultureTextInfo.ToTitleCase($"{obj}".ToLower());
    }

    public void Dispose() => Transport.Dispose();
}
