using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace LinkScope
{
    internal static class RogOmni
    {
        internal const string AzothName="ROG Azoth 夜魔";
        internal const string HarpeAceMiniName="ROG Harpe Ace Mini";
        private const ushort HarpeAceMiniPairId=0x1b65, AzothPairId=0x1a85;
        internal static bool IsReceiver(string model) { return model=="0B05:1ACE" || model=="0B05:1ACF"; }
        internal static bool IsUnpairedInterface(Device device) { return IsReceiver(device.Model) && device.PairedModel==null; }
        internal static bool IsDedicatedReceiver(string model) { return model=="0B05:1A85" || model=="0B05:1C25"; }
        internal static string KeyboardName(string model)
        {
            switch(model)
            {
                case "0B05:1A83": case "0B05:1A85": return AzothName;
                case "0B05:1C24": case "0B05:1C25": return "ROG Azoth X";
                default: return null;
            }
        }

        internal static string KeyboardModel(Device keyboard)
        { return KeyboardName(keyboard.PairedModel??keyboard.Model); }
        internal sealed class Channels
        {
            public string Pairing,Keyboard,Mouse;
        }

        internal static bool HasPairedProduct(byte[] frame,ushort product)
        {
            if(frame==null || frame.Length<9 || frame[0]!=1 || frame[1]!=0xa0 || frame[2]!=0) return false;
            // OMNI exposes one mouse and one keyboard. Each pairing entry occupies four bytes.
            for(int offset=5;offset<=9 && offset+4<=frame.Length;offset+=4)
            {
                int id=frame[offset]|(frame[offset+1]<<8);
                if(id==0) break;
                if(id==product) return true;
            }
            return false;
        }

        internal static void Apply(List<Device> all,string group,Channels channels,Func<string,byte,byte,byte,byte[]> read=null)
        {
            if(channels.Pairing==null) return;
            if(read==null) read=(path,report,command,parameter)=>Query(path,report,command,parameter);
            var paired=read(channels.Pairing,1,0xa0,0);
            if(HasPairedProduct(paired,AzothPairId))
            {
                var keyboard=PairedDevice(all,group,"键盘",AzothName);
                keyboard.PairedModel="0B05:1A83";
                if(channels.Keyboard!=null)
                    ApplyBattery(keyboard,read(channels.Keyboard,2,0x12,1),"ROG OMNI 电量");
            }

            if(!HasPairedProduct(paired,HarpeAceMiniPairId)) return;
            var mouse=PairedDevice(all,group,"鼠标",HarpeAceMiniName);
            mouse.PairedModel="0B05:1B65";
            if(channels.Mouse!=null) ApplyHarpeBattery(mouse,read(channels.Mouse,3,0x12,7));
        }

        private static Device PairedDevice(List<Device> all,string group,string kind,string name)
        {
            var device=all.FirstOrDefault(d=>d.Kind==kind && d.Link=="2.4G" &&
                string.Equals(d.Group,group,StringComparison.OrdinalIgnoreCase));
            if(device==null)
            {
                device=new Device { Id=group+"/"+kind,Group=group,Kind=kind,Link="2.4G",Model=Hardware.Model(group) };
                all.Add(device);
            }
            device.Name=name;
            device.BatteryCapable=true;
            device.Note="ROG OMNI 已保存配对";
            return device;
        }

        internal static void ApplyHarpeBattery(Device mouse,byte[] answer)
        {
            if(answer==null || answer.Length<11 || answer[0]!=3 || answer[1]!=0x12 || answer[2]!=7 ||
                answer[5]>100 || answer[5]==0 && answer[10]==0) return;
            mouse.Battery=answer[5];
            mouse.Source=answer[10]!=0 ? "ROG OMNI 电量 · 充电中" : "ROG OMNI 电量";
        }

        internal static void ApplyDirect(string path,int length,Device keyboard)
        {
            // OpenAzoth documents the normal USB reports and the 12 01 power query.
            byte report=length==65 ? (byte)0 : (byte)2;
            ApplyBattery(keyboard,Query(path,report,0x12,1,length),"ROG 电量");
        }

        private static void ApplyBattery(Device keyboard,byte[] answer,string source)
        {
            if(answer==null || answer[6]>100 || answer[6]==0 && answer[9]==0) return;
            keyboard.Battery=answer[6];
            keyboard.Source=answer[9]==1 ? source+" · 充电中" : source;
        }

        private static byte[] Query(string path,byte report,byte command,byte parameter,int length=64)
        {
            using(var handle=Receiver.Open(path,0xc0000000))
            {
                if(handle.IsInvalid) return null;
                var request=new byte[length];
                request[0]=report; request[1]=command; request[2]=parameter;
                if(!Receiver.Transfer(handle,request,true,160)) return null;
                var timer=Stopwatch.StartNew();
                while(timer.ElapsedMilliseconds<180)
                {
                    var reply=new byte[length];
                    if(!Receiver.Transfer(handle,reply,false,Math.Max(1,180-(int)timer.ElapsedMilliseconds))) break;
                    if(reply[0]==report && reply[1]==command && (command==0xa0 || reply[2]==parameter)) return reply;
                }
                return null;
            }
        }
    }
}
