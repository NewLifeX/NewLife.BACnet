using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NewLife.IoT;
using NewLife.IoT.Models;
using NewLife.IoT.ThingModels;
using NewLife.IoT.ThingSpecification;

namespace UnitTest;

internal class ThingDevice : IDevice
{
    public String Code { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public IDictionary<String, Object?> Properties => throw new NotImplementedException();

    public ThingSpec? Specification { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public IPoint[]? Points { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public IDictionary<String, Delegate> Services => throw new NotImplementedException();

    public Boolean AddData(String name, String value) => throw new NotImplementedException();
    public void PostProperty() => throw new NotImplementedException();
    public void RegisterService(String service, Delegate method) => throw new NotImplementedException();
    public void SetProperty(String name, Object? value) => throw new NotImplementedException();
    public Boolean WriteEvent(String type, String name, String remark) => throw new NotImplementedException();

    public Task StartAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task StopAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<IDeviceInfo[]> SetOnlineAsync(IDeviceInfo[] devices, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<IDeviceInfo[]> SetOfflineAsync(String[] devices, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<Int32> PostPropertyAsync(String deviceCode, Object items, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<Int32> PostDataAsync(String deviceCode, DataModel[] items, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<Int32> PostEventAsync(String deviceCode, EventModel[] items, CancellationToken cancellationToken) => throw new NotImplementedException();
}
