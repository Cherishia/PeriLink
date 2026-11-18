using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace LinkScope
{
    internal static class BatteryArt
    {
        private static readonly Brush Outline=Dashboard.BrushOf("#202A2C"), Plate=Dashboard.BrushOf("#839087"),
            Cap=Dashboard.BrushOf("#D6DFBF"), CapShade=Dashboard.BrushOf("#A6AE8D"),
            CaseSide=Dashboard.BrushOf("#4A5751"), Bottom=Dashboard.BrushOf("#59665E"),
            FrameLight=Dashboard.BrushOf("#DEECD8");
        private static readonly Geometry Case=Geometry.Parse("M11,0 H21 V2 H25 V4 H28 V6 H31 V26 H29 V28 H26 V30 H22 V32 H4 V30 H1 V9 H3 V7 H6 V5 H9 V3 H11 Z");
        private static readonly Geometry Top=Geometry.Parse("M4,9 L14,4 H25 V5 H28 L21,9 Z");
        private static readonly Geometry Side=Geometry.Parse("M22,9 L29,6 H30 V25 H28 V27 H25 V29 H22 Z");
        internal static readonly Geometry Interior=Geometry.Parse("M6,10 H19 V11 H21 V27 H6 V26 H4 V12 H6 Z");
        internal static readonly Geometry Flow=Geometry.Parse(
            "M0,3 H3 V2 H6 V0 H10 V1 H13 V2 H16 V3 H19 V2 H22 V0 H26 V1 H29 V2 H32 V3 H35 V2 H38 V0 H42 V1 H45 V2 H48 V3 V36 H0 Z");

        internal static Brush Tint(Color color,Color other,double blend)
        {
            var brush=new SolidColorBrush(Look.Blend(color,other,blend));
            brush.Freeze();return brush;
        }

        internal static void Paint(DrawingContext dc,Color color,double fill)
        {
            PaintCase(dc,color);
            if(fill>0)
            {
                dc.PushClip(Interior);
                dc.PushTransform(new TranslateTransform(-3,27-fill*19-1));
                dc.DrawGeometry(Tint(color,Colors.White,.48),null,Flow);
                dc.Pop();
                dc.PushTransform(new TranslateTransform(-7,27-fill*19+1));
                dc.DrawGeometry(Tint(color,Colors.White,.12),null,Flow);
                dc.Pop();
                dc.Pop();
            }
            PaintSheen(dc,color,fill);
        }

        internal static void PaintCase(DrawingContext dc,Color color)
        {
            dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,32,32));
            dc.DrawGeometry(Outline,null,Case);
            dc.DrawGeometry(Plate,null,Top);
            dc.DrawGeometry(CaseSide,null,Side);
            dc.DrawRectangle(CapShade,null,new Rect(11,2,12,3));
            dc.DrawRectangle(Cap,null,new Rect(12,1,8,3));
            dc.DrawRectangle(FrameLight,null,new Rect(12,1,6,1));
            dc.DrawRectangle(Plate,null,new Rect(3,9,19,21));
            dc.DrawRectangle(Outline,null,new Rect(4,10,18,19));
            dc.DrawGeometry(Tint(color,Color.FromRgb(24,44,46),.67),null,Interior);
            dc.DrawRectangle(Tint(color,Colors.White,.38),null,new Rect(24,10,2,12));
            dc.DrawRectangle(Tint(color,Color.FromRgb(24,44,46),.52),null,new Rect(27,8,2,14));
            dc.DrawRectangle(Bottom,null,new Rect(4,29,17,2));
            dc.DrawRectangle(FrameLight,null,new Rect(6,8,13,1));
            dc.DrawRectangle(FrameLight,null,new Rect(3,11,1,15));
        }

        internal static void PaintSheen(DrawingContext dc,Color color,double fill)
        {
            dc.DrawRectangle(Tint(color,Colors.White,.56),null,new Rect(5,12,2,4));
            dc.DrawRectangle(Tint(color,Colors.White,.76),null,new Rect(7,11,4,1));
            if(fill>=.35) dc.DrawRectangle(Tint(color,Colors.White,.65),null,new Rect(13,23,2,2));
            if(fill>=.52) dc.DrawRectangle(Tint(color,Colors.White,.55),null,new Rect(17,20,2,1));
            dc.DrawRectangle(Tint(color,Colors.White,.42),null,new Rect(7,27,12,1));
        }

        internal static DrawingImage Layer(Action<DrawingContext> paint)
        {
            var group=new DrawingGroup();
            using(var dc=group.Open())
            {
                dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,32,32));
                paint(dc);
            }
            var image=new DrawingImage(group);image.Freeze();return image;
        }
    }

    internal sealed class BatteryMark : Viewbox
    {
        private readonly Image shell=new Image {Width=32,Height=32}, sheen=new Image {Width=32,Height=32};
        private readonly TranslateTransform forward=new TranslateTransform(-5,15), trailing=new TranslateTransform(-9,13);
        private readonly Path front,back;
        private Color? accent;
        private bool moving;

        internal BatteryMark()
        {
            var frame=new Canvas {Width=32,Height=32};
            frame.Children.Add(shell);
            var tank=new Canvas {Width=32,Height=32,Clip=BatteryArt.Interior};
            back=new Path {Data=BatteryArt.Flow,RenderTransform=trailing};
            front=new Path {Data=BatteryArt.Flow,RenderTransform=forward};
            tank.Children.Add(back);tank.Children.Add(front);frame.Children.Add(tank);
            frame.Children.Add(sheen);
            Child=frame;
            IsHitTestVisible=false;
            Loaded+=delegate {ApplyLook();};
            IsVisibleChanged+=delegate {SyncMotion(IsVisible);};
            Unloaded+=delegate {SyncMotion(false);};
            ApplyLook();
        }

        internal void ApplyLook()
        {
            Color next=Preferences.Current.Accent;
            if(accent!=next)
            {
                accent=next;
                shell.Source=BatteryArt.Layer(dc=>BatteryArt.PaintCase(dc,next));
                sheen.Source=BatteryArt.Layer(dc=>BatteryArt.PaintSheen(dc,next,.68));
                back.Fill=BatteryArt.Tint(next,Colors.White,.48);
                front.Fill=BatteryArt.Tint(next,Colors.White,.12);
            }
            SyncMotion(IsVisible);
        }

        private void SyncMotion(bool visible)
        {
            bool enable=visible&&Motion.Enabled;
            if(moving==enable) return;
            moving=enable;
            forward.BeginAnimation(TranslateTransform.XProperty,enable?Slide(-16,0,1780):null);
            trailing.BeginAnimation(TranslateTransform.XProperty,enable?Slide(0,-16,2660):null);
        }

        private static DoubleAnimation Slide(double start,double end,int duration)
        {return Motion.At60(new DoubleAnimation(start,end,TimeSpan.FromMilliseconds(duration)){RepeatBehavior=RepeatBehavior.Forever});}
    }
}
