using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace LinkScope
{
    internal sealed class AmbientBackdrop : Grid
    {
        private readonly Rectangle near,far;
        private readonly TranslateTransform nearShift=new TranslateTransform(),farShift=new TranslateTransform();
        private readonly RenderLoop clock;
        private readonly bool animated;
        private Window owner;
        private Look theme;
        private int style=-1;
        private double phase;

        internal AmbientBackdrop(bool animated=true)
        {
            this.animated=animated;
            IsHitTestVisible=false;
            ClipToBounds=true;
            far=AddLayer(farShift);near=AddLayer(nearShift);
            clock=new RenderLoop(Tick);
            SizeChanged+=delegate {PlaceLayers();};
            if(!animated) return;
            Loaded+=delegate
            {
                owner=Window.GetWindow(this);
                if(owner!=null) owner.StateChanged+=OnWindowState;
                UpdateClock();
            };
            Unloaded+=delegate
            {
                if(owner!=null) owner.StateChanged-=OnWindowState;
                owner=null;
                clock.Stop();
            };
            IsVisibleChanged+=delegate {UpdateClock();};
        }

        private Rectangle AddLayer(TranslateTransform shift)
        {
            // Overscan keeps the painted layers covering the surface while they drift.
            var transform=new TransformGroup();
            transform.Children.Add(new ScaleTransform(1.38,1.46));
            transform.Children.Add(shift);
            var layer=new Rectangle {RenderTransform=transform,RenderTransformOrigin=new Point(.5,.5)};
            Children.Add(layer);
            return layer;
        }

        internal void Apply(Look look,int effect)
        {
            if(look!=theme||style!=effect)
            {
                theme=look;style=effect;
                PaintLayers();
                PlaceLayers();
            }
            Visibility=SystemParameters.HighContrast?Visibility.Collapsed:Visibility.Visible;
            UpdateClock();
        }

        private void PaintLayers()
        {
            Color accent=theme.Accent;
            Color blue=Look.Blend(accent,Color.FromRgb(104,145,231),.62);
            Color lilac=Look.Blend(accent,Color.FromRgb(164,124,228),.64);
            Color teal=Look.Blend(accent,Color.FromRgb(64,203,179),.64);
            switch(style)
            {
                case 1:
                    near.Fill=Light(blue,135,new Point(.24,.42),.51,.70);
                    far.Fill=Light(lilac,118,new Point(.72,.47),.63,.72);
                    break;
                case 2:
                    near.Fill=Aurora(teal,true);
                    far.Fill=Aurora(lilac,false);
                    break;
                case 3:
                    near.Fill=Moon(blue);
                    far.Fill=Sky(blue,Color.FromRgb(36,57,126));
                    break;
                case 4:
                    near.Fill=Ocean(teal);
                    far.Fill=Sky(Look.Blend(teal,blue,.53),Color.FromRgb(9,45,106));
                    break;
                case 5:
                    near.Fill=Stars(blue,lilac);
                    far.Fill=Light(lilac,147,new Point(.57,.64),.72,.48);
                    break;
                case 6:
                    near.Fill=Fog(lilac,teal,true);
                    far.Fill=Fog(blue,lilac,false);
                    break;
                default:
                    near.Fill=Light(accent,88,new Point(.30,.27),.64,.64);
                    far.Fill=Light(blue,58,new Point(.72,.72),.58,.65);
                    break;
            }
            near.Fill.Freeze();
            far.Fill.Freeze();
        }

        private static Brush Light(Color color,byte opacity,Point center,double width,double height)
        {
            return new RadialGradientBrush(Look.Alpha(color,opacity),Look.Alpha(color,0))
            {Center=center,GradientOrigin=center,RadiusX=width,RadiusY=height};
        }

        internal static Brush PreviewBase(Look look,int effect)
        {
            Color[] colors={Color.FromRgb(33,46,65),Color.FromRgb(53,43,73),Color.FromRgb(13,45,56),
                Color.FromRgb(12,20,49),Color.FromRgb(10,35,58),Color.FromRgb(22,20,47),Color.FromRgb(35,25,52)};
            return Look.Paint(Look.Blend(colors[effect],look.Accent,.12));
        }

        private Brush Sky(Color upper,Color lower)
        {
            return new LinearGradientBrush(Look.Alpha(upper,theme.IsDark?(byte)101:(byte)53),
                Look.Alpha(lower,theme.IsDark?(byte)177:(byte)100),90);
        }

        private static Brush Aurora(Color color,bool front)
        {
            var ribbon=Geometry.Parse(front?
                "M-14,53 C21,70 52,38 79,34 C112,29 134,60 175,27 L175,63 C142,93 116,53 80,59 C49,64 24,97 -14,76 Z":
                "M-13,21 C26,19 41,53 77,48 C113,43 133,9 174,23 L174,57 C135,43 112,74 76,81 C37,87 21,52 -13,55 Z");
            var glow=new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Look.Alpha(color,0),0),
                new GradientStop(Look.Alpha(color,front?(byte)159:(byte)112),.56),
                new GradientStop(Look.Alpha(color,37),.88),
                new GradientStop(Look.Alpha(color,0),1)
            },86);
            return Scene(dc=>dc.DrawGeometry(glow,null,ribbon),Stretch.Fill);
        }

        private Brush Moon(Color blue)
        {
            return Scene(dc=>
            {
                var center=new Point(110,32);
                dc.DrawEllipse(Light(blue,152,new Point(.5,.5),.5,.5),null,center,27,27);
                var disc=Look.Blend(blue,Colors.White,theme.IsDark?.77:.43);
                dc.DrawGeometry(Look.Paint(disc),null,Geometry.Parse("M113,22 A10,10 0 1 1 101,35 C109,38 117,30 113,22 Z"));
                for(int y=60;y<96;y+=4)
                {
                    double span=13+(y-60)*.30;
                    double x=109+2.5*Math.Sin(y*.45);
                    var shine=new Pen(Look.Paint(Look.Alpha(disc,(byte)(105-(y-60)*2))),.65);
                    dc.DrawLine(shine,new Point(x-span,y),new Point(x+span,y));
                }
            },Stretch.Uniform);
        }

        private Brush Ocean(Color teal)
        {
            var light=Look.Blend(teal,Colors.LightCyan,theme.IsDark?.45:.16);
            var shafts=new LinearGradientBrush(Look.Alpha(light,150),Look.Alpha(light,0),82);
            return Scene(dc=>
            {
                dc.DrawGeometry(shafts,null,Geometry.Parse("M19,-5 H33 L76,96 L51,96 Z M49,-5 H62 L110,91 L93,91 Z M75,-5 H92 L142,78 L127,81 Z"));
                var dots=new Random(208);
                for(int i=0;i<46;i++)
                {
                    var point=new Point(9+dots.NextDouble()*142,16+dots.NextDouble()*80);
                    double radius=i%8==0?1.0:.46;
                    dc.DrawEllipse(Look.Paint(Look.Alpha(light,(byte)(42+dots.Next(91)))),null,point,radius,radius);
                }
            },Stretch.UniformToFill);
        }

        private Brush Stars(Color blue,Color lilac)
        {
            return Scene(dc=>
            {
                var random=new Random(172);
                for(int i=0;i<75;i++)
                {
                    var point=new Point(random.NextDouble()*160,random.NextDouble()*100);
                    Color shine=Look.Blend(i%3==0?lilac:blue,Colors.White,theme.IsDark?.75:.06);
                    var color=Look.Paint(Look.Alpha(shine,(byte)(52+random.Next(127))));
                    double radius=.25+random.NextDouble()*.62;
                    dc.DrawEllipse(color,null,point,radius,radius);
                    if(i%13!=0) continue;
                    var flare=new Pen(color,.45);
                    dc.DrawLine(flare,new Point(point.X-2,point.Y),new Point(point.X+2,point.Y));
                    dc.DrawLine(flare,new Point(point.X,point.Y-2),new Point(point.X,point.Y+2));
                }
            },Stretch.UniformToFill);
        }

        private static Brush Fog(Color main,Color accent,bool front)
        {
            var cloud=Light(main,front?(byte)119:(byte)85,new Point(.5,.5),.5,.5);
            var edges=Light(accent,83,new Point(.5,.5),.5,.5);
            return Scene(dc=>
            {
                for(int i=0;i<9;i++)
                {
                    double x=6+i*19,y=(front?44:65)+14*Math.Sin(i*.77+(front?.3:1.45));
                    dc.DrawEllipse(i%3==0?edges:cloud,null,new Point(x,y),21+i%3*5,12+i%4*3);
                }
            },Stretch.Fill);
        }

        private static Brush Scene(Action<DrawingContext> paint,Stretch stretch)
        {
            var drawing=new DrawingGroup();
            var box=new Rect(0,0,160,100);
            using(var dc=drawing.Open())
            {
                dc.DrawRectangle(Brushes.Transparent,null,box);
                paint(dc);
            }
            return new DrawingBrush(drawing) {Stretch=stretch,ViewboxUnits=BrushMappingMode.Absolute,Viewbox=box};
        }

        private void OnWindowState(object sender,EventArgs e) {UpdateClock();}

        private bool CanRun()
        {return animated&&IsLoaded&&IsVisible&&Motion.Enabled&&(owner==null||owner.WindowState!=WindowState.Minimized);}

        private void UpdateClock()
        {
            if(CanRun()) clock.Start();
            else clock.Stop();
        }

        private void Tick(double seconds)
        {
            if(!CanRun()) {clock.Stop();return;}
            phase=(phase+seconds*(style==3||style==5?.56:.84))%(Math.PI*2);
            PlaceLayers();
        }

        private void PlaceLayers()
        {
            nearShift.X=Math.Sin(phase)*ActualWidth*(style==3?.06:style==6?.16:.113);
            nearShift.Y=Math.Cos(phase)*ActualHeight*(style==3||style==5?.065:.105);
            farShift.X=-Math.Sin(phase)*ActualWidth*.105;
            farShift.Y=-Math.Cos(phase)*ActualHeight*.13;
            near.Opacity=.86+.14*Math.Sin(phase+.85);
        }
    }
}
