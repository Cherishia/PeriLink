using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace LinkScope
{
    internal sealed class PowerText : FrameworkElement
    {
        private const int Advance=6, Rows=7;
        private static readonly Dictionary<char,Geometry> Glyphs=BuildGlyphs();
        private static readonly DependencyProperty LevelProperty=DependencyProperty.Register("Level",typeof(double),typeof(PowerText),
            new PropertyMetadata(0d,(target,args)=>((PowerText)target).SetText(((double)args.NewValue).ToString("0",CultureInfo.InvariantCulture)+"%")));
        private int? goal;
        private int unit=4;
        private Brush ink=Brushes.Black;
        internal string Text {get;private set;}
        internal Brush Foreground
        {
            get {return ink;}
            set {if(ink!=value) {ink=value;InvalidateVisual();}}
        }

        internal PowerText()
        {
            Text="—";
            SnapsToDevicePixels=true;
            RenderOptions.SetEdgeMode(this,EdgeMode.Aliased);
        }

        internal void SetSize(int pixel) {if(unit!=pixel) {unit=pixel;InvalidateMeasure();}}

        internal void Show(int? value,string fallback,bool animate)
        {
            if(!value.HasValue)
            {
                if(goal.HasValue) BeginAnimation(LevelProperty,null);
                goal=null;SetText(fallback);return;
            }
            if(goal==value) return;
            double prior=(double)GetValue(LevelProperty);
            goal=value;
            SetValue(LevelProperty,(double)value.Value);
            if(animate) BeginAnimation(LevelProperty,Motion.Tween(prior,value.Value,440));
            else {BeginAnimation(LevelProperty,null);SetText(value.Value.ToString(CultureInfo.InvariantCulture)+"%");}
        }

        private bool HasPixelGlyphs {get {return Text.Length>0&&Text.All(Glyphs.ContainsKey);}}

        private void SetText(string value)
        {
            if(Text==value) return;
            bool oldPixels=HasPixelGlyphs;
            int oldLength=Text.Length;
            Text=value??"";
            if(!oldPixels||!HasPixelGlyphs||oldLength!=Text.Length) InvalidateMeasure();
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size available)
        {
            double width=HasPixelGlyphs?(Text.Length*Advance-1)*unit:ChineseReading().Width;
            return new Size(Math.Max(1,width),Rows*unit+6);
        }

        protected override void OnRender(DrawingContext dc)
        {
            if(!HasPixelGlyphs)
            {
                dc.DrawText(ChineseReading(),new Point());
                return;
            }
            dc.PushTransform(new TranslateTransform(0,3));
            dc.PushTransform(new ScaleTransform(unit,unit));
            foreach(char glyph in Text)
            {
                dc.DrawGeometry(ink,null,Glyphs[glyph]);
                dc.PushTransform(new TranslateTransform(Advance,0));
            }
            for(int i=0;i<Text.Length+2;i++) dc.Pop();
        }

        private FormattedText ChineseReading()
        {
            return new FormattedText(Text,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Microsoft YaHei UI"),FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal),
                Rows*unit-2,ink,VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }

        private static Dictionary<char,Geometry> BuildGlyphs()
        {
            var rows=new Dictionary<char,int[]>
            {
                {'0',new[]{14,27,25,25,25,27,14}}, {'1',new[]{6,14,6,6,6,6,15}},
                {'2',new[]{14,27,3,6,12,24,31}}, {'3',new[]{30,3,3,14,3,3,30}},
                {'4',new[]{6,14,22,22,31,6,6}}, {'5',new[]{31,24,24,30,3,27,14}},
                {'6',new[]{14,24,24,30,27,27,14}}, {'7',new[]{31,3,6,6,12,12,12}},
                {'8',new[]{14,27,27,14,27,27,14}}, {'9',new[]{14,27,27,15,3,3,14}},
                {'%',new[]{25,26,4,4,4,11,19}}, {'—',new[]{0,0,0,31,31,0,0}}
            };
            var result=new Dictionary<char,Geometry>();
            foreach(var pair in rows)
            {
                var glyph=new GeometryGroup {FillRule=FillRule.Nonzero};
                for(int y=0;y<Rows;y++)
                    for(int x=0;x<5;x++)
                        if((pair.Value[y]&(16>>x))!=0) glyph.Children.Add(new RectangleGeometry(new Rect(x,y,1,1)));
                glyph.Freeze();
                result.Add(pair.Key,glyph);
            }
            return result;
        }
    }
}
