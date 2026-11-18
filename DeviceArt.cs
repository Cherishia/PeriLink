using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace LinkScope
{
    internal static class DeviceArt
    {
        private static readonly string[] Speakers={"speaker","音箱","音响","扬声器","SRS-","XB13","ULT FIELD","SoundLink",
            "JBL Flip","JBL Charge","JBL Clip","JBL Go","Soundcore Motion","Soundcore Boom","Soundcore Flare",
            "HomePod","Beosound","Emberton","Willen","Middleton","Stockwell","WONDERBOOM","MEGABOOM","Onyx Studio"};
        private static readonly string[] Earbuds={"AirPods","Galaxy Buds","FreeBuds","Redmi Buds","earbud","耳塞","WF-","Buds","LinkBuds","FreeClip"};
        private static readonly string[] Headphones={"headset","headphone","耳机","WH-","ULT WEAR","Arctis","Cloud III","BlackShark",
            "Barracuda","QuietComfort","Space One","Life Q","Elite ","Studio Pro","OpenRun","OpenFit","G PRO X","HS80"};
        private static readonly Dictionary<string,Geometry> Art=new Dictionary<string,Geometry>
        {
            {"鼠标",Icon("M28,7 C36.3,7 43,13.7 43,22 V34 C43,42.3 36.3,49 28,49 C19.7,49 13,42.3 13,34 V22 C13,13.7 19.7,7 28,7 Z M28,16 V23")},
            {"键盘",Icon("M11,14 H45 Q49,14 49,18 V38 Q49,42 45,42 H11 Q7,42 7,38 V18 Q7,14 11,14 Z M16,23 H16.1 M24,23 H24.1 M32,23 H32.1 M40,23 H40.1 M16,33 H16.1 M24,33 H32 M40,33 H40.1")},
            {"音箱",Icon("M21,8 H35 Q43,8 43,16 V40 Q43,48 35,48 H21 Q13,48 13,40 V16 Q13,8 21,8 Z M28,17 H28.1 M28,24 A8,8 0 1 1 28,40 A8,8 0 1 1 28,24 Z")},
            {"耳机",Icon("M10,30 V26 A18,18 0 0 1 46,26 V30 M10,29 H14 Q18,29 18,33 V41 Q18,45 14,45 Q10,45 10,41 Z M46,29 H42 Q38,29 38,33 V41 Q38,45 42,45 Q46,45 46,41 Z")},
            {"耳塞",Icon("M16,10 A7,7 0 0 0 16,24 H17 V34 A3,3 0 0 0 23,34 V17 A7,7 0 0 0 16,10 Z M40,20 A7,7 0 0 1 40,34 H39 V44 A3,3 0 0 1 33,44 V27 A7,7 0 0 1 40,20 Z")},
            {"手柄",Icon("M19,13 H37 C42,13 45,17 47,22 L51,33 C54,42 47,46 41,41 L36,37 H20 L15,41 C9,46 2,42 5,33 L9,22 C11,17 14,13 19,13 Z M18,21 V31 M13,26 H23 M37,24 H37.1 M43,30 H43.1")},
            {"手表",Icon("M22,15 L24,6 H32 L34,15 M22,41 L24,50 H32 L34,41 M23,15 H33 Q40,15 40,22 V34 Q40,41 33,41 H23 Q16,41 16,34 V22 Q16,15 23,15 Z M40,25 H43 M28,22 V29 L32,32")},
            {"其他",Icon("M18,12 H38 Q44,12 44,18 V38 Q44,44 38,44 H18 Q12,44 12,38 V18 Q12,12 18,12 Z M23,28 A5,5 0 1 1 33,28 A5,5 0 1 1 23,28 Z")}
        };
        private static readonly Geometry Wifi=WirelessGlyph();
        private static readonly Geometry Bluetooth=RoundGlyph("M7,7 L17,17 L12,22 V2 L17,7 L7,17",2.25);
        private static readonly Geometry Cable=RoundGlyph("M12,4 V20 M9,7 L12,4 L15,7 M12,14 L17,10 V8 M12,17 L7,12 V10",1.9);

        internal static string Category(Device device)
        {
            var name=device.Name??"";
            if(device.Kind!="音频"&&device.Kind!="其他") return device.Kind;
            if(device.Kind=="其他"&&Hardware.IsWatchName(name)) return "手表";
            if(Earbuds.Any(x=>Hardware.Has(name,x))) return "耳塞";
            if(Headphones.Any(x=>Hardware.Has(name,x))) return "耳机";
            if(Speakers.Any(x=>Hardware.Has(name,x))) return "音箱";
            if(Hardware.Has(name,"Joy-Con")||Hardware.Has(name,"DualShock")) return "手柄";
            return device.Kind;
        }

        internal static Viewbox Illustration(string category)
        {
            var artboard=new Canvas {Width=56,Height=56};
            artboard.Children.Add(new System.Windows.Shapes.Path
            {
                Data=Outline(category),Stroke=Preferences.Current.IconInk,StrokeThickness=3.5,
                StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round
            });
            return new Viewbox {Child=artboard,IsHitTestVisible=false};
        }

        internal static Geometry Outline(string category)
        {
            Geometry icon;
            if(category=="音频") category="音箱";
            return Art.TryGetValue(category??"其他",out icon)?icon:Art["其他"];
        }

        private static Geometry Icon(string path)
        {
            var outline=Geometry.Parse(path);
            outline.Freeze();
            return outline;
        }

        internal static System.Windows.Shapes.Path Signal(string link,Brush color,double size)
        {
            Geometry shape=link=="2.4G"?Wifi:link=="蓝牙"?Bluetooth:link=="USB"?Cable:null;
            if(shape==null) return null;
            var glyph=new System.Windows.Shapes.Path {Data=shape,Fill=color,Width=size,Height=size,Stretch=Stretch.Uniform,
                VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};
            return glyph;
        }

        private static Geometry WirelessGlyph()
        {
            var glyph=new GeometryGroup {FillRule=FillRule.Nonzero};
            glyph.Children.Add(RoundGlyph("M2,7 C8,2 16,2 22,7 M6,11 C10,8 14,8 18,11 M9,15 C11,13.6 13,13.6 15,15",2));
            glyph.Children.Add(new EllipseGeometry(new Point(12,19),1.45,1.45));
            glyph.Freeze();return glyph;
        }

        private static Geometry RoundGlyph(string value,double width)
        {
            var pen=new Pen(Brushes.Black,width) {StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};
            var glyph=Geometry.Parse(value).GetWidenedPathGeometry(pen);
            glyph.Freeze();return glyph;
        }

        internal static System.Windows.Shapes.Path Stroke(string geometry,Brush color,double size,double thickness=1.7)
        {
            return new System.Windows.Shapes.Path {Data=Geometry.Parse(geometry),Width=size,Height=size,Stretch=Stretch.Uniform,
                Stroke=color,StrokeThickness=thickness,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,
                StrokeLineJoin=PenLineJoin.Round,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};
        }
    }

    internal sealed class PowerStage : Viewbox
    {
        private const double HeightUnits=96;
        private readonly Canvas well=new Canvas{Width=84,Height=HeightUnits};
        private readonly TranslateTransform level=new TranslateTransform(), front=new TranslateTransform(), back=new TranslateTransform(), buoy=new TranslateTransform();
        private readonly Canvas water=new Canvas{Width=84,Height=HeightUnits};
        private readonly System.Windows.Shapes.Path first,second;
        private readonly Border backdrop;
        private readonly Canvas meter=new Canvas {Width=84,Height=HeightUnits};
        private readonly Border barTrack=new Border {Width=60,Height=6,CornerRadius=new CornerRadius(3)},
            barFill=new Border {Height=6,CornerRadius=new CornerRadius(3)};
        private readonly System.Windows.Shapes.Path ringTrack=new System.Windows.Shapes.Path
            {Data=new EllipseGeometry(new Point(42,48),33,33),StrokeThickness=4.5},
            ringFill=new System.Windows.Shapes.Path {StrokeThickness=4.5,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round};
        private readonly RenderLoop clock;
        private bool charging,hovered;
        private double floatPhase;
        private string category;
        private Look pictureLook;
        private Viewbox picture;

        internal PowerStage()
        {
            well.Clip=new RectangleGeometry(new Rect(0,0,84,HeightUnits),17,17);
            backdrop=new Border {Width=84,Height=HeightUnits};
            well.Children.Add(backdrop);
            water.RenderTransform=level;
            back.X=-42;back.Y=4;
            second=Wave(back,.23);
            first=Wave(front,.54);
            water.Children.Add(second);
            water.Children.Add(first);
            well.Children.Add(water);
            Canvas.SetLeft(barTrack,12);Canvas.SetTop(barTrack,75);
            Canvas.SetLeft(barFill,12);Canvas.SetTop(barFill,75);
            meter.Children.Add(barTrack);meter.Children.Add(barFill);meter.Children.Add(ringTrack);meter.Children.Add(ringFill);
            well.Children.Add(meter);
            Child=well;
            IsHitTestVisible=false;
            clock=new RenderLoop(Flow);
            IsVisibleChanged+=delegate {UpdateWave();};
        }

        internal bool IsWaving {get {return clock.Active;}}

        internal void SetHovered(bool value)
        {
            hovered=value;
            UpdateWave();
        }

        private static System.Windows.Shapes.Path Wave(TranslateTransform shift,double opacity)
        {
            return new System.Windows.Shapes.Path
            {
                Data=Geometry.Parse("M0,7 C14,7 21,-3 42,-3 C63,-3 70,7 84,7 C98,7 105,-3 126,-3 C147,-3 154,7 168,7 L168,104 H0 Z"),
                RenderTransform=shift,Opacity=opacity
            };
        }

        internal void Show(string kind,int? value,bool charge,bool animate,int? styleOverride=null)
        {
            var look=Preferences.Current;
            if(kind!=category||pictureLook!=look)
            {
                category=kind;
                pictureLook=look;
                if(picture!=null) well.Children.Remove(picture);
                picture=DeviceArt.Illustration(kind);
                picture.Width=65;picture.Height=65;
                picture.RenderTransform=buoy;
                Canvas.SetLeft(picture,9.5);Canvas.SetTop(picture,15.5);
                well.Children.Add(picture);
            }
            int style=styleOverride??Preferences.CurrentCardStyle;
            int reading=Math.Max(0,Math.Min(100,value.GetValueOrDefault()));
            backdrop.Background=style==3?Brushes.Transparent:look.Stage;
            first.Fill=second.Fill=look.Power(reading,true);
            picture.Width=picture.Height=style==1||style==2?49:65;
            Canvas.SetLeft(picture,(84-picture.Width)/2);
            Canvas.SetTop(picture,(HeightUnits-picture.Height)/2-(style==1?9:0));
            barTrack.Visibility=barFill.Visibility=style==1?Visibility.Visible:Visibility.Hidden;
            barTrack.Background=look.Line;
            barFill.Background=first.Fill;
            barFill.Width=.60*reading;
            ringTrack.Visibility=ringFill.Visibility=style==2?Visibility.Visible:Visibility.Hidden;
            ringTrack.Stroke=look.Line;
            ringFill.Stroke=first.Fill;
            ringFill.Data=style==2?Ring(reading):null;
            double target=HeightUnits-((value??0)*HeightUnits/100.0);
            if(charge) target=Math.Max(5,Math.Min(88,target));
            water.Visibility=style==0&&(reading>0||charge)?Visibility.Visible:Visibility.Hidden;
            double prior=level.Y;
            if((double)level.GetAnimationBaseValue(TranslateTransform.YProperty)!=target)
            {
                level.Y=target;
                level.BeginAnimation(TranslateTransform.YProperty,animate?Motion.Tween(prior,target,470):null);
            }
            charging=charge;
            UpdateWave();
        }

        private static Geometry Ring(int value)
        {
            if(value==100) return new EllipseGeometry(new Point(42,48),33,33);
            if(value==0) return null;
            double angle=Math.PI*2*value/100;
            var figure=new PathFigure {StartPoint=new Point(42,15),IsFilled=false};
            figure.Segments.Add(new ArcSegment(new Point(42+33*Math.Sin(angle),48-33*Math.Cos(angle)),
                new Size(33,33),0,value>50,SweepDirection.Clockwise,true));
            return new PathGeometry(new[]{figure});
        }

        private void UpdateWave()
        {
            if(!charging||!Motion.Enabled||!IsVisible) {floatPhase=0;buoy.Y=0;}
            if(IsVisible&&Motion.Enabled&&water.Visibility==Visibility.Visible&&(charging||hovered)) clock.Start();
            else clock.Stop();
        }

        private void Flow(double seconds)
        {
            if(!Motion.Enabled) {UpdateWave();return;}
            front.X-=seconds*35;
            if(front.X<=-84) front.X+=84;
            back.X+=seconds*(84d/3.6);
            if(back.X>=0) back.X-=84;
            if(charging)
            {
                floatPhase=(floatPhase+seconds*Math.PI*2/1.45)%(Math.PI*2);
                buoy.Y=-1.5+1.5*Math.Cos(floatPhase);
            }
        }
    }
}
