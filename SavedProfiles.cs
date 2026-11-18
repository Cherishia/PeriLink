using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LinkScope
{
    internal sealed class SavedBatteryRule
    {
        internal string Channel;
        internal bool Feature,Scale;
        internal byte Id;
        internal int Length,Offset;
        internal readonly Dictionary<int,byte> Guard=new Dictionary<int,byte>();
        internal int? Read(byte[] report)
        {
            if(report.Length!=Length||report[0]!=Id||Guard.Any(p=>report[p.Key]!=p.Value)) return null;
            int value=Scale?(int)Math.Round(report[Offset]*100d/255,MidpointRounding.AwayFromZero):report[Offset];
            return value<=100?(int?)value:null;
        }
    }

    internal sealed class SavedProfiles
    {
        private const int ReadBudgetMs=320;
        private readonly Dictionary<string,SavedBatteryRule> rules=new Dictionary<string,SavedBatteryRule>();

        internal SavedProfiles(string file)
        {
            try
            {
                if(!File.Exists(file)) return;
                foreach(string line in File.ReadLines(file).Take(128))
                {
                    string[] a=line.Split('\t');int id,len,offset;
                    if(a.Length!=8 || a[0].Length!=32 || !int.TryParse(a[3],out id)||id<0||id>255
                        ||!int.TryParse(a[4],out len)||len<2||len>1024||!int.TryParse(a[5],out offset)||offset<=0||offset>=len) continue;
                    var rule=new SavedBatteryRule {Channel=a[1],Feature=a[2]=="1",Id=(byte)id,Length=len,Offset=offset,Scale=a[6]=="1"};
                    bool valid=true;
                    foreach(string word in a[7].Split(new[]{','},StringSplitOptions.RemoveEmptyEntries))
                    {
                        string[] parts=word.Split(':');int at;byte match;
                        if(parts.Length!=2||!int.TryParse(parts[0],out at)||at<=0||at>=len||
                            !byte.TryParse(parts[1],NumberStyles.HexNumber,CultureInfo.InvariantCulture,out match)) {valid=false;break;}
                        rule.Guard[at]=match;
                    }
                    if(valid) rules[a[0]]=rule;
                }
            }
            catch(IOException) { }
            catch(UnauthorizedAccessException) { }
        }
        internal SavedBatteryRule Find(string id)
        {
            SavedBatteryRule rule;
            return rules.TryGetValue(Preferences.Key(id),out rule)?rule:null;
        }

        internal void Apply(List<Device> devices)
        {
            if(rules.Count==0) return;
            foreach(var device in devices.Where(x=>!Tracking.HasBattery(x)&&x.Model!=null))
            {
                var rule=Find(device.Id);
                if(rule==null) continue;
                device.BatteryCapable=true;
                int? value=Read(device,rule);
                if(!value.HasValue) continue;
                device.Battery=value;
                device.Source="已保存的电量适配";
            }
        }

        private static int? Read(Device device,SavedBatteryRule rule)
        {
            int? value=null;
            var timer=Stopwatch.StartNew();
            foreach(string path in Receiver.HidPaths().Where(p=>Hardware.Model(p)==device.Model).Take(64))
            {
                if(timer.ElapsedMilliseconds>=ReadBudgetMs) break;
                string[] id=Hardware.IdFromPath(path).Split('\\');
                if(id.Length<3||!string.Equals(id[1],rule.Channel,StringComparison.OrdinalIgnoreCase)||
                    !string.Equals(Hardware.PeripheralIdFromInterface(path,device.Model),device.Group,StringComparison.OrdinalIgnoreCase)) continue;
                using(var meta=Receiver.Open(path,0))
                {
                    IntPtr preparsed;
                    if(meta.IsInvalid||!Receiver.HidD_GetPreparsedData(meta,out preparsed)) continue;
                    try
                    {
                        byte[] caps=new byte[64];
                        if(Receiver.HidP_GetCaps(preparsed,caps)!=0x110000||BitConverter.ToUInt16(caps,2)<0xff00||
                            BitConverter.ToUInt16(caps,rule.Feature?8:4)!=rule.Length) continue;
                        byte[] report=new byte[rule.Length];
                        using(var read=Receiver.Open(path,0x80000000,!rule.Feature))
                        {
                            if(rule.Feature)
                            {
                                report[0]=rule.Id;
                                if(Receiver.HidD_GetFeature(read.IsInvalid?meta:read,report,report.Length)
                                    &&!Merge(rule.Read(report),ref value)) return null;
                            }
                            else if(!read.IsInvalid)
                            {
                                while(timer.ElapsedMilliseconds<ReadBudgetMs)
                                {
                                    if(!Receiver.Transfer(read,report,false,Math.Max(1,ReadBudgetMs-(int)timer.ElapsedMilliseconds))) break;
                                    if(!Merge(rule.Read(report),ref value)) return null;
                                }
                            }
                        }
                    }
                    finally {Receiver.HidD_FreePreparsedData(preparsed);}
                }
            }
            return value;
        }

        private static bool Merge(int? reading,ref int? previous)
        {
            if(!reading.HasValue) return true;
            if(previous.HasValue&&reading!=previous) return false;
            previous=reading;
            return true;
        }
    }
}
