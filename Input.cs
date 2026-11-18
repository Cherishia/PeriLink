using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace LinkScope
{
    internal static class Input
    {
        [StructLayout(LayoutKind.Sequential)] private struct Item { public IntPtr Handle; public uint Type; }
        [DllImport("user32",SetLastError=true)] private static extern uint GetRawInputDeviceList([Out] Item[] list, ref uint count, uint size);
        [DllImport("user32",EntryPoint="GetRawInputDeviceInfoW",CharSet=CharSet.Unicode)] private static extern uint DeviceInfo(IntPtr h,uint command,StringBuilder data,ref uint size);
        [DllImport("user32",EntryPoint="GetRawInputDeviceInfoW")] private static extern uint DeviceInfo(IntPtr h,uint command,byte[] data,ref uint size);

        internal static List<RawDevice> Catalog()
        {
            uint size = (uint)Marshal.SizeOf(typeof(Item));
            for (int attempt=0; attempt<3; attempt++)
            {
                uint count = 0;
                if (GetRawInputDeviceList(null, ref count, size) == uint.MaxValue || count == 0) break;
                var list = new Item[count];
                uint actual = GetRawInputDeviceList(list,ref count,size);
                if (actual == uint.MaxValue) continue;
                var devices = new List<RawDevice>();
                foreach (var n in list.Take((int)actual))
                {
                    uint len = 0;
                    DeviceInfo(n.Handle,0x20000007,(StringBuilder)null,ref len);
                    if (len == 0) continue;
                    var path = new StringBuilder((int)len + 1);
                    if (DeviceInfo(n.Handle,0x20000007,path,ref len) == uint.MaxValue || path.Length == 0) continue;
                    string kind = n.Type == 0 ? "鼠标" : n.Type == 1 ? "键盘" : null;
                    ushort page = 0, usage = 0;
                    if (n.Type == 2)
                    {
                        byte[] data = new byte[32];
                        uint bytes = 32;
                        data[0] = 32;
                        if (DeviceInfo(n.Handle,0x2000000B,data,ref bytes) != uint.MaxValue)
                        {
                            page = BitConverter.ToUInt16(data, 20);
                            usage = BitConverter.ToUInt16(data, 22);
                            if (page == 1 && (usage == 4 || usage == 5 || usage == 8)) kind = "手柄";
                        }
                    }
                    devices.Add(new RawDevice { Handle=n.Handle, Path=path.ToString(),Id=Hardware.IdFromPath(path.ToString()),Kind=kind,Page=page,Usage=usage });
                }
                return devices;
            }
            return new List<RawDevice>();
        }
    }
}
