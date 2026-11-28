using System;
using System.Collections.Generic;
using System.Linq;

namespace LinkScope
{
    internal static class DeviceConnections
    {
        internal static void PreferWired(List<Device> devices)
        {
            var wired=devices.Where(d=>d.Link=="USB" && d.Connected).ToList();
            var receivers=devices.Where(d=>d.Link=="2.4G").ToList();
            foreach(var cable in wired)
            {
                var matches=receivers.Where(r=>SameUnit(cable,r)).ToList();
                if(matches.Count==1 && wired.Count(w=>SameUnit(w,matches[0]))==1)
                {
                    Merge(devices,cable,matches[0]);
                    continue;
                }

                string wiredModel=PhysicalModel(cable);
                if(wiredModel==null || wired.Count(w=>w.Kind==cable.Kind && PhysicalModel(w)==wiredModel)!=1) continue;
                matches=receivers.Where(r=>r.Kind==cable.Kind && PhysicalModel(r)==wiredModel).ToList();
                if(matches.Count!=1 || Tracking.HasBattery(matches[0]) ||
                    cable.UnitKey!=null && matches[0].UnitKey!=null) continue;
                Merge(devices,cable,matches[0]);
            }
        }

        private static bool SameUnit(Device wired,Device receiver)
        {
            return wired.Kind==receiver.Kind && wired.UnitKey!=null &&
                string.Equals(wired.UnitKey,receiver.UnitKey,StringComparison.Ordinal);
        }

        private static string PhysicalModel(Device device)
        {
            if(device.Kind=="鼠标")
            {
                var luna=LunaBattery.ModelName(device.Model);
                if(luna!=null) return luna;
                return RazerBattery.WiredModel(device.Model);
            }
            if(device.Kind=="键盘") return RogOmni.KeyboardModel(device);
            return null;
        }

        private static void Merge(List<Device> devices,Device cable,Device receiver)
        {
            cable.AlternateId=cable.Id;
            cable.Id=receiver.Id;
            cable.BatteryCapable=true;
            if(!Tracking.HasBattery(cable) && Tracking.HasBattery(receiver))
            {
                cable.Battery=receiver.Battery;
                cable.Level=receiver.Level;
                cable.Source=receiver.Source;
            }
            devices.Remove(receiver);
        }
    }
}
