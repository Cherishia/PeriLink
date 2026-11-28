using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Markup;
using Microsoft.Win32;

namespace LinkScope
{
    internal sealed class Look
    {
        internal const int GlassMaterial=2;
        internal static readonly string[] MaterialNames={"纯色","柔雾","液态玻璃"};
        internal static readonly string[] BackdropNames={"微光","柔光","极光","月夜","深海","星空","烟雾"};
        internal static readonly Look Neutral=new Look("银灰","#F1F2F5","#DCE1EA","#202833","#E5E9F1",Color.FromRgb(82,111,169),false);
        internal static readonly Look DarkNeutral=new Look("银灰","#20242C","#303746","#EFF3FC","#353E4F",Color.FromRgb(126,154,226),true);
        internal readonly string Name;
        internal readonly SolidColorBrush Shell,Surface,Ink,Tonal,Muted,Card,Line;
        internal readonly Color Accent;
        internal readonly SolidColorBrush IconInk,Stage,Good,Warning,Low;
        internal readonly bool IsDark;
        private Look(string name,string shell,string panel,string ink,string tonal,Color accent,bool dark)
        {
            Name=name;
            Shell=B(shell);Surface=B(panel);Ink=B(ink);Tonal=B(tonal);
            Accent=accent;IsDark=dark;
            IconInk=Paint(Blend(accent,dark?Colors.White:Color.FromRgb(18,31,36),dark?.52:.60));
            Stage=Paint(Blend(Shell.Color,accent,dark?.16:.13));
            Muted=Paint(Blend(Ink.Color,Shell.Color,dark?.26:.32));
            Card=Paint(Blend(Shell.Color,dark?Surface.Color:Colors.White,.72));
            Line=Paint(Blend(Surface.Color,Ink.Color,dark?.20:.14));
            Good=B(dark?"#83E2AB":"#166B42");Warning=B(dark?"#F5CF75":"#87600E");Low=B(dark?"#FFA6AC":"#A83E47");
        }
        private static SolidColorBrush B(string color)
        {return Paint((Color)ColorConverter.ConvertFromString(color));}
        internal static SolidColorBrush Paint(Color color)
        {var brush=new SolidColorBrush(color);brush.Freeze();return brush;}
        internal static Color Alpha(Color color,byte value)
        {return Color.FromArgb(value,color.R,color.G,color.B);}
        internal static bool TryColor(string html,out Color color)
        {
            color=Colors.Transparent;
            html=(html??"").Trim().TrimStart('#');
            int value;
            if(html.Length!=6||!int.TryParse(html,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out value)) return false;
            color=Color.FromRgb((byte)(value>>16),(byte)(value>>8),(byte)value);
            return true;
        }
        internal static string Hex(Color color) { return string.Format("#{0:X2}{1:X2}{2:X2}",color.R,color.G,color.B); }
        internal static Look Custom(Color hue,bool dark=false)
        {
            var basis=dark?DarkNeutral:Neutral;
            return new Look("自定义",Blend(hue,basis.Shell.Color,.79).ToString(),Blend(hue,basis.Surface.Color,.72).ToString(),
                Blend(hue,basis.Ink.Color,.88).ToString(),Blend(hue,basis.Tonal.Color,.82).ToString(),hue,dark);
        }
        internal static Color Blend(Color a,Color b,double amount)
        {
            return Color.FromRgb((byte)Math.Round(a.R+(b.R-a.R)*amount),(byte)Math.Round(a.G+(b.G-a.G)*amount),
                (byte)Math.Round(a.B+(b.B-a.B)*amount));
        }
        internal Brush Backdrop(int material,bool overlay=false)
        {
            if(material==0||SystemParameters.HighContrast) return Shell;
            if(material==1) return new LinearGradientBrush(Shell.Color,Surface.Color,137);
            var upper=Blend(Shell.Color,IsDark?Surface.Color:Colors.White,.38);
            return new LinearGradientBrush(Alpha(upper,overlay?(byte)226:(byte)190),
                Alpha(Surface.Color,overlay?(byte)216:(byte)173),125);
        }

        internal Brush Power(int value,bool water=false)
        {
            if(!water) return value<=20?Low:value<50?Warning:Good;
            return value<=20?WaterRed:value<50?WaterAmber:WaterGreen;
        }
        private static readonly Brush WaterGreen=B("#58C78A"),WaterAmber=B("#EAB84B"),WaterRed=B("#E77C81");
    }

    internal sealed class Preferences
    {
        private readonly string path;
        private static readonly string[] OldColors={"#ECE9E4","#EBE6F3","#DDEDE7"};
        internal const int MaxTrayCards=8;
        internal int TrayLimit=4;
        internal int Material=2;
        internal int ThemeMode,BackdropStyle,CardStyle;
        internal static readonly string[] ThemeNames={"跟随系统","浅色","深色"};
        internal static readonly string[] CardNames={"水波","电量条","圆环","简约"};
        internal bool Glass=true, Eco;
        internal string CustomColor;
        internal double? Left,Top;
        internal string TrayMonitor;
        internal double? TrayX,TrayY;
        internal List<string> Pins;
        private List<string> mainOrder=new List<string>();
        private HashSet<string> mainHidden=new HashSet<string>(StringComparer.Ordinal);
        internal static Look Current=Look.Neutral;
        internal static bool GlassOn=true, PowerSaver;
        internal static int CurrentMaterial=2;
        internal static int CurrentBackdrop,CurrentCardStyle;
        internal Preferences(string file)
        {
            path=file;
            try
            {
                string oldColor=null;
                if(!File.Exists(path)) return;
                foreach(string line in File.ReadLines(path).Take(20))
                {
                    int i=line.IndexOf('=');
                    if(i<1) continue;
                    string value=line.Substring(i+1);
                    switch(line.Substring(0,i))
                    {
                        case "theme":
                            int number;
                            if(int.TryParse(value,out number)&&number>=0&&number<OldColors.Length) oldColor=OldColors[number];
                            break;
                        case "glass": Glass=value!="0";break;
                        case "material":
                            int material;
                            if(int.TryParse(value,out material)&&material>=0&&material<Look.MaterialNames.Length) Material=material;
                            break;
                        case "theme-mode":
                            ThemeMode=Option(value,ThemeNames.Length);
                            break;
                        case "backdrop":
                            BackdropStyle=Option(value,Look.BackdropNames.Length);
                            break;
                        case "card-style":
                            CardStyle=Option(value,CardNames.Length);
                            break;
                        case "color":
                            Color color;
                            if(Look.TryColor(value,out color)) CustomColor=Look.Hex(color);
                            break;
                        case "eco": Eco=value=="1";break;
                        case "left": Left=Coordinate(value);break;
                        case "top": Top=Coordinate(value);break;
                        case "tray-monitor": TrayMonitor=value;break;
                        case "tray-x": TrayX=Fraction(value);break;
                        case "tray-y": TrayY=Fraction(value);break;
                        case "tray-limit":
                            int limit;
                            if(int.TryParse(value,out limit)&&limit>=1&&limit<=MaxTrayCards) TrayLimit=limit;
                            break;
                        case "pins": Pins=value.Split(',').Where(x=>x.Length==32).Distinct().Take(MaxTrayCards).ToList();break;
                        case "main-order": mainOrder=DeviceKeys(value).ToList();break;
                        case "main-hidden": mainHidden=new HashSet<string>(DeviceKeys(value),StringComparer.Ordinal);break;
                    }
                }
                if(CustomColor==null) CustomColor=oldColor;
            }
            catch(IOException) { }
            catch(UnauthorizedAccessException) { }
        }
        internal void Use()
        {
            Color user;
            bool dark=ThemeMode==0?SystemDark():ThemeMode==2;
            Current=Look.TryColor(CustomColor,out user)?Look.Custom(user,dark):dark?Look.DarkNeutral:Look.Neutral;
            GlassOn=Glass&&Material==Look.GlassMaterial;
            CurrentMaterial=Material;CurrentBackdrop=BackdropStyle;CurrentCardStyle=CardStyle;PowerSaver=Eco;
        }
        internal static bool SystemDark()
        {
            try
            {
                return object.Equals(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme",1),0);
            }
            catch(System.Security.SecurityException) {return false;}
            catch(UnauthorizedAccessException) {return false;}
            catch(IOException) {return false;}
        }
        private static int Option(string value,int count)
        {int id;return int.TryParse(value,out id)&&id>=0&&id<count?id:0;}
        internal void Save()
        {
            var lines=new List<string>{"theme=3","material="+Material,"theme-mode="+ThemeMode,"backdrop="+BackdropStyle,
                "card-style="+CardStyle,"glass="+(Glass?"1":"0"),"eco="+(Eco?"1":"0"),"tray-limit="+TrayLimit};
            if(!string.IsNullOrEmpty(CustomColor)) lines.Add("color="+CustomColor);
            if(Left.HasValue&&Top.HasValue)
            {
                lines.Add("left="+Left.Value.ToString(CultureInfo.InvariantCulture));
                lines.Add("top="+Top.Value.ToString(CultureInfo.InvariantCulture));
            }
            if(TrayMonitor!=null&&TrayX.HasValue&&TrayY.HasValue)
            {
                lines.Add("tray-monitor="+TrayMonitor);
                lines.Add("tray-x="+TrayX.Value.ToString("R",CultureInfo.InvariantCulture));
                lines.Add("tray-y="+TrayY.Value.ToString("R",CultureInfo.InvariantCulture));
            }
            if(Pins!=null) lines.Add("pins="+string.Join(",",Pins));
            if(mainOrder.Count>0) lines.Add("main-order="+string.Join(",",mainOrder));
            if(mainHidden.Count>0) lines.Add("main-hidden="+string.Join(",",mainHidden.OrderBy(x=>x,StringComparer.Ordinal)));
            try
            {
                string tmp=path+".tmp";
                File.WriteAllLines(tmp,lines);
                if(File.Exists(path)) File.Replace(tmp,path,null);
                else File.Move(tmp,path);
            }
            catch(IOException) { }
            catch(UnauthorizedAccessException) { }
        }
        private static double? Coordinate(string value)
        {
            double n;
            return double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out n)&&Math.Abs(n)<100000 ? (double?)n : null;
        }
        private static double? Fraction(string value)
        {
            double result;
            return double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out result)&&result>=0&&result<=1?(double?)result:null;
        }
        internal void ClearTrayPosition()
        {
            TrayMonitor=null;TrayX=TrayY=Left=Top=null;
        }
        internal static string Key(string ident)
        {
            using(var hash=SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes((ident??"").ToUpperInvariant())),0,16).Replace("-","");
        }
        private static IEnumerable<string> DeviceKeys(string value)
        {return value.Split(',').Where(x=>x.Length==32).Distinct(StringComparer.Ordinal);}

        internal bool ShowsMain(string id) {return !mainHidden.Contains(Key(id));}

        internal bool SetMainVisible(string id,bool visible)
        {return visible?mainHidden.Remove(Key(id)):mainHidden.Add(Key(id));}

        internal void MergeCardPreferences(Device device)
        {
            string alternate=Key(device.AlternateId), canonical=Key(device.Id);
            bool changed=false;
            int index=mainOrder.IndexOf(alternate);
            if(index>=0)
            {
                if(mainOrder.Contains(canonical)) mainOrder.RemoveAt(index);
                else mainOrder[index]=canonical;
                changed=true;
            }
            if(Pins!=null && Pins.Contains(alternate))
            {
                Pins=Pins.Select(key=>key==alternate?canonical:key).Distinct(StringComparer.Ordinal).ToList();
                changed=true;
            }
            if(changed) Save();
        }

        internal IList<Device> MainCards(IList<Device> devices,bool visible=true)
        {
            if(visible&&mainOrder.Count==0&&mainHidden.Count==0) return devices;
            var ranks=mainOrder.Select((key,index)=>new {key,index})
                .ToDictionary(x=>x.key,x=>x.index,StringComparer.Ordinal);
            return devices.Select(d=>new {Device=d,Key=Key(d.Id)}).Where(x=>mainHidden.Contains(x.Key)!=visible)
                .OrderBy(x=>ranks.ContainsKey(x.Key)?ranks[x.Key]:int.MaxValue).Select(x=>x.Device).ToList();
        }

        internal bool MoveMain(IList<Device> devices,string sourceId,string targetId,bool after)
        {
            if(sourceId==targetId) return false;
            var group=MainCards(devices,ShowsMain(sourceId)).Select(d=>d.Id).ToList();
            int sourceIndex=group.IndexOf(sourceId),targetIndex=group.IndexOf(targetId);
            if(sourceIndex<0||targetIndex<0) return false;
            int insert=targetIndex+(after?1:0)-(sourceIndex<targetIndex?1:0);
            if(insert==sourceIndex) return false;
            var order=mainOrder.Concat(devices.Select(d=>Key(d.Id))).Distinct(StringComparer.Ordinal).ToList();
            order.Remove(Key(sourceId));
            order.Insert(order.IndexOf(Key(targetId))+(after?1:0),Key(sourceId));
            mainOrder=order;
            return true;
        }

        internal IList<Device> Peek(IList<Device> all)
        {
            if(Pins==null) return all.Take(TrayLimit).ToList();
            var byKey=all.GroupBy(d=>Key(d.Id),StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.Ordinal);
            return Pins.Where(byKey.ContainsKey).Select(id=>byKey[id]).Take(TrayLimit).ToList();
        }
    }

    internal sealed class GlassLayer : Grid
    {
        private readonly FrameworkElement target;
        private readonly bool foreground;
        private readonly AmbientBackdrop backdrop;
        private readonly RenderLoop clock;
        private readonly System.Windows.Shapes.Ellipse highlight=new System.Windows.Shapes.Ellipse(),
            reflection=new System.Windows.Shapes.Ellipse(), sweep=new System.Windows.Shapes.Ellipse();
        private readonly TranslateTransform move=new TranslateTransform(), mirror=new TranslateTransform(), drift=new TranslateTransform();
        private Point aim=new Point(.5,.4), center=new Point(.5,.4);
        private double strength,phase;
        private bool hovering;

        internal GlassLayer(FrameworkElement input,bool overCards=false)
        {
            target=input;foreground=overCards;IsHitTestVisible=false;ClipToBounds=!overCards;
            if(!foreground)
            {
                backdrop=new AmbientBackdrop();
                Children.Add(backdrop);
            }
            Add(highlight,move);
            if(!foreground) {Add(reflection,mirror);Add(sweep,drift);}
            clock=new RenderLoop(Tick);
            SizeChanged+=delegate {Sizes();};
            var track=new MouseEventHandler(delegate(object s,MouseEventArgs e)
            {
                var p=e.GetPosition(target);
                Follow(new Point(Math.Max(0,Math.Min(1,p.X/Math.Max(1,target.ActualWidth))),
                                 Math.Max(0,Math.Min(1,p.Y/Math.Max(1,target.ActualHeight)))));
            });
            input.MouseEnter+=track;
            input.AddHandler(UIElement.MouseMoveEvent,track,true);
            input.MouseLeave+=delegate {hovering=false;if(Motion.Enabled&&Preferences.GlassOn) clock.Start();};
            input.IsVisibleChanged+=delegate {if(!input.IsVisible) Reset();};
            Unloaded+=delegate {Reset();};
            Apply();
        }

        internal void Follow(Point position)
        {
            aim=position;
            if(strength<.025) center=position;
            hovering=true;
            if(Motion.Enabled&&Preferences.GlassOn) clock.Start();
        }

        private void Add(System.Windows.Shapes.Ellipse light,TranslateTransform transform)
        {
            light.HorizontalAlignment=HorizontalAlignment.Left;
            light.VerticalAlignment=VerticalAlignment.Top;
            light.RenderTransform=transform;
            light.Opacity=0;
            Children.Add(light);
        }

        private void Sizes()
        {
            highlight.Width=Math.Max(140,ActualWidth*(foreground?.82:.85));
            highlight.Height=Math.Max(96,ActualHeight*(foreground?.62:.83));
            reflection.Width=Math.Max(115,ActualWidth*.68);
            reflection.Height=Math.Max(85,ActualHeight*.68);
            sweep.Width=Math.Max(170,ActualWidth*.92);
            sweep.Height=58;
        }

        internal void Apply()
        {
            if(backdrop!=null) backdrop.Apply(Preferences.Current,Preferences.CurrentBackdrop);
            Color tint=Look.Blend(Preferences.Current.Ink.Color,Preferences.Current.Accent,.62);
            highlight.Fill=Light(Color.FromArgb((byte)(foreground?108:172),255,255,255),
                Color.FromArgb((byte)(foreground?24:54),tint.R,tint.G,tint.B));
            reflection.Fill=Light(Color.FromArgb(95,tint.R,tint.G,tint.B),Color.FromArgb(22,tint.R,tint.G,tint.B));
            sweep.Fill=Light(Color.FromArgb(157,253,255,247),Color.FromArgb(34,tint.R,tint.G,tint.B));
            if(!Motion.Enabled||!Preferences.GlassOn) Reset();
        }

        private static Brush Light(Color bright,Color edge)
        {
            var brush=new RadialGradientBrush {Center=new Point(.5,.5),GradientOrigin=new Point(.5,.5),RadiusX=.5,RadiusY=.5};
            brush.GradientStops.Add(new GradientStop(bright,0));
            brush.GradientStops.Add(new GradientStop(edge,.51));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0,edge.R,edge.G,edge.B),.88));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0,edge.R,edge.G,edge.B),1));
            brush.Freeze();
            return brush;
        }

        private void Reset()
        {
            clock.Stop();strength=0;hovering=false;
            highlight.Opacity=reflection.Opacity=sweep.Opacity=0;
        }

        private void Tick(double seconds)
        {
            if(!Motion.Enabled||!Preferences.GlassOn||!IsVisible) {Reset();return;}
            double follow=1-Math.Exp(-seconds*(foreground?20:13));
            center=new Point(center.X+(aim.X-center.X)*follow,center.Y+(aim.Y-center.Y)*follow);
            strength+=((hovering?1:0)-strength)*(1-Math.Exp(-seconds*9));
            if(!hovering&&strength<.015) {Reset();return;}
            phase=(phase+seconds*2.4)%(Math.PI*2);
            double ripple=Math.Sin(phase);
            move.X=center.X*ActualWidth-highlight.Width*.5+(foreground?0:ripple*10);
            move.Y=center.Y*ActualHeight-highlight.Height*.5;
            highlight.Opacity=strength*(foreground?.60:(.85+ripple*.15));
            if(foreground) return;
            mirror.X=(1-center.X*.72)*ActualWidth-reflection.Width*.55;
            mirror.Y=(.88-center.Y*.68)*ActualHeight-reflection.Height*.48+ripple*13;
            drift.X=center.X*ActualWidth-sweep.Width*.5+ripple*19;
            drift.Y=ActualHeight-29+ripple*7;
            reflection.Opacity=strength*.82;
            sweep.Opacity=strength*(.65+ripple*.17);
        }
    }

    internal sealed class SmoothScroll : ScrollViewer
    {
        private static readonly Style ScrollStyle=(Style)XamlReader.Parse(@"
          <Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ScrollBar}'>
            <Setter Property='Width' Value='9'/>
            <Setter Property='Template'>
              <Setter.Value>
                <ControlTemplate TargetType='{x:Type ScrollBar}'>
                  <Border Background='Transparent' Width='7' HorizontalAlignment='Right'>
                    <Track x:Name='PART_Track' Orientation='Vertical' IsDirectionReversed='True'>
                      <Track.DecreaseRepeatButton>
                        <RepeatButton Command='ScrollBar.PageUpCommand' Opacity='0' Focusable='False'/>
                      </Track.DecreaseRepeatButton>
                      <Track.Thumb>
                        <Thumb Focusable='False'>
                          <Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'>
                            <Border Background='{DynamicResource PeriLinkScrollThumb}' Margin='1,0' CornerRadius='3' SnapsToDevicePixels='True'/>
                          </ControlTemplate></Thumb.Template>
                        </Thumb>
                      </Track.Thumb>
                      <Track.IncreaseRepeatButton>
                        <RepeatButton Command='ScrollBar.PageDownCommand' Opacity='0' Focusable='False'/>
                      </Track.IncreaseRepeatButton>
                    </Track>
                  </Border>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>");
        private readonly RenderLoop animation;
        private bool horizontal;
        private double goal;
        private double Offset {get {return horizontal?HorizontalOffset:VerticalOffset;}}
        private double Extent {get {return horizontal?ScrollableWidth:ScrollableHeight;}}
        internal SmoothScroll(bool horizontal=false)
        {
            Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)]=ScrollStyle;
            ApplyLook();
            this.horizontal=horizontal;
            animation=new RenderLoop(seconds=>
            {
                if(!Motion.Enabled||!IsVisible) {animation.Stop();return;}
                goal=Math.Min(Extent,goal);
                double delta=goal-Offset;
                if(Math.Abs(delta)<.85) { Go(goal);animation.Stop(); }
                else Go(Offset+delta*(1-Math.Exp(-seconds*17)));
            });
            IsVisibleChanged+=delegate {if(!IsVisible) animation.Stop();};
            PreviewMouseDown+=delegate {animation.Stop();};
            Unloaded+=delegate {animation.Stop();};
        }
        internal void ApplyLook()
        {Resources["PeriLinkScrollThumb"]=Look.Paint(Look.Alpha(Preferences.Current.Ink.Color,103));}
        internal void SetHorizontal(bool value)
        {
            if(horizontal==value) return;
            animation.Stop();
            horizontal=value;
            HorizontalScrollBarVisibility=value?ScrollBarVisibility.Hidden:ScrollBarVisibility.Disabled;
            VerticalScrollBarVisibility=value?ScrollBarVisibility.Disabled:ScrollBarVisibility.Auto;
            PanningMode=value?PanningMode.HorizontalOnly:PanningMode.VerticalOnly;
            ScrollToHorizontalOffset(0);
            ScrollToVerticalOffset(0);
        }
        private void Go(double offset)
        {if(horizontal) ScrollToHorizontalOffset(offset);else ScrollToVerticalOffset(offset);}

        internal void JumpTo(double offset)
        {
            animation.Stop();
            goal=Math.Max(0,Math.Min(Extent,offset));
            Go(goal);
        }

        internal void ShiftBy(double delta)
        {
            if(!animation.Active) goal=Offset;
            goal=Math.Max(0,Math.Min(Extent,goal+delta));
            if(Motion.Enabled&&(horizontal||Preferences.GlassOn)) animation.Start();
            else Go(goal);
        }
        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
            if(Extent<=0||!horizontal&&(!Motion.Enabled||!Preferences.GlassOn)) return;
            ShiftBy(-e.Delta/120.0*(horizontal?118:78));
            e.Handled=true;
        }
    }
}
