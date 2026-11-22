using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace LinkScope
{
    internal sealed class ConnectionPopup : Window
    {
        private readonly DispatcherTimer expiry=new DispatcherTimer {Interval=TimeSpan.FromSeconds(4)};

        internal ConnectionPopup(IList<string> messages)
        {
            var theme=Preferences.Current;
            ShowActivated=false;
            ShowInTaskbar=false;
            Topmost=true;
            WindowStyle=WindowStyle.None;
            ResizeMode=ResizeMode.NoResize;
            AllowsTransparency=true;
            Background=Brushes.Transparent;
            SizeToContent=SizeToContent.Height;
            Width=350;
            FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI");
            Opacity=0;
            var surface=new RoundedSurface(18) {Background=theme.Card,Edge=theme.Line};
            var layout=new DockPanel {Margin=new Thickness(14,12,16,13)};
            var icon=new Image {Source=AppArt.Symbol(),Width=28,Height=28,Margin=new Thickness(0,2,9,0),VerticalAlignment=VerticalAlignment.Top};
            DockPanel.SetDock(icon,Dock.Left);
            layout.Children.Add(icon);
            var text=new StackPanel();
            text.Children.Add(new TextBlock {Text="设备连接",Foreground=theme.Ink,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,3)});
            foreach(string line in messages.Take(3))
                text.Children.Add(new TextBlock {Text=line,Foreground=theme.Muted,TextTrimming=TextTrimming.CharacterEllipsis});
            if(messages.Count>3)
                text.Children.Add(new TextBlock {Text="另有 "+(messages.Count-3)+" 台设备",Foreground=theme.Muted});
            layout.Children.Add(text);
            surface.Children.Add(layout);
            Content=surface;
            expiry.Tick+=delegate {Close();};
            Closed+=delegate {expiry.Stop();};
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var window=DisplayPlacement.Handle(this);
            const int exStyle=-20;
            const int transparent=0x20, toolWindow=0x80, noActivate=0x08000000;
            SetWindowLong(window,exStyle,GetWindowLong(window,exStyle)|transparent|toolWindow|noActivate);
        }

        internal void ShowAt(Forms.Screen screen)
        {
            Show();
            DisplayPlacement.SeatOnMonitor(this,screen);
            var work=screen.WorkingArea;
            double scale=DisplayPlacement.Scale(this);
            int inset=(int)Math.Round(14*scale);
            Width=Math.Min(350,(work.Width-2*inset)/scale);
            UpdateLayout();
            var bounds=DisplayPlacement.Bounds(this);
            DisplayPlacement.Move(this,new System.Drawing.Point(work.Right-inset-bounds.Width,work.Bottom-inset-bounds.Height));
            Opacity=1;
            if(Motion.Enabled) Motion.Reveal((FrameworkElement)Content,7,0,175);
            expiry.Start();
        }

        [DllImport("user32.dll",EntryPoint="GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd,int index);
        [DllImport("user32.dll",EntryPoint="SetWindowLongW")] private static extern int SetWindowLong(IntPtr hwnd,int index,int value);
    }
}
