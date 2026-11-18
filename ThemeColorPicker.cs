using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace LinkScope
{
    internal sealed class ThemeColorPicker : Window
    {
        internal static readonly string[] PresetHex={null,"#E35C6B","#C87858","#C8A343","#65A579","#4C9DA7","#608CDD","#876BCE","#C46C9D"};
        private readonly ColorField palette=new ColorField(false), spectrum=new ColorField(true);
        private readonly Border sample;
        private readonly TextBox hex;
        private readonly TextBlock error;
        private double hue,saturation,value;
        private bool syncing;
        internal Color SelectedColor {get;private set;}

        internal ThemeColorPicker(Window parent,Color initial)
        {
            Owner=parent;Title=Dashboard.AppName+" · 自定义背景色";
            var work=DisplayPlacement.WorkSize(parent??this);
            Width=Math.Min(352,work.Width-12);MaxHeight=work.Height-12;
            SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;
            WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
            WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;
            UseLayoutRounding=true;FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI");
            var body=new StackPanel {Margin=new Thickness(17,15,17,15)};
            var theme=Preferences.Current;
            var ink=theme.Ink;
            var title=new TextBlock {Text="自定义背景色",FontSize=19,FontWeight=FontWeights.SemiBold,Foreground=ink,
                Margin=new Thickness(0,0,0,12),Background=Brushes.Transparent};
            title.MouseLeftButtonDown+=delegate {try{DragMove();}catch(InvalidOperationException){}};
            body.Children.Add(title);
            palette.Height=170;palette.Margin=new Thickness(0,0,0,9);
            AutomationProperties.SetName(palette,"饱和度和亮度");
            palette.Picked+=p=>{saturation=p.X;value=1-p.Y;Sync(true);};
            body.Children.Add(palette);
            spectrum.Height=26;spectrum.Margin=new Thickness(0,0,0,10);
            AutomationProperties.SetName(spectrum,"色相");
            spectrum.Picked+=p=>{hue=p.X;Sync(true);};
            body.Children.Add(spectrum);

            var entry=new Grid{Margin=new Thickness(0,0,0,10)};
            entry.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(51)});
            entry.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
            sample=new Border {Width=39,Height=35,CornerRadius=new CornerRadius(9),HorizontalAlignment=HorizontalAlignment.Left,
                BorderBrush=Dashboard.BrushOf("#20000000"),BorderThickness=new Thickness(1)};
            entry.Children.Add(sample);
            hex=new TextBox {Height=35,Padding=new Thickness(11,0,5,0),MaxLength=7,FontSize=14.5,
                Background=Brushes.Transparent,BorderThickness=new Thickness(0),VerticalContentAlignment=VerticalAlignment.Center,
                Foreground=ink,FontFamily=new FontFamily("Cascadia Mono, Consolas")};
            AutomationProperties.SetName(hex,"十六进制色值");
            hex.TextChanged+=delegate
            {
                Color color;
                if(!syncing&&Look.TryColor(hex.Text,out color)) Pick(color,false);
            };
            var field=new Border {CornerRadius=new CornerRadius(9),Background=theme.Card,Child=hex,
                BorderBrush=theme.Tonal,BorderThickness=new Thickness(1)};
            Grid.SetColumn(field,1);entry.Children.Add(field);body.Children.Add(entry);
            var colors=new UniformGrid {Columns=6,Margin=new Thickness(0,0,0,7)};
            foreach(string key in new[]{"#F05467","#FF9045","#E8BC38","#36B476","#44AAB1","#557FE7",
                                        "#6670E7","#9B65DA","#D675A2","#C09A78","#9DA6AF","#22262B"})
            {
                Color picked;
                Look.TryColor(key,out picked);
                var chip=Dashboard.ButtonFor("",new SolidColorBrush(picked),ink,37);
                chip.ClearValue(WidthProperty);chip.Height=23;chip.Margin=new Thickness(2,2,2,3);
                AutomationProperties.SetName(chip,key);
                chip.Click+=delegate {Pick(picked,true);};
                colors.Children.Add(chip);
            }
            body.Children.Add(colors);
            error=new TextBlock {Text="请输入六位色值。",Foreground=Dashboard.BrushOf("#AB4137"),FontSize=12,
                Margin=new Thickness(0,0,0,6),Visibility=Visibility.Collapsed};
            body.Children.Add(error);
            var actions=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,
                Margin=new Thickness(0,6,0,0)};
            var cancel=Dashboard.ButtonFor("取消",Brushes.Transparent,ink,61);
            cancel.Height=34;cancel.IsCancel=true;
            var done=Dashboard.ButtonFor("完成",theme.Ink,theme.Shell,75);
            done.Height=34;done.Margin=new Thickness(5,0,0,0);done.IsDefault=true;
            done.Click+=delegate
            {
                Color color;
                if(!Look.TryColor(hex.Text,out color)) {error.Visibility=Visibility.Visible;hex.Focus();return;}
                SelectedColor=color;DialogResult=true;
            };
            actions.Children.Add(cancel);actions.Children.Add(done);body.Children.Add(actions);
            var scroll=new SmoothScroll {Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
            Content=new Border {Padding=new Thickness(3),CornerRadius=new CornerRadius(18),
                Background=theme.Backdrop(Preferences.CurrentMaterial),BorderBrush=theme.Line,
                BorderThickness=new Thickness(1),Child=scroll};
            Pick(initial,true);
        }

        private void Pick(Color color,bool updateField)
        {
            double r=color.R/255d,g=color.G/255d,b=color.B/255d;
            double max=Math.Max(r,Math.Max(g,b)),delta=max-Math.Min(r,Math.Min(g,b));
            if(delta>0)
            {
                double sector=max==r?(g-b)/delta:max==g?2+(b-r)/delta:4+(r-g)/delta;
                hue=(sector+6)%6/6;
            }
            else if(hue==0) hue=.68;
            value=max;saturation=max==0?0:delta/max;
            Sync(updateField);
        }

        private void Sync(bool updateField)
        {
            SelectedColor=Hsv(hue,saturation,value);
            palette.Update(hue,new Point(saturation,1-value));
            spectrum.Update(hue,new Point(hue,.5));
            sample.Background=new SolidColorBrush(SelectedColor);
            error.Visibility=Visibility.Collapsed;
            if(updateField)
            {
                syncing=true;hex.Text=Look.Hex(SelectedColor);syncing=false;
            }
        }

        internal static Color Hsv(double hue,double saturation,double value)
        {
            double phase=(hue%1)*6;
            int sector=(int)phase;
            double tint=value*(1-saturation),fade=value*(1-saturation*(phase-sector)),
                lift=value*(1-saturation*(1-phase+sector));
            switch(sector)
            {
                case 0:return Rgb(value,lift,tint);
                case 1:return Rgb(fade,value,tint);
                case 2:return Rgb(tint,value,lift);
                case 3:return Rgb(tint,fade,value);
                case 4:return Rgb(lift,tint,value);
                default:return Rgb(value,tint,fade);
            }
        }
        private static Color Rgb(double r,double g,double b)
        {return Color.FromRgb((byte)Math.Round(r*255),(byte)Math.Round(g*255),(byte)Math.Round(b*255));}

        private sealed class ColorField : FrameworkElement
        {
            private readonly bool hueOnly;
            private readonly LinearGradientBrush hueBrush,white,black;
            private Point marker;
            private SolidColorBrush main;
            internal event Action<Point> Picked;

            internal ColorField(bool strip)
            {
                hueOnly=strip;Focusable=true;FocusVisualStyle=PixelUi.KeyboardFocus;
                hueBrush=Gradient(new Point(0,.5),new Point(1,.5),Colors.Red,Colors.Yellow,Colors.Lime,Colors.Cyan,Colors.Blue,Colors.Magenta,Colors.Red);
                white=Gradient(new Point(0,.5),new Point(1,.5),Colors.White,Color.FromArgb(0,255,255,255));
                black=Gradient(new Point(.5,0),new Point(.5,1),Color.FromArgb(0,0,0,0),Colors.Black);
                MouseLeftButtonDown+=delegate(object s,MouseButtonEventArgs e) {Focus();CaptureMouse();Set(e.GetPosition(this));e.Handled=true;};
                MouseMove+=delegate(object s,MouseEventArgs e) {if(IsMouseCaptured) Set(e.GetPosition(this));};
                MouseLeftButtonUp+=delegate {if(IsMouseCaptured) ReleaseMouseCapture();};
                KeyDown+=Adjust;
            }

            internal void Update(double hue,Point point)
            {marker=point;main=new SolidColorBrush(Hsv(hue,1,1));InvalidateVisual();}

            private Rect Surface
            {
                get {return new Rect(6,hueOnly?8:6,Math.Max(1,ActualWidth-12),Math.Max(1,ActualHeight-(hueOnly?16:12)));}
            }

            protected override void OnRender(DrawingContext dc)
            {
                var r=Surface;
                dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,ActualWidth,ActualHeight));
                dc.PushClip(new RectangleGeometry(r,hueOnly?4:10,hueOnly?4:10));
                dc.DrawRectangle(hueOnly?(Brush)hueBrush:main,null,r);
                if(!hueOnly) {dc.DrawRectangle(white,null,r);dc.DrawRectangle(black,null,r);}
                dc.Pop();
                var center=new Point(r.X+marker.X*r.Width,r.Y+marker.Y*r.Height);
                dc.DrawEllipse(null,new Pen(Dashboard.BrushOf("#71000000"),3.5),center,6,6);
                dc.DrawEllipse(hueOnly?Brushes.White:null,new Pen(Brushes.White,2),center,5.5,5.5);
            }

            private void Set(Point point)
            {
                var r=Surface;
                Publish(new Point((point.X-r.X)/r.Width,hueOnly?.5:(point.Y-r.Y)/r.Height));
            }
            private void Adjust(object sender,KeyEventArgs e)
            {
                double step=(Keyboard.Modifiers&ModifierKeys.Shift)!=0?.055:.015;
                var updated=marker;
                switch(e.Key)
                {
                    case Key.Left:updated.X-=step;break;
                    case Key.Right:updated.X+=step;break;
                    case Key.Up:if(hueOnly) updated.X+=step;else updated.Y-=step;break;
                    case Key.Down:if(hueOnly) updated.X-=step;else updated.Y+=step;break;
                    default:return;
                }
                Publish(updated);e.Handled=true;
            }
            private void Publish(Point point)
            {if(Picked!=null) Picked(new Point(Math.Max(0,Math.Min(1,point.X)),Math.Max(0,Math.Min(1,point.Y))));}

            private static LinearGradientBrush Gradient(Point start,Point finish,params Color[] colors)
            {
                var brush=new LinearGradientBrush {StartPoint=start,EndPoint=finish};
                for(int i=0;i<colors.Length;i++) brush.GradientStops.Add(new GradientStop(colors[i],i/(double)(colors.Length-1)));
                brush.Freeze();return brush;
            }
        }
    }
}
