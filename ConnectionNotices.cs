using System;
using System.Collections.Generic;
using System.Linq;

namespace LinkScope
{
    internal sealed class ConnectionNotices
    {
        private Dictionary<string,Device> online;
        private HashSet<string> missedRadio=new HashSet<string>(StringComparer.Ordinal);

        internal List<string> Update(IEnumerable<Device> devices)
        {
            var connected=new Dictionary<string,Device>(StringComparer.Ordinal);
            var missed=new HashSet<string>(StringComparer.Ordinal);
            var changes=new List<string>();
            foreach(var device in devices.Where(d=>d.Connected))
            {
                Device previous=null;
                if(online!=null && !online.TryGetValue(device.Id,out previous))
                {
                    if(device.AlternateId!=null) online.TryGetValue(device.AlternateId,out previous);
                    else previous=online.Values.FirstOrDefault(d=>d.AlternateId==device.Id);
                }
                if(device.Link=="2.4G" && LunaBattery.IsReceiver(device.Model) &&
                    (!Tracking.HasBattery(device) || device.RetainedBattery))
                {
                    // One missed battery response does not imply a wireless disconnect.
                    if(previous!=null && previous.Link==device.Link && !missedRadio.Contains(device.Id))
                    {
                        connected[device.Id]=previous;
                        missed.Add(device.Id);
                    }
                    continue;
                }
                if(connected.ContainsKey(device.Id)) continue;
                connected.Add(device.Id,device);
                if(online!=null && (previous==null || previous.Link!=device.Link))
                {
                    string link=device.Link=="USB" ? "USB 有线" : device.Link;
                    changes.Add(previous==null ? (device.Link=="USB" ? "已插入 " : "已连接 ")+device.Name+" · "+link
                        : device.Name+" 已切换为 "+link);
                }
            }
            online=connected;
            missedRadio=missed;
            return changes;
        }
    }
}
