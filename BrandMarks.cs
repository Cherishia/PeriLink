using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LinkScope
{
    internal static class BrandMarks
    {
        private static readonly Lazy<Dictionary<string,Geometry>> Vectors=new Lazy<Dictionary<string,Geometry>>(ReadVectors);
        private static readonly Dictionary<string,string> Images=new Dictionary<string,string>
        {{"ATK","atk.png"},{"VGN","vgn.png"},{"MCHOSE","mchose.jpg"},{"GAMESIR","gamesir.png"}};
        private static readonly Dictionary<string,ImageBrush> Masks=new Dictionary<string,ImageBrush>();
        private static readonly HashSet<string> Symbols=new HashSet<string>
        {"ROG","XBOX","PLAYSTATION","RAZER","APPLE","BEATS"};
        internal static int VectorCount {get {return Vectors.Value.Count;}}

        internal static FrameworkElement Mark(string brand,Brush ink)
        {
            string image;
            if(Images.TryGetValue(brand,out image))
            {
                ImageBrush mask;
                if(!Masks.TryGetValue(brand,out mask))
                {
                    mask=ImageMask(image);
                    Masks[brand]=mask;
                }
                if(mask!=null) return new Border {Width=brand=="ATK"?45:67,Height=15,Background=ink,OpacityMask=mask,
                    HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center};
            }
            Geometry logo;
            if(!Vectors.Value.TryGetValue(brand,out logo)) return null;
            bool symbol=Symbols.Contains(brand);
            var path=new System.Windows.Shapes.Path {Data=logo,Fill=ink,Stretch=Stretch.Uniform,
                Width=symbol?(brand=="ROG"?23:15):70,Height=15,
                HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center};
            if(!symbol) return path;
            var row=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Left};
            var label=new TextBlock {Text=brand,FontSize=10.5,FontWeight=FontWeights.Bold,Foreground=ink,
                Margin=new Thickness(5,0,0,0),VerticalAlignment=VerticalAlignment.Center};
            row.Children.Add(path);row.Children.Add(label);
            return row;
        }

        private static Dictionary<string,Geometry> ReadVectors()
        {
            var result=new Dictionary<string,Geometry>(StringComparer.Ordinal);
            using(var stream=typeof(BrandMarks).Assembly.GetManifestResourceStream("LinkScope.BrandMarks.tsv"))
            {
                if(stream==null) return result;
                using(var reader=new StreamReader(stream))
                {
                    for(string line=reader.ReadLine();line!=null;line=reader.ReadLine())
                    {
                        int split=line.IndexOf('\t');
                        if(split<=0) continue;
                        try
                        {
                            var art=Geometry.Parse("F1 "+line.Substring(split+1));
                            if(art.Bounds.IsEmpty) continue;
                            art.Freeze();
                            result[line.Substring(0,split)]=art;
                        }
                        catch(FormatException) {}
                    }
                }
            }
            return result;
        }

        private static ImageBrush ImageMask(string file)
        {
            using(var stream=typeof(BrandMarks).Assembly.GetManifestResourceStream("LinkScope."+file))
            {
                if(stream==null) return null;
                BitmapSource source=BitmapFrame.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
                if(file=="mchose.jpg") source=LuminanceMask(source);
                var mask=new ImageBrush(source){Stretch=Stretch.Uniform,AlignmentX=AlignmentX.Left};
                mask.Freeze();
                return mask;
            }
        }
        private static BitmapSource LuminanceMask(BitmapSource source)
        {
            int width=source.PixelWidth,height=source.PixelHeight;
            var sprite=new CroppedBitmap(source,new Int32Rect(width/12,height*2/5,width*5/6,height/5));
            var pixels=new FormatConvertedBitmap(sprite,PixelFormats.Bgra32,null,0);
            int stride=pixels.PixelWidth*4;
            var colors=new byte[stride*pixels.PixelHeight];
            pixels.CopyPixels(colors,stride,0);
            for(int i=0;i<colors.Length;i+=4)
            {
                colors[i+3]=colors[i+1];
                colors[i]=colors[i+1]=colors[i+2]=255;
            }
            return BitmapSource.Create(pixels.PixelWidth,pixels.PixelHeight,96,96,PixelFormats.Bgra32,null,colors,stride);
        }
    }
}
