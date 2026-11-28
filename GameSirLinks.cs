using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LinkScope
{
    internal sealed class GameSirLink
    {
        internal readonly string UsbId,BluetoothKey;
        internal GameSirLink(string usbId,string bluetoothKey) {UsbId=usbId;BluetoothKey=bluetoothKey;}
    }

    internal sealed class GameSirLinks
    {
        private readonly string path;
        private List<GameSirLink> links=new List<GameSirLink>();

        internal GameSirLinks(string path)
        {
            this.path=path;
            try
            {
                if(!File.Exists(path)) return;
                foreach(string line in File.ReadLines(path).Take(64))
                {
                    var fields=line.Split('\t');
                    if(fields.Length!=2 || !ValidUsbId(fields[0]) || !Regex.IsMatch(fields[1],@"^[0-9A-F]{32}$") ||
                        links.Any(p=>SameId(p.UsbId,fields[0]) || p.BluetoothKey==fields[1])) continue;
                    links.Add(new GameSirLink(fields[0],fields[1]));
                }
            }
            catch(IOException) {}
            catch(UnauthorizedAccessException) {}
        }

        private static bool IsWiredNova(Device device)
        {
            return device.Link=="USB" && device.Model==GameSirBattery.Nova2LiteUsb &&
                device.Kind=="手柄" && device.Connected && ValidUsbId(device.Id);
        }

        internal static bool IsBluetoothPad(Device device)
        {
            return device.Link=="蓝牙" && device.Kind=="手柄" &&
                Regex.IsMatch(device.Group??"", @"^BT:[0-9A-F]{12}$",RegexOptions.IgnoreCase);
        }

        internal void RecognizeAndApply(List<Device> devices)
        {
            RecognizeAndApply(devices,GameSirBluetooth.IsNova2Lite);
        }

        internal void RecognizeAndApply(List<Device> devices,Func<string,bool> isNovaBluetooth)
        {
            var bluetooth=devices.Where(IsBluetoothPad).GroupBy(d=>Preferences.Key(d.Group),StringComparer.Ordinal)
                .ToDictionary(g=>g.Key,g=>g.OrderByDescending(d=>d.Connected).ThenByDescending(Tracking.HasBattery).ToList(),StringComparer.Ordinal);
            var wired=devices.Where(IsWiredNova).ToList();
            var confirmed=bluetooth.Where(p=>links.Any(l=>l.BluetoothKey==p.Key) ||
                isNovaBluetooth(p.Value[0].Group.Substring(3))).ToList();

            // A model signature alone can't pair two active controllers or choose between multiple units.
            if(wired.Count==1 && confirmed.Count==1 && !confirmed[0].Value[0].Connected && links.Count<64 &&
                !links.Any(p=>SameId(p.UsbId,wired[0].Id) || p.BluetoothKey==confirmed[0].Key))
            {
                links.Add(new GameSirLink(wired[0].Id,confirmed[0].Key));
                try {Save();}
                catch(IOException) {}
                catch(UnauthorizedAccessException) {}
            }

            foreach(var pair in links)
            {
                var cable=wired.FirstOrDefault(d=>SameId(d.Id,pair.UsbId));
                List<Device> matching;
                bluetooth.TryGetValue(pair.BluetoothKey,out matching);
                var wireless=matching==null ? null : matching[0];
                if(cable!=null && wireless!=null && wireless.Connected) continue;
                var card=cable??wireless;
                if(card==null) continue;
                if(wireless!=null) card.AlternateId=wireless.Id;
                card.Id=pair.UsbId;
                NameCard(card);
                if(matching!=null)
                    foreach(var duplicate in matching.Where(d=>d!=card)) devices.Remove(duplicate);
            }

            foreach(var entry in confirmed)
            {
                var card=entry.Value[0];
                if(!devices.Contains(card) || ValidUsbId(card.Id)) continue;
                card.AlternateId=SameId(card.Id,card.Group)?card.Group+"/手柄":card.Id;
                card.Id=card.Group;
                NameCard(card);
                foreach(var duplicate in entry.Value.Skip(1)) devices.Remove(duplicate);
            }
        }

        private static void NameCard(Device card)
        {
            card.Name=GameSirBattery.Nova2LiteName;
            card.Manufacturer="GameSir";
            card.BatteryCapable=true;
        }

        private void Save()
        {
            string temporary=path+".tmp";
            File.WriteAllLines(temporary,links.Select(p=>p.UsbId+"\t"+p.BluetoothKey));
            if(File.Exists(path)) File.Replace(temporary,path,null);
            else File.Move(temporary,path);
        }

        private static bool ValidUsbId(string id)
        {
            return !string.IsNullOrEmpty(id) && id.Length<=512 && id.IndexOfAny(new[]{'\t','\r','\n'})<0 &&
                Hardware.Model(id)==GameSirBattery.Nova2LiteUsb && id.EndsWith("/手柄",StringComparison.Ordinal);
        }

        private static bool SameId(string first,string second) {return string.Equals(first,second,StringComparison.OrdinalIgnoreCase);}
    }
}
