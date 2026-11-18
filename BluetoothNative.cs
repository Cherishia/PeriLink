using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LinkScope
{
    internal static class BluetoothNative
    {
        private static readonly Guid ServiceInterface=new Guid("6e3bb679-4372-40c8-9eaa-4509df260cd8");
        private static readonly Guid BatteryUuid=new Guid("00002a19-0000-1000-8000-00805f9b34fb");
        private const int MoreData=unchecked((int)0x800700EA);

        internal static void Scan(List<Device> devices, ScanLog log)
        {
            try
            {
                ScanClassic(devices);
                foreach (string path in Receiver.InterfacePaths(ServiceInterface))
                {
                    if (!Hardware.Has(path,"0000180f-0000-1000-8000-00805f9b34fb")) continue;
                    string address=Hardware.AddressFromInterface(path);
                    if(address==null) continue;
                    var related=devices.Where(d=>d.Group=="BT:"+address && !d.Battery.HasValue).ToList();
                    if(related.Count==0) continue;
                    foreach(var d in related) d.BatteryCapable=true;
                    using(var h=Receiver.Open(path,0xc0000000,false))
                    {
                        if(h.IsInvalid) continue;
                        int? level=ReadCachedBattery(h);
                        if(!level.HasValue) continue;
                        foreach(var d in related) { d.Battery=level; d.Source="BLE 电池服务 · 系统缓存"; }
                    }
                }
            }
            catch(Exception e) { log.BluetoothNativeError=e.GetType().Name; }
        }

        private static void ScanClassic(List<Device> devices)
        {
            var search=new Search
            {
                Size=Marshal.SizeOf(typeof(Search)), Authenticated=1, Remembered=1, Connected=1
            };
            var info=new Info { Size=Marshal.SizeOf(typeof(Info)) };
            IntPtr iterator=BluetoothFindFirstDevice(ref search,ref info);
            if(iterator==IntPtr.Zero) return;
            try
            {
                do
                {
                    if(info.Address==0 || string.IsNullOrWhiteSpace(info.Name)) continue;
                    string group="BT:"+info.Address.ToString("X12");
                    var matches=devices.Where(d=>d.Group==group).ToList();
                    if(matches.Count==0)
                    {
                        var d=new Device { Id=group,Group=group,Link="蓝牙",Kind=ClassOfDevice(info.Class,info.Name),Connected=false };
                        matches.Add(d); devices.Add(d);
                    }
                    foreach(var d in matches)
                    {
                        d.Name=info.Name;
                        if(d.Kind=="其他") d.Kind=ClassOfDevice(info.Class,info.Name);
                        if(info.Connected!=0 || d.Note!="蓝牙设备当前已连接") d.Connected=info.Connected!=0;
                        d.Note=d.Connected ? "蓝牙设备当前已连接" : "已配对，当前未连接";
                    }
                } while(BluetoothFindNextDevice(iterator,ref info));
            }
            finally { BluetoothFindDeviceClose(iterator); }
        }

        internal static string ClassOfDevice(uint code,string name)
        {
            uint major=(code>>8)&31, minor=(code>>2)&63;
            if(major==4) return "音频";
            if(major==5)
            {
                if((minor&16)!=0) return "键盘";
                if((minor&32)!=0) return "鼠标";
                if((minor&15)==1 || (minor&15)==2) return "手柄";
            }
            return Hardware.Guess(name);
        }

        private static int? ReadCachedBattery(SafeFileHandle service)
        {
            ushort count;
            if(BluetoothGATTGetCharacteristics(service,IntPtr.Zero,0,IntPtr.Zero,out count,0)!=MoreData || count==0 || count>256) return null;
            int size=Marshal.SizeOf(typeof(Characteristic));
            IntPtr chars=Marshal.AllocHGlobal(size*count);
            try
            {
                ushort actual;
                if(BluetoothGATTGetCharacteristics(service,IntPtr.Zero,count,chars,out actual,0)!=0 || actual>count) return null;
                int? lowest=null;
                for(int i=0;i<actual;i++)
                {
                    IntPtr p=IntPtr.Add(chars,i*size);
                    var c=(Characteristic)Marshal.PtrToStructure(p,typeof(Characteristic));
                    if(c.Readable==0 || (c.IsShort!=0 ? (c.Uuid.ToByteArray()[0] | c.Uuid.ToByteArray()[1]<<8)!=0x2a19 : c.Uuid!=BatteryUuid)) continue;
                    byte[] value=new byte[16];
                    ushort written;
                    const uint cache=0x08;
                    if(BluetoothGATTGetCharacteristicValue(service,p,(uint)value.Length,value,out written,cache)==0
                        && BitConverter.ToInt32(value,0)==1 && value[4]<=100)
                        lowest=lowest.HasValue ? Math.Min(lowest.Value,value[4]) : value[4];
                }
                return lowest;
            }
            finally { Marshal.FreeHGlobal(chars); }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Search
        {
            public int Size, Authenticated, Remembered, Unknown, Connected, Inquiry;
            public byte Timeout;
            public IntPtr Radio;
        }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        private struct Info
        {
            public int Size;
            public ulong Address;
            public uint Class;
            public int Connected, Remembered, Authenticated;
            public ulong LastSeen1,LastSeen2,LastUsed1,LastUsed2;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=248)] public string Name;
        }
        [StructLayout(LayoutKind.Explicit,Size=36)]
        private struct Characteristic
        {
            [FieldOffset(4)] public byte IsShort;
            [FieldOffset(8)] public Guid Uuid;
            [FieldOffset(29)] public byte Readable;
        }
        [DllImport("bthprops.cpl",SetLastError=true)] private static extern IntPtr BluetoothFindFirstDevice(ref Search search,ref Info info);
        [DllImport("bthprops.cpl",SetLastError=true)] private static extern bool BluetoothFindNextDevice(IntPtr search,ref Info info);
        [DllImport("bthprops.cpl")] private static extern bool BluetoothFindDeviceClose(IntPtr search);
        [DllImport("BluetoothApis.dll")] private static extern int BluetoothGATTGetCharacteristics(SafeFileHandle h,IntPtr service,ushort count,IntPtr buffer,out ushort actual,uint flags);
        [DllImport("BluetoothApis.dll")] private static extern int BluetoothGATTGetCharacteristicValue(SafeFileHandle h,IntPtr c,uint bytes,[Out] byte[] value,out ushort required,uint flags);
    }
}
