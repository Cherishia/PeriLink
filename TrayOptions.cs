using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using Forms=System.Windows.Forms;

namespace LinkScope
{
    internal sealed class TrayOptions : Window
    {
        private static readonly ControlTemplate Row=(ControlTemplate)XamlReader.Parse(@"
          <ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
              xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ButtonBase}'>
            <Border x:Name='frame' CornerRadius='8' Background='{TemplateBinding Background}'>
              <ContentPresenter VerticalAlignment='Center' HorizontalAlignment='Stretch'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='frame' Property='Background' Value='{DynamicResource TrayHover}'/></Trigger>
              <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='frame' Property='Background' Value='{DynamicResource TrayHover}'/></Trigger>
              <Trigger Property='IsPressed' Value='True'><Setter TargetName='frame' Property='Background' Value='{DynamicResource TrayPressed}'/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>");
        private readonly List<ButtonBase> items=new List<ButtonBase>();
        private readonly Border surface;
        private readonly Border separator;
        private readonly StackPanel rows;
        private readonly CheckBox startup;
        private readonly TextBlock startupStatus;

        internal TrayOptions(Action open,Action refresh,Action settings,Action quit)
        {
            Title=Dashboard.AppName+" · 托盘菜单";
            ShowInTaskbar=false;Topmost=true;Width=228;SizeToContent=SizeToContent.Height;
            WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
            AllowsTransparency=true;Background=Brushes.Transparent;
            FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI");UseLayoutRounding=true;
            rows=new StackPanel {Margin=new Thickness(5)};
            Add(rows,"打开 "+Dashboard.AppName,"M3,10 L12,3 L21,10 M5,9 V21 H9 V15 H15 V21 H19 V9",open);
            Add(rows,"刷新电量","M20,12 A8,8 0 1 1 18,6 M18,3 V9 H12",refresh);
            Add(rows,"设置","M4,6 H20 M4,12 H20 M4,18 H20 M9,4 V8 M15,10 V14 M8,16 V20",settings);
            var startupRow=new DockPanel {Margin=new Thickness(11,0,10,0)};
            startupStatus=new TextBlock {FontSize=11,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,0,0)};
            startupStatus.SetResourceReference(TextBlock.ForegroundProperty,"TrayMuted");
            DockPanel.SetDock(startupStatus,Dock.Right);
            startupRow.Children.Add(startupStatus);
            startupRow.Children.Add(MenuLabel("开机自启","M12,3 V11 M18,6 A8,8 0 1 1 6,6"));
            startup=new CheckBox {Template=Row,Background=Brushes.Transparent,Content=startupRow,Height=35,
                FocusVisualStyle=null,ToolTip="登录 Windows 后在托盘启动"};
            AutomationProperties.SetName(startup,"开机自启");
            startup.Click+=delegate
            {
                bool saved=StartupRegistration.SetEnabled(startup.IsChecked==true);
                SyncStartup();
                if(!saved) startupStatus.Text="保存失败";
            };
            items.Add(startup);
            rows.Children.Add(startup);
            separator=new Border {Height=1,Margin=new Thickness(7,4,7,4)};
            rows.Children.Add(separator);
            Add(rows,"退出 "+Dashboard.AppName,"M12,4 H5 V20 H12 M10,12 H20 M16,8 L20,12 L16,16",quit);
            surface=new Border {CornerRadius=new CornerRadius(16),BorderThickness=new Thickness(1),
                Padding=new Thickness(3),Child=rows};
            Content=surface;
            ApplyLook();
            Deactivated+=delegate {if(IsVisible) Hide();};
            PreviewKeyDown+=OnMenuKey;
        }

        internal void ApplyLook()
        {
            var theme=Preferences.Current;
            Resources["TrayInk"]=theme.Ink;
            Resources["TrayIcon"]=theme.IconInk;
            Resources["TrayMuted"]=theme.Muted;
            Resources["TrayHover"]=new SolidColorBrush(Look.Blend(theme.Tonal.Color,theme.Accent,.17));
            Resources["TrayPressed"]=new SolidColorBrush(Look.Blend(theme.Tonal.Color,theme.Accent,.32));
            surface.Background=theme.Backdrop(Preferences.CurrentMaterial,true);
            surface.BorderBrush=theme.Line;
            separator.Background=theme.Line;
        }

        private void Add(Panel rows,string label,string path,Action action)
        {
            var group=MenuLabel(label,path);
            group.Margin=new Thickness(11,0,10,0);
            var row=new Button {Template=Row,Background=Brushes.Transparent,Content=group,Height=35,FocusVisualStyle=null};
            AutomationProperties.SetName(row,label);
            row.Click+=delegate {Hide();action();};
            items.Add(row);rows.Children.Add(row);
        }

        private static StackPanel MenuLabel(string label,string path)
        {
            var group=new StackPanel {Orientation=Orientation.Horizontal};
            var icon=DeviceArt.Stroke(path,null,15);
            icon.SetResourceReference(System.Windows.Shapes.Path.StrokeProperty,"TrayIcon");
            icon.Margin=new Thickness(0,0,10,0);
            group.Children.Add(icon);
            var text=new TextBlock {Text=label,FontSize=13,VerticalAlignment=VerticalAlignment.Center};
            text.SetResourceReference(TextBlock.ForegroundProperty,"TrayInk");
            group.Children.Add(text);
            return group;
        }

        private void SyncStartup()
        {
            bool enabled=StartupRegistration.IsEnabled;
            startup.IsChecked=enabled;
            startupStatus.Text=enabled?"已开启":"已关闭";
        }

        private void OnMenuKey(object sender,KeyEventArgs e)
        {
            if(e.Key==Key.Escape) {Hide();e.Handled=true;return;}
            if(e.Key!=Key.Up&&e.Key!=Key.Down) return;
            int index=items.FindIndex(button=>button.IsKeyboardFocused);
            int next=index<0?(e.Key==Key.Down?0:items.Count-1):(index+(e.Key==Key.Down?1:-1)+items.Count)%items.Count;
            items[next].Focus();
            e.Handled=true;
        }

        internal void Open(System.Drawing.Point pointer)
        {
            SyncStartup();
            var screen=Forms.Screen.FromPoint(pointer);
            new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
            DisplayPlacement.SeatOnMonitor(this,screen);
            double dpi=DisplayPlacement.Scale(this);
            var area=screen.WorkingArea;
            surface.Measure(new Size(Width,double.PositiveInfinity));
            int width=(int)Math.Ceiling(Width*dpi),height=(int)Math.Ceiling(surface.DesiredSize.Height*dpi);
            int left=Math.Max(area.Left,Math.Min(pointer.X-width+(int)(22*dpi),area.Right-width));
            int top=pointer.Y-height+(int)(8*dpi);
            if(top<area.Top) top=pointer.Y-(int)(8*dpi);
            top=Math.Max(area.Top,Math.Min(top,area.Bottom-height));
            var location=new System.Drawing.Point(left,top);
            DisplayPlacement.Move(this,location);
            Show();
            DisplayPlacement.Move(this,location);
            Activate();
            Focus();
            Motion.Enter(rows,3,true);
        }
    }
}
