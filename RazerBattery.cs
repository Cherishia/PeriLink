using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace LinkScope
{
    internal static class RazerBattery
    {
        // Razer dongles with the Chroma 91-byte feature-report battery query.
        private static readonly HashSet<string> Receivers=new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "1532:007B","1532:00C1","1532:00DF","1532:00B7","1532:00C3","1532:00BF",
            "1532:00E6","1532:00AB","1532:00CD","1532:00D7","1532:00C5","1532:00D1",
            "1532:00C8","1532:00D4","1532:00B0","1532:00DB","1532:00B4","1532:00B8",
            "1532:00B9","1532:00B3","1532:009A","1532:005A","1532:0045"
        };
        internal static bool IsReceiver(string model) { return model!=null && Receivers.Contains(model); }

        internal static int? Read(string path,string model)
        {
            using(var h=Receiver.Open(path,0x40000000,false))
            {
                if(h.IsInvalid) return null;
                byte transaction=model=="1532:007B" || model=="1532:0045" ? (byte)0xff : (byte)0x1f;
                byte[] attempts={transaction,0x20};
                foreach(byte tx in attempts)
                {
                    var request=Packet(tx);
                    if(!HidD_SetFeature(h,request,request.Length)) continue;
                    var response=new byte[91];
                    var watch=Stopwatch.StartNew();
                    while(watch.ElapsedMilliseconds<105)
                    {
                        Thread.Sleep(8);
                        response[0]=0;
                        if(!HidD_GetFeature(h,response,response.Length)) break;
                        if(response[2]!=tx || response[7]!=7 || response[8]!=0x80) continue;
                        if(response[1]==2 && response[6]>=2 && response[6]<=80)
                            return Percent(response[10]);
                        if(response[1]>=3) break;
                    }
                }
                return null;
            }
        }

        internal static int Percent(byte raw) { return (int)Math.Round(raw*100.0/255); }
        internal static byte[] Packet(byte transaction)
        {
            var data=new byte[91];
            data[2]=transaction;
            data[6]=2; data[7]=7; data[8]=0x80;
            for(int i=3;i<=88;i++) data[89]^=data[i];
            return data;
        }

        [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetFeature(SafeFileHandle h,[In,Out] byte[] data,int length);
        [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)] private static extern bool HidD_SetFeature(SafeFileHandle h,byte[] data,int length);
    }
}
