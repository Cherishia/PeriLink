using System;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace LinkScope
{
    internal static class GameSirBluetooth
    {
        internal static bool IsNova2Lite(string address)
        {
            const string devices=@"SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\Devices\";
            try
            {
                using(var device=Registry.LocalMachine.OpenSubKey(devices+address))
                {
                    if(device==null) return false;
                    foreach(var name in new[]{"CachedServices","DynamicCachedServices"})
                    using(var services=device.OpenSubKey(name))
                    {
                        if(services==null) continue;
                        foreach(string handle in services.GetValueNames())
                            if(MatchesDeviceIdRecord(services.GetValue(handle) as byte[])) return true;
                    }
                }
            }
            catch(IOException) { }
            catch(SecurityException) { }
            catch(UnauthorizedAccessException) { }
            return false;
        }

        internal static bool MatchesDeviceIdRecord(byte[] record)
        {
            if(record==null || record.Length==0 || record[0]>>3!=6) return false;
            int offset=0,length;
            if(!ReadElement(record,ref offset,out length) || offset+length!=record.Length) return false;

            int serviceClass=0,vendor=0,product=0,version=0,vendorSource=0,lastId=-1;
            while(offset<record.Length)
            {
                if(record.Length-offset<4 || record[offset]!=0x09) return false;
                int id=ReadUInt16(record,offset+1);
                if(id<=lastId) return false;
                lastId=id;
                offset+=3;
                byte type=record[offset];
                if(!ReadElement(record,ref offset,out length)) return false;
                if(id==0x0001 && type>>3==6 && length==3 && record[offset]==0x19)
                    serviceClass=ReadUInt16(record,offset+1);
                if(type==0x09)
                {
                    int value=ReadUInt16(record,offset);
                    switch(id)
                    {
                        case 0x0201: vendor=value; break;
                        case 0x0202: product=value; break;
                        case 0x0203: version=value; break;
                        case 0x0205: vendorSource=value; break;
                    }
                }
                offset+=length;
            }
            // Xbox-compatible classic Bluetooth VID/PID and Nova 2 Lite firmware 1.1b.
            // https://github.com/atar-axis/xpadneo/commit/72c62bcd46eccb261ba4bc97f46619d2f87378f2
            return serviceClass==0x1200 && vendor==0x045e && product==0x02e0 && version==0x011b && vendorSource==2;
        }

        private static bool ReadElement(byte[] data,ref int offset,out int length)
        {
            length=0;
            if(offset>=data.Length) return false;
            int header=data[offset++],size=header&7;
            if(header==0) return true;
            if(size<5) length=1<<size;
            else
            {
                int count=1<<(size-5);
                if(data.Length-offset<count) return false;
                uint bytes=0;
                for(int i=0;i<count;i++) bytes=(bytes<<8)|data[offset++];
                if(bytes>int.MaxValue) return false;
                length=(int)bytes;
            }
            return length<=data.Length-offset;
        }

        private static int ReadUInt16(byte[] bytes,int offset) {return bytes[offset]<<8|bytes[offset+1];}
    }
}
