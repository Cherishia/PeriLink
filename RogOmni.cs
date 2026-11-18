using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace LinkScope
{
    internal static class RogOmni
    {
        internal static bool IsReceiver(string model) { return model=="0B05:1ACE" || model=="0B05:1ACF"; }
        internal sealed class Channels
        {
            public string Pairing,Keyboard;
        }

        internal static bool AzothOnly(byte[] frame)
        {
            return frame!=null && frame.Length>=11 && frame[0]==1 && frame[1]==0xa0 && frame[2]==0
                && frame[5]==0x85 && frame[6]==0x1a && frame[9]==0 && frame[10]==0;
        }

        internal static void Apply(List<Device> all,string group,Channels channels)
        {
            if(channels.Pairing==null || !AzothOnly(Query(channels.Pairing,1,0xa0,0))) return;
            var keyboard=all.FirstOrDefault(d=>d.Group==group && d.Kind=="键盘");
            if(keyboard==null) return;
            all.RemoveAll(d=>d.Group==group && d.Kind=="鼠标");
            keyboard.Name="ROG Azoth 夜魔";
            keyboard.BatteryCapable=true;
            keyboard.Note="ROG OMNI 已保存该键盘的配对";
            if(channels.Keyboard==null) return;
            byte[] answer=Query(channels.Keyboard,2,0x12,1);
            if(answer==null || answer[6]>100 || answer[6]==0 && answer[9]==0) return;
            keyboard.Battery=answer[6];
            keyboard.Source="ROG OMNI 电量";
        }

        private static byte[] Query(string path,byte report,byte command,byte parameter)
        {
            using(var handle=Receiver.Open(path,0xc0000000))
            {
                if(handle.IsInvalid) return null;
                var request=new byte[64];
                request[0]=report; request[1]=command; request[2]=parameter;
                if(!Receiver.Transfer(handle,request,true,160)) return null;
                var timer=Stopwatch.StartNew();
                while(timer.ElapsedMilliseconds<180)
                {
                    var reply=new byte[64];
                    if(!Receiver.Transfer(handle,reply,false,Math.Max(1,180-(int)timer.ElapsedMilliseconds))) break;
                    if(reply[0]==report && reply[1]==command && (command==0xa0 || reply[2]==parameter)) return reply;
                }
                return null;
            }
        }
    }
}
