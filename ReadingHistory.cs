using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LinkScope
{
    internal static class Tracking
    {
        internal static bool HasBattery(Device d) { return d.Battery.HasValue || d.Level!=null && d.Source!=null; }

        internal static bool Show(Device d)
        {
            if(d.Level=="外部供电") return false;
            if(HasBattery(d) || d.BatteryCapable) return true;
            if(d.Kind=="手表") return d.Connected&&d.Link=="蓝牙";
            bool portableInput=d.Kind=="鼠标" || d.Kind=="键盘" || d.Kind=="手柄";
            return portableInput && (d.Link=="蓝牙" || d.Link=="2.4G" && !RogOmni.IsReceiver(d.Model));
        }

        internal static string ReadTime(Device d,DateTime now)
        {
            if(!d.BatteryReadAt.HasValue) return "";
            DateTime when=d.BatteryReadAt.Value;
            string stamp=when.Date==now.Date ? when.ToString("HH:mm:ss")
                : when.Date==now.Date.AddDays(-1) ? "昨天 "+when.ToString("HH:mm")
                : when.ToString("MM-dd HH:mm");
            return "上次读取 "+stamp;
        }
    }

    internal sealed class ReadingHistory
    {
        private const int Limit=256;
        private readonly string path;
        private readonly Dictionary<string,Reading> entries=new Dictionary<string,Reading>(StringComparer.Ordinal);
        private sealed class Reading
        {
            internal readonly string Value;
            internal readonly DateTime Utc;
            internal Reading(string value,DateTime utc) { Value=value;Utc=utc; }
        }
        internal ReadingHistory(string file)
        {
            path=file;
            try
            {
                if(!File.Exists(path)) return;
                foreach(var line in File.ReadLines(path).Take(Limit))
                {
                    var parts=line.Split('\t');
                    long ticks;
                    if(parts.Length!=3 || parts[0].Length!=64 || !long.TryParse(parts[1],out ticks)
                        || ticks<=0 || ticks>DateTime.MaxValue.Ticks || !ValidValue(parts[2])) continue;
                    entries[parts[0]]=new Reading(parts[2],new DateTime(ticks,DateTimeKind.Utc));
                }
            }
            catch(IOException) { }
            catch(UnauthorizedAccessException) { }
        }

        internal void Attach(List<Device> devices,DateTime now)
        {
            bool changed=false;
            using(var hash=SHA256.Create())
            {
                foreach(var d in devices)
                {
                    if(d.Level=="外部供电" || string.IsNullOrEmpty(d.Id)) continue;
                    string key=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(d.Id.ToUpperInvariant()))).Replace("-","");
                    if(Tracking.HasBattery(d))
                    {
                        d.BatteryReadAt=now;
                        string value=d.Battery.HasValue ? d.Battery.Value.ToString(CultureInfo.InvariantCulture) : d.Level;
                        if(!ValidValue(value)) continue;
                        entries[key]=new Reading(value,now.ToUniversalTime());
                        changed=true;
                        continue;
                    }
                    Reading old;
                    entries.TryGetValue(key,out old);
                    if(d.AlternateId!=null)
                    {
                        string alternateKey=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(d.AlternateId.ToUpperInvariant()))).Replace("-","");
                        Reading alternate;
                        if(entries.TryGetValue(alternateKey,out alternate) && (old==null || alternate.Utc>old.Utc)) old=alternate;
                    }
                    if(old==null) continue;
                    int percent;
                    if(int.TryParse(old.Value,out percent)) d.Battery=percent;
                    else d.Level=old.Value;
                    d.BatteryReadAt=old.Utc.ToLocalTime();
                    d.RetainedBattery=true;
                    d.Source="上次读取";
                }
            }
            if(changed) Save();
        }

        private void Save()
        {
            var latest=entries.OrderByDescending(p=>p.Value.Utc).Take(Limit).ToArray();
            if(entries.Count>Limit)
            {
                entries.Clear();
                foreach(var p in latest) entries.Add(p.Key,p.Value);
            }
            try
            {
                string temporary=path+".tmp";
                File.WriteAllLines(temporary,latest.Select(p=>p.Key+"\t"+p.Value.Utc.Ticks.ToString(CultureInfo.InvariantCulture)+"\t"+p.Value.Value));
                if(File.Exists(path)) File.Replace(temporary,path,null);
                else File.Move(temporary,path);
            }
            catch(IOException) { }
            catch(UnauthorizedAccessException) { }
        }

        private static bool ValidValue(string value)
        {
            int level;
            return int.TryParse(value,out level) && level>=0 && level<=100
                || value=="空" || value=="低" || value=="中" || value=="满" || value=="极低";
        }
    }
}
