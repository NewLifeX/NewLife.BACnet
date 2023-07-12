using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NewLife.IoT;
using NewLife.IoT.Models;
using NewLife.IoT.ThingModels;
using NewLife.IoT.ThingSpecification;

namespace UnitTest
{
    internal class ThingDevice : IDevice
    {
        public String Code { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public IDictionary<String, Object> Properties => throw new NotImplementedException();

        public ThingSpec Specification { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public IPoint[] Points { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public IDictionary<String, Delegate> Services => throw new NotImplementedException();

        public Boolean AddData(String name, String value) => throw new NotImplementedException();
        public void PostProperty() => throw new NotImplementedException();
        public void RegisterService(String service, Delegate method) => throw new NotImplementedException();
        public IDeviceInfo[] SetOffline(String[] devices) => throw new NotImplementedException();
        public IDeviceInfo[] SetOnline(IDeviceInfo[] devices) => throw new NotImplementedException();
        public void SetProperty(String name, Object value) => throw new NotImplementedException();
        public Task Start() => throw new NotImplementedException();
        public void Stop() => throw new NotImplementedException();
        public Boolean WriteEvent(String type, String name, String remark) => throw new NotImplementedException();
    }
}
