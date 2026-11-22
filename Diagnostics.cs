using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LinkScope
{
    internal static class Diagnostics
    {
        internal static void Run(string path)
        {
            var lines=new List<string>();
            int failures=0;
            Action<string,bool> check=(name,ok)=> { lines.Add((ok?"PASS ":"FAIL ")+name); if(!ok) failures++; };
            string settings=path+".interval";
            try
            {
                check("invalid settings fall back to 30 seconds",RefreshSettings.Load(settings+".absent")==30);
                var ui=new Dashboard(settings);
                foreach(int seconds in Dashboard.Intervals)
                {
                    ui.SelectInterval(seconds,true);
                    check("active timer uses "+seconds+" seconds",ui.IntervalSeconds==seconds && ui.TimerInterval.TotalSeconds==seconds);
                }
                check("selection persists after restart",RefreshSettings.Load(settings)==60);
                ui.Shutdown();

                var mouse=new Node {Id=@"USB\VID_373E&PID_0084\mouse"};
                var hub=new Node {Id=@"USB\VID_1A86&PID_8091\hub"};
                check("mouse behind hub is independent",Hardware.PeripheralUsbNode(new[]{mouse,hub})==mouse);
                check("Bluetooth service UUID suffix is not a device address",
                    Hardware.BluetoothAddress(@"BTHENUM\{0000111e-0000-1000-8000-00805f9b34fb}\8&ABC&0&112233445566_C00000000")=="112233445566");
                var live=Hardware.Scan(Input.Catalog(),true,new ScanLog());
                check("live scan returns a list",live!=null);
                if(live.Any(d=>d.Model=="373E:0084"))
                    check("TYPE33 listed as 2.4G",live.Any(d=>d.Name=="LunaFury TYPE33" && d.Kind=="鼠标" && d.Link=="2.4G"));
                foreach(var d in live) lines.Add((Tracking.Show(d)?"visible ":"hidden ")+d.Name+" / "+d.Link+" / "+d.Power);
                check("unpowered USB devices hidden",!Tracking.Show(new Device{Name="AE64 Pro",Link="USB",Kind="键盘"})
                    && !Tracking.Show(new Device{Name="HyperX Cloud III",Link="USB",Kind="音频"}));
                check("known rechargeable device without a report stays visible",Tracking.Show(new Device{Link="2.4G",Kind="键盘",
                    Model="0B05:1ACE",BatteryCapable=true}));
                check("0% is displayed as a real reading",Tracking.Show(new Device{Link="USB",Battery=0}));
                string updateJson="{\"tag_name\":\"v9.8.7\",\"assets\":[{\"name\":\"PeriLink.exe\",\"state\":\"uploaded\",\"size\":640,"+
                    "\"browser_download_url\":\""+Dashboard.RepositoryUrl+"/releases/download/v9.8.7/PeriLink.exe\",\"digest\":\"sha256:"+new string('a',64)+"\"}]}";
                var update=AppUpdates.ReadRelease(updateJson,new Version(9,8,6,0));
                check("updater accepts the newer official release with its checksum",update!=null &&
                    update.Version==new Version(9,8,7,0) && update.Digest==new string('a',64));
                check("updater ignores the same version and older releases",
                    AppUpdates.ReadRelease(updateJson,new Version(9,8,7,0))==null &&
                    AppUpdates.ReadRelease(updateJson,new Version(9,8,8,0))==null);
                bool rejectedUpdate=false;
                try {AppUpdates.ReadRelease(updateJson.Replace("github.com","example.invalid"),new Version(9,8,6,0));}
                catch(InvalidDataException) {rejectedUpdate=true;}
                check("updater rejects an executable outside the official release",rejectedUpdate);
                var wiredLuna=new Device {Id="wired-luna",Model="373E:0054",Kind="鼠标",Link="USB",Battery=100,Name="LunaFury TYPE33"};
                var radioLuna=new Device {Id="radio-luna",Model="373E:0084",Kind="鼠标",Link="2.4G",Name=wiredLuna.Name};
                var switched=new List<Device>{radioLuna,wiredLuna};
                DeviceConnections.PreferWired(switched);
                check("wired LunaFury replaces an idle receiver with the same card identity",switched.Count==1 &&
                    switched[0]==wiredLuna && wiredLuna.Id=="radio-luna" && wiredLuna.WiredId=="wired-luna" && wiredLuna.Battery==100);
                radioLuna.Battery=65;
                var otherLuna=new Device {Id="another-luna",Model="373E:0054",Kind="鼠标",Link="USB",Battery=72};
                var twoMice=new List<Device>{radioLuna,otherLuna};
                DeviceConnections.PreferWired(twoMice);
                check("separately responding LunaFury devices stay visible",twoMice.Count==2);
                check("DeathAdder and Basilisk cables match their dedicated receivers",
                    RazerBattery.WiredModel("1532:00B7")=="1532:00B6" && RazerBattery.WiredModel("1532:00AB")=="1532:00AA" &&
                    RazerBattery.WiredModel("1532:00B3")==null);
                var razerWired=new Device {Id="rz-cable",Link="USB",Kind="鼠标",Model="1532:00C0",Battery=55};
                var razerRadio=new Device {Id="rz-radio",Link="2.4G",Kind="鼠标",Model="1532:00C1",Battery=54};
                var rzr=new byte[91];
                System.Text.Encoding.ASCII.GetBytes("TEST00004567").CopyTo(rzr,9);
                razerWired.UnitKey=RazerBattery.DecodeUnit(razerWired.Model,rzr);
                System.Text.Encoding.ASCII.GetBytes("TEST00004568").CopyTo(rzr,9);
                razerRadio.UnitKey=RazerBattery.DecodeUnit(razerRadio.Model,rzr);
                var razers=new List<Device>{razerWired,razerRadio};
                razerRadio.Battery=null;
                DeviceConnections.PreferWired(razers);
                check("different Razer serials stay separate even when one is idle",razers.Count==2);
                razerRadio.Battery=54;
                razerRadio.UnitKey=razerWired.UnitKey;
                DeviceConnections.PreferWired(razers);
                check("same Razer serial merges even when both transports report",razerWired.UnitKey!=null && razers.Count==1 &&
                    razerWired.Id=="rz-radio" && razerWired.Battery==55);
                var azoth=new Device {Id="azoth-usb",Link="USB",Kind="键盘",Model="0B05:1A83"};
                var omni=new Device {Id="omni",Link="2.4G",Kind="键盘",Model="0B05:1ACE"};
                var rogLinks=new List<Device>{azoth,omni};
                DeviceConnections.PreferWired(rogLinks);
                check("unidentified shared Omni receiver never merges",rogLinks.Count==2);
                omni.PairedModel="0B05:1A83";
                DeviceConnections.PreferWired(rogLinks);
                check("only confirmed Azoth pairing shares the USB card",rogLinks.Count==1 && rogLinks[0].Id=="omni");
                var logitechReport=new byte[]{0x11,0xff,7,0x0d,1,0x12,0x34,0x56,0x78,0,0x0c,0x40,0x93,0xc0,0x94,0,0,0,0,0};
                var logitechWired=new Device {Id="logi-usb",Link="USB",Kind="鼠标",Model="046D:C094",
                    UnitKey=Receiver.DecodeLogitechUnit(logitechReport,"046D:C094"),Battery=77};
                var logitechRadio=new Device {Id="logi-wireless",Link="2.4G",Kind="鼠标",Model="046D:C547",UnitKey="046D:4093/87654321"};
                var logitechLinks=new List<Device>{logitechWired,logitechRadio};
                DeviceConnections.PreferWired(logitechLinks);
                check("different Logitech unit IDs keep two cards",logitechLinks.Count==2 && logitechWired.UnitKey=="046D:4093/12345678");
                logitechRadio.UnitKey=logitechWired.UnitKey;
                DeviceConnections.PreferWired(logitechLinks);
                check("matching Logitech wireless model and unit ID share one card",logitechLinks.Count==1 && logitechLinks[0].Id=="logi-wireless" &&
                    Receiver.DecodeLogitechUnit(logitechReport,"046D:C09B")==null);
                var connections=new ConnectionNotices();
                check("first scan is quiet",connections.Update(new[]{radioLuna}).Count==0);
                check("cable changes the same mouse to USB",connections.Update(switched).SequenceEqual(new[]{"LunaFury TYPE33 已切换为 USB 有线"}));
                check("repeated refresh stays quiet",connections.Update(switched).Count==0);
                check("removing the cable changes the same mouse back to 2.4G",
                    connections.Update(new[]{radioLuna}).SequenceEqual(new[]{"LunaFury TYPE33 已切换为 2.4G"}));
                var sleepingLuna=new Device {Id=radioLuna.Id,Model=radioLuna.Model,Link="2.4G",Battery=65,RetainedBattery=true};
                var oneMiss=connections.Update(new[]{sleepingLuna});
                check("one missed wireless reading does not repeat a notification",oneMiss.Count==0 && connections.Update(new[]{radioLuna}).Count==0);
                connections.Update(new[]{sleepingLuna});
                connections.Update(new[]{sleepingLuna});
                check("a mouse that comes back online notifies once",
                    connections.Update(new[]{radioLuna}).SequenceEqual(new[]{"已连接 LunaFury TYPE33 · 2.4G"}));
                var plugged=new Device {Id="test-usb",Link="USB",Name="测试手柄",Battery=0};
                check("a new USB device is announced by name",connections.Update(new[]{radioLuna,plugged}).SequenceEqual(new[]{"已插入 测试手柄 · USB 有线"}));
                var at=new DateTime(2026,11,18,12,0,0,DateTimeKind.Local);
                string readings=Path.ChangeExtension(settings,"readings");
                var newEntry=new Device {Id="tester-mouse",Link="2.4G"};
                var history=new ReadingHistory(readings);
                history.Attach(new List<Device>{new Device {Id="tester-mouse",Link="2.4G",Battery=92}},at);
                new ReadingHistory(readings).Attach(new List<Device>{newEntry},at.AddMinutes(1));
                check("retained reading keeps original time after restart",newEntry.RetainedBattery && newEntry.Battery==92
                    && newEntry.BatteryReadAt==at && Tracking.ReadTime(newEntry,at)=="上次读取 12:00:00");
                var many=Enumerable.Range(0,24).Select(i=>new Device {Id="demo-"+i,Name="Device "+i,Kind="鼠标",Link="蓝牙",Battery=i}).ToList();
                var tray=new BatteryList(true);
                tray.Update(many,false);
                check("tray list includes all 24 devices",tray.Count==24);
                tray.Update(many.Take(3).Reverse().ToList(),false);
                check("tray list removes unplugged items",tray.Count==3);
                string layout=path+".ui";
                var prefs=new Preferences(layout) {CustomColor="#EBE6F3",Eco=true,Left=712,Top=285,
                    Pins=many.Skip(7).Take(4).Select(d=>Preferences.Key(d.Id)).ToList()};
                prefs.Save();
                var next=new Preferences(layout);
                check("four chosen cards, theme, and coordinates survive restart",next.Peek(many).Select(d=>d.Id).SequenceEqual(many.Skip(7).Take(4).Select(d=>d.Id))
                    && next.Left==712&&next.Top==285&&next.CustomColor=="#EBE6F3"&&next.Eco);
                next.Pins.Add(Preferences.Key(wiredLuna.WiredId));
                next.Pins.Add(Preferences.Key(wiredLuna.Id));
                next.MergeWiredCard(wiredLuna);
                check("an old wired tray pin follows the unified mouse after restart",
                    new Preferences(layout).Peek(switched).SequenceEqual(switched));
                check("unconfigured tray defaults to at most four cards",new Preferences(layout+".absent").Peek(many).Count==4);
                next.CustomColor="#91A8C8";next.Material=1;next.Save();
                var custom=new Preferences(layout);
                custom.Use();
                check("custom background and individual material persist",custom.CustomColor=="#91A8C8"&&custom.Material==1
                    &&Preferences.Current.Name=="自定义"&&!Preferences.GlassOn);
                custom.ThemeMode=2;custom.CardStyle=2;custom.BackdropStyle=3;custom.Save();
                var display=new Preferences(layout);
                display.Use();
                check("dark theme, card and background styles survive a restart",
                    Preferences.Current.IsDark&&Preferences.CurrentCardStyle==2&&Preferences.CurrentBackdrop==3);
                File.WriteAllText(layout,"material=4\ntheme-mode=0\n");
                var migrated=new Preferences(layout);
                migrated.Use();
                check("removed materials fall back to glass and the default theme follows Windows",
                    migrated.Material==Look.GlassMaterial&&Preferences.Current.IsDark==Preferences.SystemDark());
                check("level water colors change at twenty and fifty percent",
                    Preferences.Current.Power(20,true)!=Preferences.Current.Power(21,true)&&
                    Preferences.Current.Power(21,true)==Preferences.Current.Power(49,true)&&
                    Preferences.Current.Power(49,true)!=Preferences.Current.Power(50,true));
                var luna=new Device {Name="LunaFury TYPE33",Kind="鼠标",Link="2.4G",Model="373E:0084",Battery=89};
                var sony=new Device {Name="XB13",Kind="音频",Link="蓝牙",Battery=100};
                check("known LunaFury receiver and Sony speaker get distinct logos",BrandBadge.Identify(luna)=="LUNAFURY"
                    &&BrandBadge.Identify(sony)=="SONY"&&BrandBadge.ModelName(sony,"SONY")=="SRS-XB13");
                var lunaMark=BrandBadge.Create("LUNAFURY",luna.Kind,Preferences.Current.Ink);
                check("LunaFury manufacturer wordmark is embedded",lunaMark.OpacityMask is System.Windows.Media.ImageBrush);
                check("unknown product uses the category icon",BrandBadge.Identify(new Device{Name="Some speaker",Kind="音频",Link="蓝牙"})==null);
                check("expanded brand catalog uses device names, manufacturers and USB IDs",
                    Brands.CatalogSize>=60 &&
                    BrandBadge.Identify(new Device{Name="HID Keyboard Device",Model="3434:0161"})=="KEYCHRON" &&
                    BrandBadge.Identify(new Device{Name="LE-JBL Flip 6",Kind="音频"})=="JBL" &&
                    BrandBadge.Identify(new Device{Name="BT Headset",Manufacturer="Bose Corporation",Kind="音频"})=="BOSE" &&
                    BrandBadge.Identify(new Device{Name="Wireless Gamepad",Model="2DC8:6001",Kind="手柄"})=="8BITDO");
                check("device art distinguishes speakers, headphones and earbuds",
                    DeviceArt.Category(sony)=="音箱" && DeviceArt.Category(new Device{Name="WH-1000XM5",Kind="音频"})=="耳机" &&
                    DeviceArt.Category(new Device{Name="AirPods Pro",Kind="音频"})=="耳塞");
                var wifi=DeviceArt.Signal("2.4G",Preferences.Current.IconInk,23);
                var bluetooth=DeviceArt.Signal("蓝牙",Preferences.Current.IconInk,23);
                check("clean connection badges use distinct glyphs with theme ink",
                    wifi.Data.Bounds.Width>bluetooth.Data.Bounds.Width&&wifi.Fill==Preferences.Current.IconInk&&wifi.Width==23);
                check("startup command preserves a path containing spaces",
                    StartupRegistration.CommandFor(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Test Folder","连测.exe"))
                    .EndsWith("连测.exe\" --startup",StringComparison.Ordinal));
                var deviceRows=new List<Device>{luna,sony,new Device{Id="off",Name="Xbox Wireless Controller",Battery=47,Connected=false},
                    new Device{Id="sleeping",Kind="键盘",Link="2.4G",BatteryCapable=true}};
                var inUse=deviceRows.Where(d=>d.Connected).ToList();
                check("tray input includes connected devices and pending readings",inUse.Count==3&&inUse.Contains(sony)
                    &&inUse.Any(x=>x.Id=="sleeping"));
                using(var trayIcon=AppArt.TrayIcon(inUse))
                    check("filled battery tray icon is readable by Windows",trayIcon.Width>=16&&trayIcon.Height>=16);
                var artIcon=AppArt.IconBytes(null);
                int iconCount=BitConverter.ToUInt16(artIcon,4);
                check("desktop icon package includes 16, 48 and 256 pixel drawings",
                    new[]{16,48,0}.All(d=>Enumerable.Range(0,iconCount).Any(i=>artIcon[6+16*i]==d)));
                check("Bluetooth smartwatch selects its own device category",
                    Hardware.Guess("Galaxy Watch 6")=="手表"&&Hardware.Guess("Bose Headphones")=="音频"&&
                    DeviceArt.Category(new Device{Name="华为手表",Kind="其他"})=="手表");
                var deviceOutlines=new[]{"鼠标","键盘","音箱","耳机","耳塞","手柄","手表"}.Select(DeviceArt.Outline).ToList();
                var lineIcon=(System.Windows.Shapes.Path)((System.Windows.Controls.Canvas)DeviceArt.Illustration("鼠标").Child).Children[0];
                check("device outlines are distinct and fit the same scalable artboard",
                    deviceOutlines.Distinct().Count()==7&&deviceOutlines.All(x=>x.IsFrozen&&x.Bounds.Width>15&&
                    new System.Windows.Rect(3,3,50,50).Contains(x.Bounds)));
                check("device outlines use rounded strokes in the current theme",
                    lineIcon.Fill==null&&lineIcon.Stroke==Preferences.Current.IconInk&&lineIcon.StrokeEndLineCap==System.Windows.Media.PenLineCap.Round);
                var small=AppArt.DrawIcon(16,inUse);
                var coverage=Pixels(small);
                check("16 pixel glyph reaches both edges of its usable canvas",
                    Enumerable.Range(0,16).Any(y=>coverage[(y*16+1)*4+3]>200)&&
                    Enumerable.Range(0,16).Any(y=>coverage[(y*16+14)*4+3]>200));
                check("brand marks include ROG, Xbox, Razer, Logitech, HyperX and ATK",
                    new[]{"ROG","XBOX","RAZER","LOGITECH","HYPERX","ATK"}.All(x=>BrandMarks.Mark(x,Preferences.Current.Ink)!=null));
                check("offline brand collection loads",BrandMarks.VectorCount>=16&&new[]{"VGN","MCHOSE","RAPOO","GAMESIR"}
                    .All(x=>BrandMarks.Mark(x,Preferences.Current.Ink)!=null));
                check("HSV handles primary and neutral colors",ThemeColorPicker.Hsv(0,1,1)==System.Windows.Media.Colors.Red
                    &&ThemeColorPicker.Hsv(1.0/3,1,1)==System.Windows.Media.Colors.Lime
                    &&ThemeColorPicker.Hsv(.65,0,1)==System.Windows.Media.Colors.White);
                next.Use();
                check("power saver disables motion",!Motion.Enabled);
                new Preferences(layout+".absent").Use();
                if(File.Exists(layout)) File.Delete(layout);
                string ruleKey=Preferences.Key("example-mouse");
                string config=path+".profiles";
                File.WriteAllText(config,string.Join("\t",new[]{ruleKey,"HID_DEMO","1","7","4","2","0","1:A1"}));
                var store=new SavedProfiles(config);
                var rule=store.Find("example-mouse");
                check("previously saved profiles are read only for their matching device",
                    rule!=null&&store.Find("different")==null&&rule.Read(new byte[]{7,0xa1,43,28})==43
                    &&rule.Read(new byte[]{7,0xb1,43,28})==null);
                File.Delete(config);
                var maximum=new Preferences(layout) {TrayLimit=8};
                maximum.Save();
                check("tray count can be saved and increased to eight",new Preferences(layout).Peek(many).Count==8);
                maximum.TrayLimit=1;maximum.Save();
                check("tray count can be reduced to a single card",new Preferences(layout).Peek(many).Count==1);
                maximum.TrayMonitor=@"\.DISPLAY1";maximum.TrayX=.72;maximum.TrayY=.61;
                maximum.Save();
                var restored=new Preferences(layout);
                check("tray keeps remembered monitor and relative position across restart",
                    restored.TrayMonitor==maximum.TrayMonitor&&restored.TrayX==.72&&restored.TrayY==.61);
                bool fits=true;
                foreach(double scale in new[]{1.25,1.5,2d})
                {
                    int margin=(int)Math.Round(8*scale);
                    int w=(int)Math.Ceiling(432*scale),h=(int)Math.Ceiling(255*scale);
                    int x=(int)Math.Round(.72*DisplayPlacement.Travel(1280,w,margin));
                    int y=(int)Math.Round(.61*DisplayPlacement.Travel(720,h,margin));
                    fits&=x+margin+w<=1280&&y+margin+h<=720;
                }
                check("remembered tray position fits work area at 125, 150 and 200 percent",fits);
                restored.ClearTrayPosition();restored.Save();
                check("reset removes both current and legacy tray coordinates",
                    new Preferences(layout).TrayMonitor==null&&!new Preferences(layout).Left.HasValue);
                File.Delete(layout);
                check("WPF animations request sixty frames per second",
                    System.Windows.Media.Animation.Timeline.GetDesiredFrameRate(Motion.Tween(0,1,160))==60);
                var number=new PowerText();
                number.Show(92,"",false);
                number.Show(89,"",false);
                check("animated display settles at the exact reading",number.Text=="89%");
                number.Show(100,"",false);
                number.Measure(new System.Windows.Size(double.PositiveInfinity,50));
                check("pixel counter measures all four characters",number.Text=="100%"&&number.DesiredSize.Width==92);
            }
            catch(Exception e) { failures++;lines.Add(e.ToString()); }
            finally
            {
                if(File.Exists(settings)) File.Delete(settings);
                string readings=Path.ChangeExtension(settings,"readings");
                if(File.Exists(readings)) File.Delete(readings);
            }
            lines.Add("Failures "+failures);
            File.WriteAllLines(path,lines);
            Environment.ExitCode=failures;
        }

        private static byte[] Pixels(System.Windows.Media.Imaging.BitmapSource bitmap)
        {
            int stride=bitmap.PixelWidth*4;
            var bytes=new byte[stride*bitmap.PixelHeight];
            bitmap.CopyPixels(bytes,stride,0);
            return bytes;
        }
    }
}
