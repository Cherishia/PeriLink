using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace LinkScope
{
    internal static class Bluetooth
    {
        const string Connected = "System.Devices.Aep.IsConnected";
        const string Address = "System.Devices.Aep.DeviceAddress";
        const string Percent = "System.Devices.BatteryLife";
        const string Manufacturer = "System.Devices.Aep.Manufacturer";
        static readonly Guid BatteryService = new Guid("0000180f-0000-1000-8000-00805f9b34fb");
        static readonly Guid BatteryLevel = new Guid("00002a19-0000-1000-8000-00805f9b34fb");
        static readonly string[] Properties = { Connected, Address, Percent, Manufacturer, "System.Devices.Aep.Category", "System.Devices.Aep.IsPaired" };

        internal static void Scan(List<Device> devices, bool query, ScanLog log)
        {
            ScanAsync(devices, query, log).GetAwaiter().GetResult();
        }

        private static async Task ScanAsync(List<Device> devices, bool query, ScanLog log)
        {
            var le = Find(true, log);
            var classic = Find(false, log);
            var entries = new[] { await le.ConfigureAwait(false), await classic.ConfigureAwait(false) };
            var reads = new List<Task>();
            for (int i=0; i < entries.Length; i++)
            {
                if (entries[i] == null) continue;
                foreach (var d in entries[i])
                {
                    if (string.IsNullOrWhiteSpace(d.Name)) continue;
                    object value;
                    bool online = d.Properties.TryGetValue(Connected, out value) && value is bool && (bool)value;
                    string address = d.Properties.TryGetValue(Address, out value) ? Hardware.BluetoothAddress(Convert.ToString(value)) : null;
                    if (address == null && value is string && System.Text.RegularExpressions.Regex.IsMatch((string)value, "^[0-9A-Fa-f]{12}$")) address = ((string)value).ToUpperInvariant();
                    address = address ?? Hardware.BluetoothAddress(d.Id);
                    string group = address == null ? d.Id : "BT:" + address;
                    var related = devices.Where(x => x.Group == group).ToList();
                    if (related.Count == 0)
                    {
                        string kind = Hardware.Guess(d.Name);
                        if (kind == "其他" && d.Properties.TryGetValue("System.Devices.Aep.Category", out value))
                            kind = Hardware.Guess(value is string[] ? string.Join(" ",(string[])value) : value as string);
                        var entry = new Device { Id = group, Group = group, Name = d.Name, Kind = kind, Link = "蓝牙", Connected = false };
                        devices.Add(entry);
                        related.Add(entry);
                    }
                    int level = 101;
                    if (d.Properties.TryGetValue(Percent, out value) && value != null)
                    {
                        int parsed;
                        if (int.TryParse(Convert.ToString(value), out parsed)) level = parsed;
                    }
                    string manufacturer=d.Properties.TryGetValue(Manufacturer,out value) ? value as string : null;
                    foreach (var entry in related)
                    {
                        if (online || entry.Note != "蓝牙设备当前已连接") entry.Connected = online;
                        entry.Name = d.Name;
                        if(!string.IsNullOrWhiteSpace(manufacturer)) entry.Manufacturer=manufacturer;
                        entry.Note = entry.Connected ? "蓝牙设备当前已连接" : "已配对，当前未连接";
                        if (level >= 0 && level <= 100 && (online || !entry.Battery.HasValue))
                        { entry.Battery = level; entry.Source = "Windows 蓝牙缓存"; }
                    }
                    if (query && i == 0 && online && reads.Count < 8)
                        reads.Add(ReadBattery(d.Id, related));
                }
            }
            await Task.WhenAll(reads).ConfigureAwait(false);
        }

        private static async Task<DeviceInformationCollection> Find(bool le, ScanLog log)
        {
            try
            {
                string selector = le ? BluetoothLEDevice.GetDeviceSelectorFromPairingState(true) : BluetoothDevice.GetDeviceSelectorFromPairingState(true);
                return await Wait(DeviceInformation.FindAllAsync(selector, Properties, DeviceInformationKind.AssociationEndpoint), 2400).ConfigureAwait(false);
            }
            catch (Exception e) { log.BleError = e.GetType().Name; return null; }
        }

        private static async Task ReadBattery(string id, List<Device> devices)
        {
            try
            {
                using (var device = await Wait(BluetoothLEDevice.FromIdAsync(id), 1800).ConfigureAwait(false))
                {
                    if (device == null) return;
                    var services = await Wait(device.GetGattServicesForUuidAsync(BatteryService, BluetoothCacheMode.Cached), 1900).ConfigureAwait(false);
                    if (services == null || services.Status != GattCommunicationStatus.Success) return;
                    var values = new List<int>();
                    foreach (var service in services.Services)
                    {
                        using (service)
                        {
                            var characteristics = await Wait(service.GetCharacteristicsForUuidAsync(BatteryLevel, BluetoothCacheMode.Cached), 1500).ConfigureAwait(false);
                            if (characteristics == null || characteristics.Status != GattCommunicationStatus.Success) continue;
                            foreach (var characteristic in characteristics.Characteristics)
                            {
                                if ((characteristic.CharacteristicProperties & GattCharacteristicProperties.Read) == 0) continue;
                                foreach(var entry in devices) entry.BatteryCapable=true;
                                var result = await Wait(characteristic.ReadValueAsync(BluetoothCacheMode.Uncached), 1800).ConfigureAwait(false);
                                if (result == null || result.Status != GattCommunicationStatus.Success || result.Value.Length != 1) continue;
                                using (var data = DataReader.FromBuffer(result.Value))
                                {
                                    byte value = data.ReadByte();
                                    if (value <= 100) values.Add(value);
                                }
                            }
                        }
                    }
                    if (values.Count == 0) return;
                    foreach (var entry in devices)
                    {
                        entry.Battery = values.Min();
                        entry.Source = values.Count > 1 ? "BLE 电池服务 · 取最低值" : "BLE 电池服务 · 实时";
                    }
                }
            }
            catch (Exception e) { IgnoreUnavailable(e); }
        }

        private static void IgnoreUnavailable(Exception e)
        {
            if (!(e is COMException) && !(e is UnauthorizedAccessException) && !(e is System.IO.FileNotFoundException)
                && !(e is InvalidOperationException) && !(e is ArgumentException)) throw e;
        }

        private static async Task<T> Wait<T>(IAsyncOperation<T> op, int timeout)
        {
            var watch = Stopwatch.StartNew();
            while (op.Status == AsyncStatus.Started && watch.ElapsedMilliseconds < timeout)
                await Task.Delay(25).ConfigureAwait(false);
            if (op.Status == AsyncStatus.Completed) return op.GetResults();
            if (op.Status == AsyncStatus.Started) op.Cancel();
            return default(T);
        }
    }
}
