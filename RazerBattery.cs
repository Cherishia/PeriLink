using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace LinkScope
{
    internal static class RazerBattery
    {
        // USB identifier pairs published by OpenRazer. Shared receivers are not in this table.
        private static readonly Dictionary<string,string> RadioForUsb=new Dictionary<string,string>
        {
            {"1532:0070","1532:006F"}, {"1532:0073","1532:0072"}, {"1532:007A","1532:007B"},
            {"1532:007C","1532:007D"}, {"1532:0080","1532:0077"}, {"1532:0086","1532:0088"},
            {"1532:008F","1532:0090"}, {"1532:009E","1532:009F"}, {"1532:00A5","1532:00A6"},
            {"1532:00A7","1532:00A8"}, {"1532:00AA","1532:00AB"}, {"1532:00AF","1532:00B0"},
            {"1532:00B6","1532:00B7"}, {"1532:00BE","1532:00BF"}, {"1532:00C0","1532:00C1"},
            {"1532:00C2","1532:00C3"}, {"1532:00C4","1532:00C5"}, {"1532:00C7","1532:00C8"},
            {"1532:00CC","1532:00CD"}, {"1532:00D0","1532:00D1"}, {"1532:00D3","1532:00D4"},
            {"1532:00D6","1532:00D7"}, {"1532:00DA","1532:00DB"}, {"1532:00DE","1532:00DF"},
            {"1532:00E5","1532:00E6"}
        };
        private static readonly Dictionary<string,string> UsbForRadio=RadioForUsb.ToDictionary(p=>p.Value,p=>p.Key);

        // Razer dongles with the Chroma 91-byte feature-report battery query.
        private static readonly HashSet<string> Receivers=new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "1532:007B","1532:00C1","1532:00DF","1532:00B7","1532:00C3","1532:00BF",
            "1532:00E6","1532:00AB","1532:00CD","1532:00D7","1532:00C5","1532:00D1",
            "1532:00C8","1532:00D4","1532:00B0","1532:00DB","1532:00B4","1532:00B8",
            "1532:00B9","1532:00B3","1532:009A","1532:005A","1532:0045"
        };
        internal static bool IsReceiver(string model) { return model!=null && (Receivers.Contains(model) || UsbForRadio.ContainsKey(model)); }
        internal static bool IsWired(string model) { return model!=null && RadioForUsb.ContainsKey(model); }
        internal static string WiredModel(string model)
        {
            string wired;
            if(IsWired(model)) return model;
            return model!=null && UsbForRadio.TryGetValue(model,out wired) ? wired : null;
        }

        private static byte Transaction(string model)
        {
            switch(model)
            {
                case "1532:007A": case "1532:007B": case "1532:0045": return 0xff;
                case "1532:0072": case "1532:0073": case "1532:007C": case "1532:007D": case "1532:005A": return 0x3f;
                default: return 0x1f;
            }
        }

        internal static void Apply(string path,Device mouse,bool compareUnit)
        {
            mouse.BatteryCapable=true;
            using(var handle=Receiver.Open(path,0x40000000,false))
            {
                if(handle.IsInvalid) return;
                byte transaction=Transaction(mouse.Model);
                foreach(byte tx in new byte[]{transaction,0x20})
                {
                    var battery=Query(handle,tx,7,0x80,2);
                    if(battery==null) continue;
                    if(!mouse.Battery.HasValue)
                    {
                        mouse.Battery=Percent(battery[10]);
                        var status=IsWired(mouse.Model) ? Query(handle,tx,7,0x84,2) : null;
                        mouse.Source=status!=null && status[10]==1 ? "Razer · 充电中" : "Razer 电量";
                    }
                    transaction=tx;
                    break;
                }
                if(compareUnit && mouse.UnitKey==null)
                {
                    var reply=Query(handle,transaction,0,0x82,22);
                    if(reply!=null)
                        mouse.UnitKey=DecodeUnit(mouse.Model,reply);
                }
            }
        }

        internal static string DecodeUnit(string model,byte[] reply)
        {
            string wired=WiredModel(model);
            if(wired==null || reply.Length<31) return null;
            string serial=Encoding.ASCII.GetString(reply,9,22).Trim('\0',' ').ToUpperInvariant();
            return serial.Length>=8 && serial.All(c=>(c>='0' && c<='9') || (c>='A' && c<='Z')) &&
                serial.Any(c=>c!=serial[0]) ? wired+"/"+serial : null;
        }

        private static byte[] Query(SafeFileHandle file,byte transaction,byte category,byte command,byte size)
        {
            var request=Packet(transaction,category,command,size);
            if(!HidD_SetFeature(file,request,request.Length)) return null;
            var response=new byte[91];
            var watch=Stopwatch.StartNew();
            while(watch.ElapsedMilliseconds<105)
            {
                Thread.Sleep(8);
                response[0]=0;
                if(!HidD_GetFeature(file,response,response.Length)) break;
                if(response[2]!=transaction || response[7]!=category || response[8]!=command) continue;
                if(response[1]==2 && response[6]>=size && response[6]<=80) return response;
                if(response[1]>=3) break;
            }
            return null;
        }

        internal static int Percent(byte raw) { return (int)Math.Round(raw*100.0/255); }
        internal static byte[] Packet(byte transaction,byte category=7,byte command=0x80,byte size=2)
        {
            var data=new byte[91];
            data[2]=transaction;
            data[6]=size; data[7]=category; data[8]=command;
            for(int i=3;i<=88;i++) data[89]^=data[i];
            return data;
        }

        [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetFeature(SafeFileHandle h,[In,Out] byte[] data,int length);
        [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)] private static extern bool HidD_SetFeature(SafeFileHandle h,byte[] data,int length);
    }
}
