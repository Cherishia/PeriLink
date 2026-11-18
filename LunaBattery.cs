using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace LinkScope
{
    internal static class LunaBattery
    {
        private const int ReportSize=65; // Windows includes the report ID before the 64-byte payload.
        internal static string ModelName(string model)
        {
            if(model=="373E:0032" || model=="373E:0033") return "LunaFury LUNA33";
            if(model=="373E:0054" || model=="373E:0084") return "LunaFury TYPE33";
            return null;
        }
        internal static bool IsReceiver(string model) { return model=="373E:0033" || model=="373E:0084"; }

        internal static int? Read(string path,string model,out bool charging)
        {
            charging=false;
            using(var h=Receiver.Open(path,0xc0000000,false))
            {
                if(h.IsInvalid) return null;
                var input=new byte[ReportSize];
                input[3]=IsReceiver(model) ? (byte)2 : (byte)0;
                input[4]=2;
                input[6]=0x83;
                if(!HidD_SetFeature(h,input,input.Length)) return null;
                var output=new byte[ReportSize];
                for(int i=0;i<7;i++)
                {
                    Thread.Sleep(i<3 ? 30 : 95);
                    output[0]=0;
                    if(!HidD_GetFeature(h,output,output.Length)) break;
                    int? battery=Decode(output,out charging);
                    if(battery.HasValue || output[1]==0xa2) return battery;
                }
                return null;
            }
        }

        internal static int? Decode(byte[] data,out bool charging)
        {
            charging=false;
            if(data.Length<9 || data[0]!=0 || data[1]!=0xa1 || data[4]<2 || data[5]!=0
                || data[6]!=0x83 || data[8]>100) return null;
            charging=data[7]==1;
            return data[8];
        }

        [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetFeature(SafeFileHandle h,[In,Out] byte[] data,int length);
        [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)] private static extern bool HidD_SetFeature(SafeFileHandle h,byte[] data,int length);
    }
}
