using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace LinkScope
{
    internal static class PixelUi
    {
        internal static readonly FontFamily Numbers=new FontFamily("Cascadia Mono, Consolas, Microsoft YaHei UI");
        internal static readonly Brush Edge=Dashboard.BrushOf("#61776F"), LightEdge=Dashboard.BrushOf("#A4B6AF");
        internal static readonly Style KeyboardFocus=(Style)XamlReader.Parse(@"
          <Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Control}'>
            <Setter Property='Template'>
              <Setter.Value>
                <ControlTemplate TargetType='{x:Type Control}'>
                  <Rectangle Margin='1' RadiusX='6' RadiusY='6' StrokeThickness='1'
                             Stroke='{DynamicResource {x:Static SystemColors.HighlightBrushKey}}'
                             StrokeDashArray='2,2' SnapsToDevicePixels='True'/>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>");
        internal static Brush Grid(Color color,int pitch=9)
        {
            var tile=new DrawingGroup();
            using(var dc=tile.Open())
            {
                dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,pitch,pitch));
                dc.DrawRectangle(new SolidColorBrush(color),null,new Rect(0,0,1,1));
            }
            var brush=new DrawingBrush(tile) {TileMode=TileMode.Tile,ViewportUnits=BrushMappingMode.Absolute,
                Viewport=new Rect(0,0,pitch,pitch),Stretch=Stretch.None};
            brush.Freeze();
            return brush;
        }

    }

    internal sealed class RoundedSurface : Grid
    {
        private readonly System.Windows.Shapes.Path stroke;
        private readonly RectangleGeometry outline;
        internal Brush Edge {set {stroke.Stroke=value;}}
        internal double Radius {set {outline.RadiusX=outline.RadiusY=value;}}
        internal RoundedSurface(double radius)
        {
            // The fill, content, highlights and outline all render inside the same curve.
            outline=new RectangleGeometry {RadiusX=radius,RadiusY=radius};
            Clip=outline;
            SizeChanged+=delegate(object sender,SizeChangedEventArgs e)
            {outline.Rect=new Rect(new Point(),e.NewSize);};

            var edge=new Canvas {IsHitTestVisible=false};
            stroke=new System.Windows.Shapes.Path {Data=outline,Stroke=PixelUi.LightEdge,StrokeThickness=1.6};
            edge.Children.Add(stroke);
            SetZIndex(edge,1);
            Children.Add(edge);
        }
    }
}
