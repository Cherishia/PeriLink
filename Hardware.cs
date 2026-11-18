using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace LinkScope
{
    internal sealed class Device
    {
        public string Id, Group, Name, Kind, Link, Source, Level, Note;
        public string Model, Manufacturer;
        public int? Battery;
        public DateTime? BatteryReadAt;
        public bool RetainedBattery;
        public bool BatteryCapable;
        public int Pad = -1;
        public bool Connected = true;
        public readonly HashSet<IntPtr> Inputs = new HashSet<IntPtr>();
        public string Power { get { return Battery.HasValue ? Battery + "%" : (Level ?? "—"); } }
        public override string ToString() { return Name; }
    }

    internal sealed class Node
    {
        public string Id, Name, Class, BusName, Manufacturer;
        public uint Instance;
        public int? Battery;
    }

    internal sealed class RawDevice
    {
        public string Path, Id, Kind;
        public ushort Page, Usage;
        public IntPtr Handle;
    }

    internal static class Hardware
    {
        internal static bool Has(string value, string pattern)
        { return value != null && value.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0; }

        internal static bool IsWatchName(string name)
        {
            if(string.IsNullOrWhiteSpace(name)) return false;
            return Has(name,"手表") || Has(name,"手环") || System.Text.RegularExpressions.Regex.IsMatch(name,
                @"\b(?:watch|smartwatch|smart[ -]?band|fitness[ -]?band|amazfit|forerunner|fenix|instinct|vívoactive|vivoactive|venu)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase|System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        }

        internal static string Guess(string name)
        {
            if (new[] { "headset", "headphone", "earbud", "speaker", "stereo", "airpods", "audio", "a2dp", "音响", "音箱", "耳机", "扬声器" }.Any(x => Has(name, x))) return "音频";
            if (new[] { "controller", "gamepad", "joystick", "xbox", "dualsense", "8bitdo", "手柄" }.Any(x => Has(name, x))) return "手柄";
            if (Has(name,"keyboard") || Has(name,"键盘")) return "键盘";
            if (Has(name,"mouse") || Has(name,"鼠标") || Has(name,"trackball")) return "鼠标";
            if (IsWatchName(name)) return "手表";
            return "其他";
        }

        internal static string IdFromPath(string path)
        {
            string start = path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\??\", StringComparison.Ordinal) ? path.Substring(4) : path;
            string[] fields = start.Split('#');
            return fields.Length >= 3 ? string.Join("\\", fields.Take(3)) : start;
        }

        internal static List<Device> Scan(List<RawDevice> raw, bool queryBle, ScanLog log)
        {
            var nodes = EnumerateNodes();
            log.NodeCount = nodes.Count;
            var byId = nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
            var byInstance = nodes.ToDictionary(n => n.Instance);
            var devices = new List<Device>();
            foreach (RawDevice item in raw)
            {
                if (item.Kind == null) continue;
                Node node;
                byId.TryGetValue(item.Id, out node);
                var trail = Ancestors(node, byInstance).ToList();
                var bluetooth = trail.FirstOrDefault(n => IsBluetooth(n.Id) && BluetoothAddress(n.Id)!=null) ?? trail.FirstOrDefault(n => IsBluetooth(n.Id));
                var usb = PeripheralUsbNode(trail);
                string address = bluetooth == null ? null : BluetoothAddress(bluetooth.Id);
                string group = address == null ? ((usb ?? node) == null ? item.Id : (usb ?? node).Id) : "BT:" + address;
                string model = Model(usb == null ? item.Id : usb.Id);
                string lunaName=LunaBattery.ModelName(model);
                if (lunaName!=null && item.Kind!="鼠标" || model=="1CA6:300A" && item.Kind!="键盘") continue;
                string link = bluetooth != null ? "蓝牙" : usb != null || Has(item.Id, "VID_")
                    ? (IsReceiver(model, BestName(usb,node)) ? "2.4G" : "USB") : "系统 HID";
                Device entry = devices.FirstOrDefault(d => d.Group == group && d.Kind == item.Kind);
                if (entry == null)
                {
                    string name = lunaName ?? (model=="1CA6:300A" ? "AE64 Pro" : BestName(bluetooth, usb, node)) ?? Receiver.ProductName(item.Path);
                    if (string.IsNullOrWhiteSpace(name)) name = item.Kind + "设备" + (model==null ? "" : " · "+model);
                    entry = new Device { Id = group + "/" + item.Kind, Group = group, Link = link, Kind = item.Kind, Name = name, Model = model,
                        Manufacturer=usb==null ? null : usb.Manufacturer,
                        BatteryCapable=lunaName!=null, Note = "Windows 输入接口" };
                    if (link == "USB") entry.Note = "Windows 识别为 USB，可能是有线或未标明的接收器";
                    if (link == "2.4G") entry.Note = "USB 接收器已接入，型号和电量取决于接收器的上报能力";
                    if(RogOmni.IsReceiver(model))
                    {
                        entry.Name="ROG OMNI "+item.Kind+"接口";
                        entry.Note="共享接收器可能同时暴露键盘和鼠标接口";
                    }
                    devices.Add(entry);
                }
                entry.Inputs.Add(item.Handle);
            }

            foreach (Node node in nodes.Where(n => IsBluetooth(n.Id)))
            {
                string address = BluetoothAddress(node.Id);
                if (address == null) continue;
                string group = "BT:" + address;
                List<Device> matches = devices.Where(d => d.Group == group).ToList();
                string kind = Guess(node.Name);
                if (matches.Count == 0 && (node.Id.StartsWith("BTHENUM\\DEV_", StringComparison.OrdinalIgnoreCase) || node.Id.StartsWith("BTHLE\\DEV_", StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrWhiteSpace(node.Name))
                {
                    var entry = new Device { Id = group, Group = group, Name = node.Name, Link = "蓝牙", Kind = kind, Connected = false, Note = "Windows 配对记录，连接状态待确认" };
                    devices.Add(entry);
                    matches.Add(entry);
                }
                if (node.Battery.HasValue)
                    foreach (Device entry in matches.Where(d => !d.Battery.HasValue))
                    { entry.Battery = node.Battery; entry.Source = "Windows 蓝牙缓存"; }
            }
            Bluetooth.Scan(devices, queryBle, log);
            AddAudio(devices, nodes, byInstance);
            BluetoothNative.Scan(devices,log);
            var cached=nodes.Where(n=>n.Battery.HasValue).Select(n=>new { Address=BluetoothAddress(n.Id), Level=n.Battery })
                .Where(x=>x.Address!=null).GroupBy(x=>x.Address).ToDictionary(g=>g.Key,g=>g.Min(x=>x.Level));
            foreach (var d in devices.Where(d=>d.Link=="蓝牙" && !d.Battery.HasValue && d.Group.StartsWith("BT:")))
            {
                int? level;
                if(cached.TryGetValue(d.Group.Substring(3),out level)) { d.Battery=level; d.Source="Windows 蓝牙缓存"; }
            }
            AddPads(devices);
            try { Receiver.Scan(devices); }
            catch (Exception e) { log.ReceiverError = e.GetType().Name; }

            MergeHandsFreeAudio(devices);
            return devices.OrderByDescending(d => d.Connected).ThenBy(d => d.Link == "蓝牙" || d.Link == "2.4G" ? 0 : 1)
                .ThenBy(d => d.Kind).ThenBy(d => d.Name).ToList();
        }

        internal static void MergeHandsFreeAudio(List<Device> devices)
        {
            const string suffix=" Hands-Free AG";
            var audio=devices.Where(d=>d.Link=="蓝牙" && d.Kind=="音频").ToList();
            foreach(var profile in audio.Where(d=>d.Name.EndsWith(suffix,StringComparison.OrdinalIgnoreCase)))
            {
                string name=profile.Name.Substring(0,profile.Name.Length-suffix.Length);
                var matches=audio.Where(d=>string.Equals(d.Name,name,StringComparison.OrdinalIgnoreCase)).ToList();
                if(matches.Count!=1 || audio.Count(d=>string.Equals(d.Name,profile.Name,StringComparison.OrdinalIgnoreCase))!=1) continue;
                var device=matches[0];
                if(device.Group.StartsWith("BT:",StringComparison.Ordinal) && profile.Group.StartsWith("BT:",StringComparison.Ordinal)
                    && !string.Equals(device.Group,profile.Group,StringComparison.OrdinalIgnoreCase)) continue;
                if(!device.Battery.HasValue && profile.Battery.HasValue) { device.Battery=profile.Battery;device.Source=profile.Source; }
                device.Connected=device.Connected || profile.Connected;
                devices.Remove(profile);
            }
        }

        internal static string BestName(params Node[] nodes)
        {
            return nodes.Where(n=>n!=null).SelectMany(n=>new[] { n.BusName,n.Name }).FirstOrDefault(UsefulName);
        }

        internal static bool UsefulName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && !new[] { "HID-compliant", "HID Keyboard Device", "Composite Device",
                "复合设备", "USB 输入设备", "USB Input Device", "符合 HID", "HID 标准", "通用 USB", "USB Audio Device" }.Any(x=>Has(name,x));
        }

        internal static Node PeripheralUsbNode(IEnumerable<Node> trail)
        {
            // USB interfaces sit above the HID node and below the composite device. Stop before the parent hub.
            var branch=trail.SkipWhile(n=>!n.Id.StartsWith(@"USB\VID_",StringComparison.OrdinalIgnoreCase)).ToList();
            if(branch.Count==0) return null;
            string model=Model(branch[0].Id);
            return branch.TakeWhile(n=>n.Id.StartsWith(@"USB\VID_",StringComparison.OrdinalIgnoreCase) && Model(n.Id)==model).Last();
        }

        internal static string Model(string id)
        {
            var m = System.Text.RegularExpressions.Regex.Match(id ?? "", "VID_([0-9A-F]{4}).*?PID_([0-9A-F]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return m.Success ? (m.Groups[1].Value + ":" + m.Groups[2].Value).ToUpperInvariant() : null;
        }

        internal static bool IsReceiver(string model, string name)
        {
            return Receiver.Slots(model) > 0 || RazerBattery.IsReceiver(model) || LunaBattery.IsReceiver(model) || RogOmni.IsReceiver(model) || model == "045E:02E6" || model == "045E:02FE"
                || new[] { "2.4g", "receiver", "wireless adapter", "dongle", "接收器", "无线适配器" }.Any(s => Has(name, s));
        }

        private static bool IsBluetooth(string id)
        {
            return id.StartsWith("BTHLE", StringComparison.OrdinalIgnoreCase) || id.StartsWith("BTHENUM\\", StringComparison.OrdinalIgnoreCase);
        }

        internal static string BluetoothAddress(string id)
        {
            const System.Text.RegularExpressions.RegexOptions caseInsensitive=System.Text.RegularExpressions.RegexOptions.IgnoreCase;
            var text=System.Text.RegularExpressions.Regex.Replace(id??"",@"\{[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\}","",caseInsensitive);
            foreach(string pattern in new[] { "(?<=DEV_)[0-9a-f]{12}(?![0-9a-f])",
                "(?:[0-9a-f]{2}:){5}[0-9a-f]{2}(?![0-9a-f])",
                "(?:^|[#&-])[0-9a-f]{12}(?![0-9a-f])" })
            {
                var matches=System.Text.RegularExpressions.Regex.Matches(text,pattern,caseInsensitive);
                for(int i=matches.Count-1;i>=0;i--)
                {
                    string address=matches[i].Value.TrimStart('#','&','-').Replace(":","").ToUpperInvariant();
                    if(address!="000000000000") return address;
                }
            }
            return null;
        }

        internal static string AddressFromInterface(string path)
        {
            uint instance;
            if (CM_Locate_DevNode(out instance,IdFromPath(path),0)==0)
            {
                var name=new StringBuilder(1024);
                for(int depth=0;depth<12;depth++)
                {
                    if(CM_Get_Device_ID(instance,name,name.Capacity,0)==0 && IsBluetooth(name.ToString()))
                    {
                        string address=BluetoothAddress(name.ToString());
                        if(address!=null) return address;
                    }
                    uint parent;
                    if(CM_Get_Parent(out parent,instance,0)!=0) break;
                    instance=parent;
                }
            }
            return BluetoothAddress(IdFromPath(path));
        }

        internal static string PeripheralIdFromInterface(string path,string model)
        {
            uint instance;
            if(CM_Locate_DevNode(out instance,IdFromPath(path),0)!=0) return null;
            var buffer=new StringBuilder(1024);
            string result=null;
            for(int depth=0;depth<12;depth++)
            {
                if(CM_Get_Device_ID(instance,buffer,buffer.Capacity,0)!=0) break;
                string id=buffer.ToString();
                if(id.StartsWith(@"USB\VID_",StringComparison.OrdinalIgnoreCase))
                {
                    if(Model(id)==model) result=id;
                    else if(result!=null) break;
                }
                uint parent;
                if(CM_Get_Parent(out parent,instance,0)!=0) break;
                instance=parent;
            }
            return result;
        }

        private static IEnumerable<Node> Ancestors(Node n, Dictionary<uint,Node> index)
        {
            for (int i = 0; n != null && i < 12; i++)
            {
                yield return n;
                uint parent;
                if (CM_Get_Parent(out parent, n.Instance, 0) != 0 || !index.TryGetValue(parent, out n)) yield break;
            }
        }

        private static void AddAudio(List<Device> devices, List<Node> nodes, Dictionary<uint,Node> index)
        {

            foreach (Node node in nodes.Where(n => string.Equals(n.Class, "MEDIA", StringComparison.OrdinalIgnoreCase)))
            {
                var trail = Ancestors(node, index).ToList();
                Node bt = trail.FirstOrDefault(n => IsBluetooth(n.Id) && BluetoothAddress(n.Id)!=null) ?? trail.FirstOrDefault(n => IsBluetooth(n.Id));
                Node usb = PeripheralUsbNode(trail);
                if (bt == null && usb == null) continue;
                string address = bt == null ? null : BluetoothAddress(bt.Id);
                string group = address != null ? "BT:" + address : (bt ?? usb).Id;
                var entry = devices.FirstOrDefault(d => d.Group == group && d.Kind == "音频");
                if (entry != null) continue;
                var existing = devices.FirstOrDefault(d => d.Group == group && d.Kind == "其他");
                if (existing != null) { existing.Kind = "音频"; continue; }
                string model = usb == null ? null : Model(usb.Id);
                string name=BestName(bt, bt==null ? usb : null,node) ?? "音频设备";
                if (bt!=null && devices.Any(d=>d.Kind=="音频" && d.Link=="蓝牙" && Hardware.Has(name,d.Name) && d.Name.Length>=3)) continue;
                devices.Add(new Device { Id = group + "/音频", Group = group, Name = name, Kind = "音频",
                    Link = bt != null ? "蓝牙" : IsReceiver(model, name) ? "2.4G" : "USB", Model = bt==null ? model : null,
                    Manufacturer=usb==null ? null : usb.Manufacturer, Note = "Windows 音频接口" });
            }
        }

        private static void AddPads(List<Device> devices)
        {
            for (uint slot = 0; slot < 4; slot++)
            {
                XState state;
                if (XInputGetState(slot, out state) != 0) continue;
                XBattery battery;
                var d = new Device { Id = "XInput/" + slot, Group = "XInput", Name = "XInput 手柄 " + (slot + 1), Kind = "手柄",
                    Link = "XInput", Pad = (int)slot, Note = "Windows 手柄槽位，连接方式由驱动管理" };
                if (XInputGetBatteryInformation(slot, 0, out battery) == 0)
                {
                    if (battery.Type == 1) d.Level = "外部供电";
                    else if ((battery.Type == 2 || battery.Type == 3) && battery.Level < 4)
                    { d.Level = new[] { "空", "低", "中", "满" }[battery.Level]; d.Source = "XInput 四档电量"; }
                }
                devices.Add(d);
            }
        }

        private static List<Node> EnumerateNodes()
        {
            var result = new List<Node>();
            IntPtr set = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, 6);
            if (set == new IntPtr(-1)) throw new System.ComponentModel.Win32Exception();
            try
            {
                var id = new StringBuilder(1024);
                for (uint i = 0; ; i++)
                {
                    var data = new DevInfo { Size = Marshal.SizeOf(typeof(DevInfo)) };
                    if (!SetupDiEnumDeviceInfo(set, i, ref data))
                    {
                        if (Marshal.GetLastWin32Error() != 259) throw new System.ComponentModel.Win32Exception();
                        break;
                    }
                    id.Length = 0;
                    if (!SetupDiGetDeviceInstanceId(set, ref data, id, id.Capacity, IntPtr.Zero)) continue;
                    string ident = id.ToString();
                    int? battery = null;
                    if (IsBluetooth(ident))
                    {
                        var key = new PropertyKey { Format = new Guid("49cd1f76-5626-4b17-a4e8-18b4aa1a2213"), Id = 10 };
                        byte[] bytes = new byte[16];
                        uint size = (uint)bytes.Length, type;
                        uint status=CM_Get_DevNode_Property(data.Instance, ref key, out type, bytes, ref size, 0);
                        if (status!=0)
                        {
                            key = new PropertyKey { Format=new Guid("104ea319-6ee2-4701-bd47-8ddbf425bbe5"), Id=2 };
                            size=(uint)bytes.Length;
                            status=CM_Get_DevNode_Property(data.Instance, ref key, out type, bytes, ref size, 0);
                        }
                        if (status==0)
                        {
                            uint value = type == 3 && size == 1 ? bytes[0] : type == 7 && size == 4 ? BitConverter.ToUInt32(bytes,0) : uint.MaxValue;
                            if (value <= 100) battery = (int)value;
                        }
                    }
                    bool usb=ident.StartsWith(@"USB\VID_",StringComparison.OrdinalIgnoreCase);
                    result.Add(new Node {Id=ident, Instance=data.Instance, Class=RegistryText(set, ref data, 7),
                        Name = RegistryText(set, ref data, 12) ?? RegistryText(set, ref data, 0), Battery = battery,
                        BusName=usb ? BusDescription(data.Instance) : null, Manufacturer=usb ? RegistryText(set, ref data, 11) : null });
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return result;
        }

        private static string RegistryText(IntPtr set, ref DevInfo data, uint property)
        {
            byte[] bytes = new byte[1024];
            uint type, size;
            return SetupDiGetDeviceRegistryProperty(set, ref data, property, out type, bytes, (uint)bytes.Length, out size)
                && size > 2 && type == 1 ? Encoding.Unicode.GetString(bytes, 0, (int)size).TrimEnd('\0') : null;
        }

        private static string BusDescription(uint instance)
        {
            var key=new PropertyKey { Format=new Guid("540b947e-8b40-45bc-a8a2-6a0b894cbda2"),Id=4 };
            byte[] data=new byte[512];
            uint type,size=(uint)data.Length;
            return CM_Get_DevNode_Property(instance,ref key,out type,data,ref size,0)==0 && type==18 && size>=2
                ? Encoding.Unicode.GetString(data,0,(int)size).TrimEnd('\0').Trim() : null;
        }

        [StructLayout(LayoutKind.Sequential)] private struct DevInfo { public int Size; public Guid Class; public uint Instance; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public uint Id; }
        [DllImport("setupapi.dll", EntryPoint="SetupDiGetClassDevsW",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr SetupDiGetClassDevs(IntPtr g, string e, IntPtr h, uint flags);
        [DllImport("setupapi.dll",SetLastError=true)] private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref DevInfo data);
        [DllImport("setupapi.dll",EntryPoint="SetupDiGetDeviceInstanceIdW",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref DevInfo data, StringBuilder s, int count, IntPtr needed);
        [DllImport("setupapi.dll",EntryPoint="SetupDiGetDeviceRegistryPropertyW",SetLastError=true)] private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set, ref DevInfo data, uint property, out uint type, byte[] bytes, uint len, out uint needed);
        [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_Parent(out uint parent, uint child, uint flags);
        [DllImport("cfgmgr32.dll",EntryPoint="CM_Locate_DevNodeW",CharSet=CharSet.Unicode)] private static extern uint CM_Locate_DevNode(out uint device,string id,uint flags);
        [DllImport("cfgmgr32.dll",EntryPoint="CM_Get_Device_IDW",CharSet=CharSet.Unicode)] private static extern uint CM_Get_Device_ID(uint device,StringBuilder id,int len,uint flags);
        [DllImport("cfgmgr32.dll",EntryPoint="CM_Get_DevNode_PropertyW")] private static extern uint CM_Get_DevNode_Property(uint node, ref PropertyKey key, out uint type, byte[] bytes, ref uint size, uint flags);
        [StructLayout(LayoutKind.Sequential)] internal struct XState { public uint Packet; public ushort Buttons; public byte LeftTrigger,RightTrigger; public short LX,LY,RX,RY; }
        [StructLayout(LayoutKind.Sequential)] private struct XBattery { public byte Type,Level; }
        [DllImport("xinput1_4.dll")] internal static extern uint XInputGetState(uint index,out XState state);
        [DllImport("xinput1_4.dll")] private static extern uint XInputGetBatteryInformation(uint index,byte type,out XBattery battery);
    }

    internal sealed class ScanLog
    {
        public int NodeCount;
        public string BleError, BluetoothNativeError, ReceiverError;

    }
}
