using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace LinkScope
{
    internal static class Receiver
    {
        internal static int Slots(string model)
        {
            switch (model)
            {
                case "046D:C52B": case "046D:C532": case "046D:C534": case "046D:C548": return 6;
                case "046D:C52F": case "046D:C531": case "046D:C537": case "046D:C539": case "046D:C53A":
                case "046D:C53D": case "046D:C541": case "046D:C542": case "046D:C543": case "046D:C547": case "046D:C54D":
                case "046D:C54E": case "046D:C54F": return 1;
                default: return 0;
            }
        }

        internal static void Scan(List<Device> devices)
        {
            var rogChannels=new Dictionary<string,RogOmni.Channels>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in HidPaths())
            {
                string id = Hardware.IdFromPath(path);
                string model = Hardware.Model(id);
                int slots = Slots(model);
                using (var handle = Open(path, 0))
                {
                    if (handle.IsInvalid) continue;
                    IntPtr pp;
                    if (!HidD_GetPreparsedData(handle,out pp)) continue;
                    try
                    {
                        var caps = new byte[64];
                        if (HidP_GetCaps(pp,caps) != 0x110000) continue;
                        ushort usage = U16(caps,0), page = U16(caps,2);
                        string group=model==null ? null : Hardware.PeripheralIdFromInterface(path,model);
                        var matching = devices.Where(d=>d.Model==model && group!=null && string.Equals(d.Group,group,StringComparison.OrdinalIgnoreCase)).ToList();
                        if(RogOmni.IsReceiver(model) && group!=null && matching.Count!=0 &&
                            (page==0xff02 || page==0xff00) && U16(caps,4)==64 && U16(caps,6)==64)
                        {
                            RogOmni.Channels channels;
                            if(!rogChannels.TryGetValue(group,out channels)) rogChannels[group]=channels=new RogOmni.Channels();
                            if(page==0xff02) channels.Pairing=path; else channels.Keyboard=path;
                        }
                        if (matching.Count == 0 && model != null)
                        {
                            // Use a VID/PID match only when exactly one physical device exposes that model.
                            var modelMatches=devices.Where(d=>d.Model == model).ToList();
                            if (modelMatches.Select(d=>d.Group).Distinct().Count() == 1) matching=modelMatches;
                        }
                        if (slots > 0 && page == 0xff00 && (usage == 1 || usage == 2) && U16(caps,4) >= 20 && U16(caps,6) >= 20)
                        {
                            ReadReceiver(path, slots, U16(caps,4), U16(caps,6), matching, devices);
                            continue;
                        }
                        if(LunaBattery.ModelName(model)!=null && page>=0xff00 && U16(caps,8)==65)
                        {
                            var mouse=matching.Where(d=>d.Kind=="鼠标").ToList();
                            if(mouse.Count==1 && !mouse[0].Battery.HasValue)
                            {
                                bool charging;
                                int? percent=LunaBattery.Read(path,model,out charging);
                                if(percent.HasValue)
                                {
                                    mouse[0].Battery=percent;
                                    mouse[0].Source=charging ? "LunaFury · 充电中" : "LunaFury 电量";
                                }
                            }
                        }
                        if (RazerBattery.IsReceiver(model) && U16(caps,8)==91)
                        {
                            var mouse=matching.Where(d=>d.Kind=="鼠标").ToList();
                            if (mouse.Count==1 && !mouse[0].Battery.HasValue)
                            {
                                mouse[0].BatteryCapable=true;
                                int? percent=RazerBattery.Read(path,model);
                                if(percent.HasValue) { mouse[0].Battery=percent; mouse[0].Source="Razer 接收器"; }
                            }
                        }
                        if (matching.Count == 1 && !matching[0].Battery.HasValue)
                            ReadStandard(handle, pp, U16(caps,8), matching[0]);
                    }
                    finally { HidD_FreePreparsedData(pp); }
                }
            }
            foreach(var pair in rogChannels) RogOmni.Apply(devices,pair.Key,pair.Value);
        }

        private static void ReadStandard(SafeFileHandle h, IntPtr pp, int length, Device d)
        {
            if (length < 2 || length > 1024) return;
            byte[] caps = new byte[72*16];
            ushort count = 16;
            if (HidP_GetSpecificValueCaps(2,6,0,0x20,caps,ref count,pp) != 0x110000) return;
            for (int i=0;i<count && i<16;i++)
            {
                int offset=i*72;
                int min=BitConverter.ToInt32(caps,offset+40), max=BitConverter.ToInt32(caps,offset+44);
                if (min < 0 || max <= min || U16(caps,offset+20) != 1) continue;
                byte[] data=new byte[length];
                data[0]=caps[offset+2];
                if (!HidD_GetFeature(h,data,data.Length)) continue;
                uint value;
                if (HidP_GetUsageValue(2,6,U16(caps,offset+6),0x20,out value,pp,data,data.Length) == 0x110000
                    && value >= min && value <= max)
                {
                    d.Battery=(int)Math.Round((value-min)*100.0/(max-min));
                    d.Source="标准 USB HID 电量";
                    return;
                }
            }
        }

        private static void ReadReceiver(string path,int slots,int input,int output,List<Device> matching,List<Device> devices)
        {
            using (var h=Open(path,0xc0000000))
            {
                if (h.IsInvalid) return;
                var pending=new List<Device>();
                for (byte slot=1;slot<=slots;slot++)
                {
                    byte[] feature=Request(h,input,output,slot,0,0,0x10,0x04);
                    bool modern=feature!=null && feature[4]!=0;
                    if (!modern) feature=Request(h,input,output,slot,0,0,0x10,0x00);
                    if (feature==null || feature[4]==0) continue;
                    byte[] battery=Request(h,input,output,slot,feature[4],modern ? 1 : 0);
                    if (battery==null) continue;
                    int? value=battery[4]<=100 ? (int?)battery[4] : null;
                    string grade=null;
                    if (modern)
                    {
                        byte[] caps=Request(h,input,output,slot,feature[4],0);
                        if (caps==null || (caps[5]&2)==0)
                        {
                            value=null;
                            grade=(battery[5]&8)!=0 ? "满" : (battery[5]&4)!=0 ? "中" : (battery[5]&2)!=0 ? "低" : (battery[5]&1)!=0 ? "极低" : null;
                        }
                    }
                    if (!value.HasValue && grade==null) continue;
                    string kind="其他", name=null;
                    byte[] info=Request(h,input,output,slot,0,0,0x00,0x05);
                    if (info!=null && info[4]!=0)
                    {
                        byte idx=info[4];
                        byte[] type=Request(h,input,output,slot,idx,2);
                        if (type!=null) kind=type[4]==0 || type[4]==2 ? "键盘" : type[4]==3 || type[4]==4 ? "鼠标" : "其他";
                        byte[] size=Request(h,input,output,slot,idx,0);
                        if (size!=null && size[4]>0 && size[4]<=64)
                        {
                            var bytes=new List<byte>();
                            for (byte at=0;at<size[4];at+=16)
                            {
                                byte[] part=Request(h,input,output,slot,idx,1,at);
                                if (part==null) break;
                                bytes.AddRange(part.Skip(4).Take(Math.Min(Math.Min(16,size[4]-at),part.Length-4)));
                            }
                            if (bytes.Count==size[4]) name=Encoding.UTF8.GetString(bytes.ToArray()).Trim('\0',' ');
                        }
                    }
                    pending.Add(new Device { Id=path+"/slot/"+slot, Group=path, Link="2.4G", Name=string.IsNullOrWhiteSpace(name) ? "罗技接收器 · 槽位 "+slot : name,
                        Kind=kind,Battery=value,Level=grade,Source="Logitech HID++",Note="接收器查询收到设备回应" });
                }
                foreach (Device d in pending)
                {
                    var matches=matching.Where(x=>x.Kind==d.Kind).ToList();
                    if (matches.Count==1 && pending.Count(x=>x.Kind==d.Kind)==1)
                    {
                        Device match=matches[0];
                        match.Name=d.Name.StartsWith("罗技接收器") ? match.Name : d.Name;
                        match.Battery=d.Battery; match.Level=d.Level; match.Source=d.Source; match.Note=d.Note;
                    }
                    else if (!devices.Any(x=>x.Id==d.Id)) devices.Add(d);
                }
            }
        }

        private static byte[] Request(SafeFileHandle h,int input,int output,byte slot,byte feature,int fn,params byte[] parameters)
        {
            byte[] command=new byte[output];
            command[0]=0x11; command[1]=slot; command[2]=feature; command[3]=(byte)((fn<<4)|0x0D);
            Array.Copy(parameters,0,command,4,parameters.Length);
            var watch=Stopwatch.StartNew();
            if (!Transfer(h,command,true,100)) return null;
            byte[] response=new byte[input];
            while (watch.ElapsedMilliseconds<125 && Transfer(h,response,false,Math.Max(1,125-(int)watch.ElapsedMilliseconds)))
            {
                if (response[1]!=slot) continue;
                if ((response[0]==0x11 || response[0]==0x10) && response[2]==feature && response[3]==command[3]) return response;
                if ((response[2]==0xff || response[2]==0x8f) && response[3]==feature && response[4]==command[3]) return null;
            }
            return null;
        }

        internal static bool Transfer(SafeFileHandle file,byte[] data,bool write,int ms)
        {
            using (var ready = new System.Threading.EventWaitHandle(false,System.Threading.EventResetMode.ManualReset))
            {
                IntPtr native=Marshal.AllocHGlobal(3*IntPtr.Size+8);
                GCHandle buffer=GCHandle.Alloc(data, GCHandleType.Pinned);
                try
                {
                    for(int i=0;i<3*IntPtr.Size+8;i++) Marshal.WriteByte(native,i,0);
                    Marshal.WriteIntPtr(native,2*IntPtr.Size+8,ready.SafeWaitHandle.DangerousGetHandle());
                    IntPtr p=buffer.AddrOfPinnedObject();
                    bool complete=write ? WriteFile(file,p,data.Length,IntPtr.Zero,native) : ReadFile(file,p,data.Length,IntPtr.Zero,native);
                    if (!complete)
                    {
                        if (Marshal.GetLastWin32Error()!=997) return false;
                        if (!ready.WaitOne(ms))
                        {
                            CancelIoEx(file,native);
                            int ignored;
                            GetOverlappedResult(file,native,out ignored,true);
                            return false;
                        }
                    }
                    int bytes;
                    return GetOverlappedResult(file,native,out bytes,false) && bytes>0;
                }
                finally { buffer.Free(); Marshal.FreeHGlobal(native); }
            }
        }

        internal static List<string> HidPaths()
        {
            Guid guid;
            HidD_GetHidGuid(out guid);
            return InterfacePaths(guid);
        }

        internal static List<string> InterfacePaths(Guid guid)
        {
            IntPtr set=OpenInterfaces(ref guid,null,IntPtr.Zero,18);
            var result=new List<string>();
            if (set==new IntPtr(-1)) return result;
            try
            {
                for(uint i=0;;i++)
                {
                    var info=new InterfaceData { Size=Marshal.SizeOf(typeof(InterfaceData)) };
                    if(!SetupDiEnumDeviceInterfaces(set,IntPtr.Zero,ref guid,i,ref info)) break;
                    int bytes;
                    SetupDiGetDeviceInterfaceDetail(set,ref info,IntPtr.Zero,0,out bytes,IntPtr.Zero);
                    if(bytes<8 || bytes>16384) continue;
                    IntPtr block=Marshal.AllocHGlobal(bytes);
                    try
                    {
                        Marshal.WriteInt32(block,IntPtr.Size==8?8:6);
                        if(SetupDiGetDeviceInterfaceDetail(set,ref info,block,bytes,out bytes,IntPtr.Zero))
                            result.Add(Marshal.PtrToStringUni(IntPtr.Add(block,4)));
                    }
                    finally { Marshal.FreeHGlobal(block); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return result;
        }
        private static ushort U16(byte[] a,int off) { return BitConverter.ToUInt16(a,off); }
        internal static SafeFileHandle Open(string path,uint access,bool overlapped=true)
        { return CreateFile(path,access,3,IntPtr.Zero,3,access!=0 && overlapped ? 0x40000000u : 0,IntPtr.Zero); }
        internal static string ProductName(string path)
        {
            using(var h=Open(path,0))
            {
                if(h.IsInvalid) return null;
                var data=new byte[512];
                if(!HidD_GetProductString(h,data,data.Length)) return null;
                string name=Encoding.Unicode.GetString(data).TrimEnd('\0').Trim();
                return Hardware.UsefulName(name) ? name : null;
            }
        }
        [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetProductString(SafeFileHandle h,[Out] byte[] data,int length);
        [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_GetPreparsedData(SafeFileHandle h,out IntPtr p);
        [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_FreePreparsedData(IntPtr p);
        [DllImport("hid.dll")] internal static extern int HidP_GetCaps(IntPtr pp,[Out] byte[] caps);
        [DllImport("hid.dll")] private static extern int HidP_GetSpecificValueCaps(int type,ushort page,ushort link,ushort usage,[Out] byte[] caps,ref ushort count,IntPtr pp);
        [DllImport("hid.dll")] private static extern int HidP_GetUsageValue(int type,ushort page,ushort link,ushort usage,out uint value,IntPtr pp,byte[] data,int length);
        [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_GetFeature(SafeFileHandle h,[In,Out] byte[] data,int length);
        [DllImport("kernel32",EntryPoint="CreateFileW",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr sec,uint mode,uint flags,IntPtr template);
        [DllImport("kernel32",SetLastError=true)] private static extern bool ReadFile(SafeFileHandle h,IntPtr b,int n,IntPtr read,IntPtr overlap);
        [DllImport("kernel32",SetLastError=true)] private static extern bool WriteFile(SafeFileHandle h,IntPtr b,int n,IntPtr written,IntPtr overlap);
        [DllImport("kernel32",SetLastError=true)] private static extern bool CancelIoEx(SafeFileHandle h,IntPtr overlap);
        [DllImport("kernel32",SetLastError=true)] private static extern bool GetOverlappedResult(SafeFileHandle h,IntPtr overlap,out int bytes,bool wait);
        [StructLayout(LayoutKind.Sequential)] private struct InterfaceData { public int Size; public Guid Guid; public int Flags; public IntPtr Reserved; }
        [DllImport("setupapi",EntryPoint="SetupDiGetClassDevsW",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr OpenInterfaces(ref Guid guid,string filter,IntPtr window,uint flags);
        [DllImport("setupapi",SetLastError=true)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set,IntPtr info,ref Guid g,uint index,ref InterfaceData data);
        [DllImport("setupapi",EntryPoint="SetupDiGetDeviceInterfaceDetailW",SetLastError=true)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set,ref InterfaceData info,IntPtr data,int size,out int needed,IntPtr device);
        [DllImport("setupapi")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    }
}
