using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

namespace LinkScope
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            AppContext.SetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges",false);
            if(args.Length>0 && args[0]=="--apply-update")
            {
                AppUpdates.Apply(args);
                return;
            }
            if(args.Length==2 && args[0]=="--check")
            {
                Diagnostics.Run(args[1]);
                return;
            }
            bool startup=args.Contains(StartupRegistration.Argument,StringComparer.OrdinalIgnoreCase);
            bool first;
            using(var instance=new Mutex(true,@"Local\PeriLink.Battery.17",out first))
            using(var reopen=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\PeriLink.Battery.17.Open"))
            {
                if(!first) { if(!startup) reopen.Set();return; }
                var app=new Application { ShutdownMode=ShutdownMode.OnMainWindowClose };
                var view=new Dashboard(RefreshSettings.PortablePath);
                app.SessionEnding+=delegate { view.Shutdown(); };
                var wake=ThreadPool.RegisterWaitForSingleObject(reopen,delegate
                {
                    if(!app.Dispatcher.HasShutdownStarted) app.Dispatcher.BeginInvoke(new Action(view.Reopen));
                },null,Timeout.Infinite,false);
                try
                {
                    if(startup)
                    {
                        app.MainWindow=view;
                        view.StartFromTray();
                        AppUpdates.SignalReady(args);
                        app.Run();
                    }
                    else
                    {
                        view.Loaded+=delegate { AppUpdates.SignalReady(args); };
                        app.Run(view);
                    }
                }
                finally { wake.Unregister(null); }
            }
        }
    }

    internal sealed class Dashboard : Window
    {
        internal const string AppName="PeriLink",WindowTitle=AppName+" · 外设电量";
        internal const string RepositoryUrl="https://github.com/Cherishia/PeriLink";
        internal static readonly int[] Intervals={15,30,60};
        private static readonly Brush Ink=BrushOf("#203D36"), Muted=BrushOf("#4E6761");
        internal static Brush PrimaryColor { get { return Preferences.Current.Ink; } }
        private static readonly ControlTemplate PixelButton=(ControlTemplate)XamlReader.Parse(@"
          <ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
              xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ButtonBase}'>
            <Border x:Name='frame' CornerRadius='11' SnapsToDevicePixels='True' Background='{TemplateBinding Background}'
                BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' RecognizesAccessKey='True'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='frame' Property='Opacity' Value='0.82'/></Trigger>
              <Trigger Property='IsPressed' Value='True'><Setter TargetName='frame' Property='Opacity' Value='0.65'/></Trigger>
              <Trigger Property='IsEnabled' Value='False'><Setter TargetName='frame' Property='Opacity' Value='0.60'/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>");
        private static readonly ControlTemplate CaptionButton=(ControlTemplate)XamlReader.Parse(@"
          <ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
              xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Button}'>
            <Border x:Name='frame' Background='Transparent' CornerRadius='4'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='frame' Property='Background'
                Value='{Binding Tag, RelativeSource={RelativeSource TemplatedParent}}'/></Trigger>
              <Trigger Property='IsPressed' Value='True'><Setter TargetName='frame' Property='Opacity' Value='.67'/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>");
        private static readonly Geometry MaximizeGlyph=Geometry.Parse("M5,5 H19 V19 H5 Z"),
            RestoreGlyph=Geometry.Parse("M4,9 H15 V20 H4 Z M8,9 V5 H19 V16 H15");
        private readonly DispatcherTimer timer=new DispatcherTimer(DispatcherPriority.Background);
        private readonly DispatcherTimer hotplug=new DispatcherTimer(DispatcherPriority.Background);
        private readonly DispatcherTimer connectionNoticeTimer=new DispatcherTimer(DispatcherPriority.Background);
        private readonly BatteryList list=new BatteryList(false);
        private readonly Grid root;
        private readonly GlassLayer glass;
        private readonly BatteryMark mark;
        private readonly Border hero,segmentFrame,controls;
        private readonly RoundedSurface scene;
        private readonly Border settingsShade;
        private readonly ReadingHistory history;
        private readonly ConnectionNotices connections=new ConnectionNotices();
        private readonly Preferences preferences;
        private readonly SavedProfiles savedProfiles;
        private readonly TextBlock title,count,lastUpdate,intervalLabel,brandTitle;
        private readonly Button refresh,settings,minimize,zoom,closeWindow,github,manageDevices;
        private readonly DashboardDevicePicker devicePicker;
        private readonly RadioButton[] presets=new RadioButton[Intervals.Length];
        private List<Device> devices=new List<Device>();
        private readonly string settingsPath;
        private NotificationIcon tray;
        private TrayOptions trayMenu;
        private SettingsWindow settingsWindow;
        private System.Drawing.Icon trayIcon;
        private TrayFlyout flyout;
        private ConnectionPopup connectionPopup;
        private HwndSource hwnd;
        private int seconds;
        private bool updating,installing,closed,allowExit;
        private DateTime suppressSingleUntil;
        private string trayReadingKey, scanStatus="";

        internal Dashboard(string path)
        {
            settingsPath=path;
            preferences=new Preferences(Path.ChangeExtension(path,"ui"));
            preferences.Use();
            savedProfiles=new SavedProfiles(Path.ChangeExtension(path,"profiles"));
            history=new ReadingHistory(Path.ChangeExtension(path,"readings"));
            seconds=RefreshSettings.Load(path);
            Title=WindowTitle;
            Width=669;Height=514;MinWidth=371;MinHeight=425;
            WindowStartupLocation=WindowStartupLocation.CenterScreen;
            Height=Math.Min(Height,SystemParameters.WorkArea.Height);
            FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI");
            WindowStyle=WindowStyle.None;
            AllowsTransparency=true;
            Background=Brushes.Transparent;
            Foreground=Ink;
            UseLayoutRounding=true;
            SnapsToDevicePixels=true;
            Icon=AppArt.Symbol();

            root=new Grid { Margin=new Thickness(19,10,19,15) };
            TextOptions.SetTextFormattingMode(root,TextFormattingMode.Display);
            root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
            root.RowDefinitions.Add(new RowDefinition {Height=new GridLength(1,GridUnitType.Star)});
            root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
            root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
            scene=new RoundedSurface(11);
            glass=new GlassLayer(scene);
            scene.Children.Add(glass);
            var body=new DockPanel();
            var chrome=new Grid {Height=33,Margin=new Thickness(16,0,0,0),Background=Brushes.Transparent};
            brandTitle=new TextBlock {Text=AppName,FontWeight=FontWeights.SemiBold,FontSize=12.5,
                HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center};
            chrome.Children.Add(brandTitle);
            chrome.MouseLeftButtonDown+=delegate(object sender,System.Windows.Input.MouseButtonEventArgs e)
            {
                if(e.ClickCount==2) {ToggleZoom();return;}
                if(WindowState==WindowState.Normal) try {DragMove();} catch(InvalidOperationException) {}
            };
            var windowActions=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
            minimize=ChromeButton("最小化","M4,12 H20",delegate {WindowState=WindowState.Minimized;});
            zoom=ChromeButton("最大化",MaximizeGlyph.ToString(),ToggleZoom);
            closeWindow=ChromeButton("关闭","M5,5 L19,19 M19,5 L5,19",Close);
            windowActions.Children.Add(minimize);
            windowActions.Children.Add(zoom);windowActions.Children.Add(closeWindow);
            chrome.Children.Add(windowActions);
            chrome.MouseRightButtonUp+=delegate(object sender,System.Windows.Input.MouseButtonEventArgs e)
            {SystemCommands.ShowSystemMenu(this,PointToScreen(e.GetPosition(this)));e.Handled=true;};
            PreviewKeyDown+=delegate(object sender,System.Windows.Input.KeyEventArgs e)
            {
                if(e.Key!=System.Windows.Input.Key.System||e.SystemKey!=System.Windows.Input.Key.Space) return;
                SystemCommands.ShowSystemMenu(this,PointToScreen(new Point(0,33)));
                e.Handled=true;
            };
            StateChanged+=delegate
            {
                bool maximized=WindowState==WindowState.Maximized;
                ((System.Windows.Shapes.Path)zoom.Content).Data=maximized?RestoreGlyph:MaximizeGlyph;
                zoom.ToolTip=maximized?"还原":"最大化";
                System.Windows.Automation.AutomationProperties.SetName(zoom,(string)zoom.ToolTip);
                scene.Radius=maximized?0:11;
            };
            DockPanel.SetDock(chrome,Dock.Top);
            body.Children.Add(chrome);body.Children.Add(root);
            scene.Children.Add(body);
            settingsShade=new Border {IsHitTestVisible=false,Opacity=0,Background=BrushOf("#54131926")};
            scene.Children.Add(settingsShade);
            Content=scene;

            var header=new Grid { Margin=new Thickness(15,16,15,16) };
            header.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(63)});
            header.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
            header.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
            header.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
            header.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
            mark=new BatteryMark {Width=52,Height=52,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center};
            header.Children.Add(mark);
            var heading=new StackPanel { VerticalAlignment=VerticalAlignment.Center };
            title=Text("设备电量",27,Ink,FontWeights.SemiBold);
            heading.Children.Add(title);
            count=Text("正在扫描…",12,Muted);
            count.Margin=new Thickness(0,3,0,0);
            heading.Children.Add(count);
            Place(header,heading,1);
            refresh=ButtonFor("刷新",BrushOf("#E2E2EF"),Ink,64);
            refresh.Height=35;
            refresh.Click+=async delegate { await Refresh(); };
            Place(header,refresh,2);
            manageDevices=ButtonFor("管理设备",Brushes.Transparent,Ink,78);
            manageDevices.Height=35;
            manageDevices.Margin=new Thickness(5,0,0,0);
            manageDevices.Click+=delegate {EditMainCards(!list.CardDrag.Enabled);};
            Place(header,manageDevices,3);
            settings=ButtonFor("设置",Brushes.Transparent,Ink,64);
            settings.Height=35;
            settings.Margin=new Thickness(6,0,0,0);
            settings.Click+=delegate {OpenSettings();};
            Place(header,settings,4);
            hero=new Border {Child=header,CornerRadius=new CornerRadius(23),Margin=new Thickness(3,0,3,12),BorderThickness=new Thickness(1)};
            Action fitHeader=delegate
            {
                bool shortWindow=ActualHeight<450,narrow=hero.ActualWidth<410;
                mark.Width=mark.Height=narrow||shortWindow?40:52;
                header.ColumnDefinitions[0].Width=new GridLength(narrow?48:63);
                title.FontSize=narrow||shortWindow?22:27;
                refresh.Width=settings.Width=narrow?44:64;
                manageDevices.Width=narrow?47:78;
                manageDevices.Height=refresh.Height=settings.Height=shortWindow?30:35;
                LabelManageButton(narrow);
                header.Margin=shortWindow?new Thickness(13,7,13,7):new Thickness(15,16,15,16);
                hero.Margin=new Thickness(3,0,3,shortWindow?7:12);
                count.Visibility=shortWindow?Visibility.Collapsed:Visibility.Visible;
                count.Margin=new Thickness(0,narrow?1:3,0,0);
            };
            hero.SizeChanged+=delegate {fitHeader();};
            root.Children.Add(hero);

            devicePicker=new DashboardDevicePicker(preferences.ShowsMain,PlaceMainDevice);
            list.CardDrag.Moved+=MoveMainCard;
            var deviceArea=new DockPanel();
            DockPanel.SetDock(devicePicker.Surface,Dock.Top);
            deviceArea.Children.Add(devicePicker.Surface);
            deviceArea.Children.Add(list.Surface);
            Grid.SetRow(deviceArea,1);
            root.Children.Add(deviceArea);

            var footer=new Grid { Margin=new Thickness(12,0,9,0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
            footer.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
            intervalLabel=Text("自动刷新",13,Ink,FontWeights.SemiBold);
            footer.Children.Add(intervalLabel);
            var segment=new UniformGrid { Rows=1, Columns=Intervals.Length,Width=194 };
            for(int i=0;i<presets.Length;i++)
            {
                int interval=Intervals[i];
                var option=new RadioButton { GroupName="RefreshInterval",Content=interval+" 秒",Height=30,
                    Template=PixelButton,FontSize=12,FontWeight=FontWeights.SemiBold,Foreground=PrimaryColor,
                    VerticalContentAlignment=VerticalAlignment.Center,HorizontalContentAlignment=HorizontalAlignment.Center,
                    FocusVisualStyle=PixelUi.KeyboardFocus,Margin=new Thickness(2),BorderThickness=new Thickness(0) };
                option.Checked+=delegate { SelectInterval(interval,true); };
                presets[i]=option;
                segment.Children.Add(option);
            }
            segmentFrame=new Border { Background=Preferences.Current.Tonal,CornerRadius=new CornerRadius(13),Padding=new Thickness(2),Child=segment };
            Place(footer,segmentFrame,1);
            controls=new Border {Child=footer,CornerRadius=new CornerRadius(16),Margin=new Thickness(4,10,4,0),Padding=new Thickness(0,4,0,4),
                Background=BrushOf("#E8FCFFFC"),BorderBrush=PixelUi.LightEdge,BorderThickness=new Thickness(1)};
            Grid.SetRow(controls,2);
            root.Children.Add(controls);

            var bottom=new Grid { Margin=new Thickness(8,7,5,0) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            lastUpdate=Text("",11.75,Muted);
            lastUpdate.TextTrimming=TextTrimming.CharacterEllipsis;
            bottom.Children.Add(lastUpdate);
            github=ButtonFor("",Brushes.Transparent,Muted,68);
            github.Height=24;
            github.FontWeight=FontWeights.Normal;
            github.FontSize=11.75;
            github.Cursor=System.Windows.Input.Cursors.Hand;
            var githubLabel=new StackPanel {Orientation=Orientation.Horizontal};
            githubLabel.Children.Add(new TextBlock {Text="GitHub",VerticalAlignment=VerticalAlignment.Center});
            var externalArrow=DeviceArt.Stroke("M5,19 L19,5 M11,5 H19 V13",Muted,9,1.15);
            externalArrow.Margin=new Thickness(5,0,0,0);
            externalArrow.SetBinding(System.Windows.Shapes.Path.StrokeProperty,
                new System.Windows.Data.Binding("Foreground") {Source=github});
            githubLabel.Children.Add(externalArrow);
            github.Content=githubLabel;
            github.ToolTip="在浏览器中打开 PeriLink 的 GitHub 仓库";
            System.Windows.Automation.AutomationProperties.SetName(github,"GitHub 仓库");
            github.Click+=delegate
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(RepositoryUrl) {UseShellExecute=true});
                }
                catch(System.ComponentModel.Win32Exception)
                {
                    MessageBox.Show(this,RepositoryUrl,"PeriLink · GitHub",MessageBoxButton.OK,MessageBoxImage.Information);
                }
            };
            Place(bottom,github,1);
            Grid.SetRow(bottom,3);
            root.Children.Add(bottom);
            SizeChanged+=delegate
            {
                bool shortWindow=ActualHeight<450;
                root.Margin=shortWindow?new Thickness(13,7,13,7):new Thickness(19,10,19,15);
                controls.Margin=new Thickness(4,shortWindow?5:10,4,0);
                controls.Padding=new Thickness(0,shortWindow?2:4,0,shortWindow?2:4);
                foreach(var preset in presets) preset.Height=shortWindow?23:30;
                lastUpdate.Visibility=shortWindow&&!connectionNoticeTimer.IsEnabled?Visibility.Collapsed:Visibility.Visible;
                github.Height=shortWindow?20:24;
                bottom.Margin=new Thickness(8,shortWindow?2:7,5,0);
                fitHeader();
            };

            timer.Tick+=async delegate { await Refresh(); };
            hotplug.Interval=TimeSpan.FromMilliseconds(1000);
            hotplug.Tick+=async delegate
            {
                if(updating) return;
                hotplug.Stop();
                await Refresh();
            };
            connectionNoticeTimer.Interval=TimeSpan.FromSeconds(5);
            connectionNoticeTimer.Tick+=delegate
            {
                connectionNoticeTimer.Stop();
                lastUpdate.Text=scanStatus;
                lastUpdate.ToolTip=null;
                lastUpdate.Foreground=Muted;
                lastUpdate.Visibility=ActualHeight<450?Visibility.Collapsed:Visibility.Visible;
            };
            ApplyPreferences();
            IsVisibleChanged+=delegate { AdjustInterval();if(!IsVisible) EditMainCards(false); };
            PreviewKeyDown+=delegate(object sender,System.Windows.Input.KeyEventArgs e)
            {
                if(e.Key!=System.Windows.Input.Key.Escape||!list.CardDrag.Enabled) return;
                EditMainCards(false);e.Handled=true;
            };
            Loaded+=delegate
            {
                if(tray!=null) return;
                Motion.Enter(root,6);
                StartFromTray();
            };
            Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e)
            { if(!allowExit && tray!=null) { e.Cancel=true;Park(); } };
            DpiChanged+=delegate {Dispatcher.BeginInvoke(new Action(delegate {FitDisplay();if(!closed) UpdateTray();}));};
            Closed+=OnClosed;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            hwnd=(HwndSource)PresentationSource.FromVisual(this);
            if(hwnd!=null) hwnd.AddHook(OnMessage);
            SystemEvents.UserPreferenceChanged+=OnDesktopPreferenceChanged;
            FitDisplay();
        }

        private void FitDisplay()
        {
            if(closed) return;
            var work=DisplayPlacement.WorkSize(this);
            MinWidth=Math.Min(371,work.Width);MinHeight=Math.Min(425,work.Height);
            MaxWidth=work.Width;MaxHeight=work.Height;
            if(Width>MaxWidth) Width=MaxWidth;
            if(Height>MaxHeight) Height=MaxHeight;
        }

        private IntPtr OnMessage(IntPtr window,int message,IntPtr w,IntPtr l,ref bool handled)
        {
            if(message==0x0084&&WindowState==WindowState.Normal&&scene.ActualWidth>0)
            {
                long screen=l.ToInt64();
                var p=scene.PointFromScreen(new Point(unchecked((short)screen),unchecked((short)(screen>>16))));
                if(scene.Clip.FillContains(p))
                {
                    const double edge=7;
                    bool left=p.X<edge,right=p.X>scene.ActualWidth-edge,top=p.Y<edge,bottom=p.Y>scene.ActualHeight-edge;
                    int hit=top?(left?13:right?14:12):bottom?(left?16:right?17:15):left?10:right?11:0;
                    if(hit!=0) {handled=true;return new IntPtr(hit);}
                }
            }
            if(message==0x001A&&!closed&&preferences.ThemeMode==0) OnDesktopPreferenceChanged(null,null);
            return IntPtr.Zero;
        }

        private void ToggleZoom()
        {WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;}

        private static Button ChromeButton(string label,string glyph,Action action,double width=45,double size=11)
        {
            var button=new Button {Template=CaptionButton,Width=width,ToolTip=label,FocusVisualStyle=PixelUi.KeyboardFocus,
                Content=DeviceArt.Stroke(glyph,Ink,size,1.35)};
            System.Windows.Automation.AutomationProperties.SetName(button,label);
            button.Click+=delegate {action();};
            return button;
        }

        private void OnDesktopPreferenceChanged(object sender,UserPreferenceChangedEventArgs e)
        {
            if(Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if(closed||preferences.ThemeMode!=0||Preferences.Current.IsDark==Preferences.SystemDark()) return;
                ApplyPreferences();
                if(settingsWindow!=null) settingsWindow.SyncLook();
            }));
        }

        private void OnClosed(object sender,EventArgs args)
        {
            closed=true;
            timer.Stop();
            hotplug.Stop();
            connectionNoticeTimer.Stop();
            if(connectionPopup!=null) connectionPopup.Close();
            SystemEvents.UserPreferenceChanged-=OnDesktopPreferenceChanged;
            if(hwnd!=null && !hwnd.IsDisposed) hwnd.RemoveHook(OnMessage);
            if(flyout!=null) flyout.Shutdown();
            if(tray!=null) tray.Dispose();
            if(trayMenu!=null) trayMenu.Close();
            if(trayIcon!=null) trayIcon.Dispose();
        }

        internal int IntervalSeconds { get { return seconds; } }
        internal TimeSpan TimerInterval { get { return timer.Interval; } }
        private int EffectiveSeconds { get { return preferences.Eco && !IsVisible ? Math.Max(60,seconds):seconds; } }

        internal void SelectInterval(int value,bool save)
        {
            if(!Intervals.Contains(value) || value==seconds) return;
            seconds=value;
            AdjustInterval();
            SyncPresets();
            RefreshFlyout();
            if(save) RefreshSettings.Save(settingsPath,value);
        }

        private void SyncPresets()
        {
            for(int i=0;i<presets.Length;i++)
            {
                bool active=Intervals[i]==seconds;
                presets[i].IsChecked=active;
                presets[i].Background=active ? Preferences.Current.Tonal : Brushes.Transparent;
                presets[i].BorderBrush=active ? Preferences.Current.Ink : Brushes.Transparent;
                presets[i].BorderThickness=active ? new Thickness(1) : new Thickness(0);
                presets[i].Foreground=PrimaryColor;
            }
        }

        private void AdjustInterval()
        {
            var delay=TimeSpan.FromSeconds(EffectiveSeconds);
            if(delay!=timer.Interval) timer.Interval=delay;
        }

        private void OpenSettings()
        {
            if(settingsWindow!=null) {settingsWindow.Activate();return;}
            settingsWindow=new SettingsWindow(this,preferences,ApplyPreferences,()=>devices,InstallUpdate);
            ShadeSettings(true);
            try {settingsWindow.ShowDialog();}
            finally {settingsWindow=null;ShadeSettings(false);}
        }

        private void ShadeSettings(bool open)
        {
            double from=settingsShade.Opacity, to=open?1:0;
            settingsShade.Opacity=to;
            settingsShade.BeginAnimation(OpacityProperty,Motion.Enabled?Motion.Tween(from,to,open?155:185):null);
        }

        private void ApplyPreferences()
        {
            preferences.Use();
            var theme=Preferences.Current;
            Icon=AppArt.Symbol();
            mark.ApplyLook();
            scene.Background=theme.Backdrop(preferences.Material);
            scene.Edge=theme.Line;
            Foreground=title.Foreground=intervalLabel.Foreground=brandTitle.Foreground=theme.Ink;
            count.Foreground=theme.Muted;
            lastUpdate.Foreground=connectionNoticeTimer.IsEnabled?theme.Ink:theme.Muted;
            hero.Background=controls.Background=preferences.Material==Look.GlassMaterial
                ?Look.Paint(Look.Alpha(theme.Card.Color,222)):theme.Card;
            hero.BorderBrush=controls.BorderBrush=theme.Line;
            refresh.Background=theme.Tonal;
            refresh.Foreground=settings.Foreground=theme.Ink;
            manageDevices.Foreground=theme.Ink;
            manageDevices.Background=list.CardDrag.Enabled?theme.Tonal:theme.Card;
            github.Foreground=theme.Muted;
            settings.Background=theme.Card;
            settings.BorderBrush=theme.Line;
            settings.BorderThickness=new Thickness(1);
            foreach(var button in new[]{minimize,zoom,closeWindow})
            {
                ((System.Windows.Shapes.Path)button.Content).Stroke=theme.Ink;
                button.Tag=button==closeWindow?BrushOf(theme.IsDark?"#89434D":"#F5CED4"):theme.Tonal;
            }
            segmentFrame.Background=theme.Surface;
            SyncPresets();
            AdjustInterval();
            devicePicker.ApplyLook();
            ShowMainCards(false);
            glass.Apply();
            if(flyout!=null) flyout.ApplyLook();
            if(trayMenu!=null) trayMenu.ApplyLook();
            RefreshFlyout();
            UpdateTray();
        }

        private void EditMainCards(bool edit)
        {
            list.CardDrag.Enable(edit);
            devicePicker.ShowEditor(edit);
            LabelManageButton(hero.ActualWidth<410);
            manageDevices.Background=edit?Preferences.Current.Tonal:Preferences.Current.Card;
        }

        private void LabelManageButton(bool narrow)
        {
            bool editing=list.CardDrag.Enabled;
            manageDevices.Content=editing?"完成":narrow?"管理":"管理设备";
            System.Windows.Automation.AutomationProperties.SetName(manageDevices,editing?"结束设备管理":"管理设备");
        }

        private void PlaceMainDevice(string id,bool visible,string neighbor,bool after)
        {
            bool change=preferences.SetMainVisible(id,visible);
            if(neighbor!=null&&preferences.MoveMain(devices,id,neighbor,after)) change=true;
            if(!change) return;
            preferences.Save();
            ShowMainCards(false);
        }

        private void MoveMainCard(string from,string target,bool after)
        {
            if(!preferences.MoveMain(devices,from,target,after)) return;
            preferences.Save();
            ShowMainCards(false);
        }

        private void ShowMainCards(bool animate)
        {
            var cards=preferences.MainCards(devices);
            if(devices.Count>0)
                count.Text=cards.Count==devices.Count?devices.Count+" 台电池设备":"显示 "+cards.Count+" / "+devices.Count+" 台设备";
            list.Update(cards,animate,devices.Count>0&&cards.Count==0);
            devicePicker.Update(cards,preferences.MainCards(devices,false));
        }

        private void RefreshFlyout()
        {
            if(flyout!=null) flyout.Update(preferences.Peek(devices),EffectiveSeconds,devices.Count);
        }

        private async Task Refresh()
        {
            if(updating || installing || closed) return;
            updating=true;
            refresh.IsEnabled=false;
            refresh.Content="读取中…";
            var scan=new ScanLog();
            try
            {
                var result=await Task.Run(()=>
                {
                    var found=Hardware.Scan(Input.Catalog(),true,scan,savedProfiles);
                    var readAt=DateTime.Now;
                    history.Attach(found,readAt);
                    return new {Devices=found,At=readAt};
                });
                if(closed) return;
                var order=devices.Select((d,i)=>new {d.Id,Index=i}).GroupBy(d=>d.Id)
                    .ToDictionary(g=>g.Key,g=>g.First().Index,StringComparer.Ordinal);
                devices=result.Devices.Where(Tracking.Show).GroupBy(d=>d.Id,StringComparer.Ordinal).Select(g=>g.First())
                    .OrderBy(d=>order.ContainsKey(d.Id) ? order[d.Id] : int.MaxValue)
                    .ThenBy(d=>d.Link=="2.4G" && d.Kind=="鼠标" ? 0 : d.Battery.HasValue ? 1 : 2).ToList();
                foreach(var device in devices.Where(d=>d.WiredId!=null)) preferences.MergeWiredCard(device);
                count.Text=devices.Count+" 台电池设备";
                SetScanStatus("扫描于 "+result.At.ToString("HH:mm:ss"));
                ShowMainCards(IsVisible);
                RefreshFlyout();
                UpdateTray();
                ShowConnectionNotices(connections.Update(devices));
            }
            catch(Exception)
            {
                if(!closed)
                {
                    count.Text=devices.Count==0 ? "设备读取失败" : count.Text;
                    SetScanStatus("读取失败，点击刷新重试");
                    ShowMainCards(false);
                }
            }
            finally
            {
                updating=false;
                if(!closed)
                {
                    refresh.Content="刷新";
                    refresh.IsEnabled=true;
                }
            }
        }

        private void SetScanStatus(string text)
        {
            scanStatus=text;
            if(!connectionNoticeTimer.IsEnabled) lastUpdate.Text=text;
        }

        private void ShowConnectionNotices(IList<string> notices)
        {
            if(notices.Count==0) return;
            string details=string.Join("\n",notices.Take(3))+(notices.Count>3?"\n另外 "+(notices.Count-3)+" 台设备":"");
            lastUpdate.Text=notices[0]+(notices.Count>1?" · 另有 "+(notices.Count-1)+" 台":"");
            lastUpdate.ToolTip=notices.Count>1?details:null;
            lastUpdate.Foreground=Ink;
            lastUpdate.Visibility=Visibility.Visible;
            connectionNoticeTimer.Stop();
            connectionNoticeTimer.Start();
            if(!IsActive || WindowState==WindowState.Minimized)
            {
                if(connectionPopup!=null) connectionPopup.Close();
                var popup=new ConnectionPopup(notices);
                connectionPopup=popup;
                popup.Closed+=delegate {if(connectionPopup==popup) connectionPopup=null;};
                popup.ShowAt(tray==null?DisplayPlacement.ScreenFor(this):Forms.Screen.FromRectangle(tray.Bounds()));
            }
        }

        private async Task InstallUpdate(AppUpdate update)
        {
            await AppUpdates.Stage(update);
            installing=true;
            try
            {
                while(updating && !closed) await Task.Delay(40);
                if(closed) return;
                AppUpdates.StartWorker(update,!IsVisible || WindowState==WindowState.Minimized);
                Shutdown();
            }
            finally { installing=false; }
        }

        internal void StartFromTray()
        {
            InitializeTray();
            timer.Start();
            // A login launch enters here before Application.Run starts the WPF dispatcher.
            Dispatcher.BeginInvoke(DispatcherPriority.Normal,new Action(async delegate {await Refresh();}));
        }

        private void InitializeTray()
        {
            if(tray!=null) return;
            trayIcon=AppArt.TrayIcon(null);
            trayMenu=new TrayOptions(Reopen,async delegate {await Refresh();},delegate {Reopen();OpenSettings();},Shutdown);
            tray=new NotificationIcon(trayIcon);
            tray.Clicked+=OnTraySingle;
            tray.RightClicked+=OnTrayRight;
            tray.DoubleClicked+=OnTrayDouble;
            tray.HoverOpened+=PreviewTray;
            tray.DevicesChanged+=delegate { if(!closed) {hotplug.Stop();hotplug.Start();} };
            if(flyout==null) flyout=new TrayFlyout(Refresh,Reopen,preferences);
            RefreshFlyout();
            UpdateTray();
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(delegate
            {if(!closed&&!flyout.IsVisible) flyout.Prepare();}));
        }

        internal void OnTraySingle()
        {
            if(closed||DateTime.UtcNow<suppressSingleUntil) return;
            if(trayMenu!=null) trayMenu.Hide();
            if(flyout!=null) flyout.TogglePin(tray.Bounds(),EffectiveSeconds);
        }

        internal void OnTrayRight()
        {
            if(closed||trayMenu==null) return;
            if(flyout!=null) flyout.Dismiss(true);
            if(trayMenu.IsVisible) trayMenu.Hide();
            else trayMenu.Open(TrayPoint());
        }

        internal void OnTrayDouble()
        {
            if(closed) return;
            suppressSingleUntil=DateTime.UtcNow.AddMilliseconds(Forms.SystemInformation.DoubleClickTime);
            Reopen();
        }

        private void Park()
        {
            InitializeTray();
            Hide();
        }

        internal void Reopen()
        {
            if(closed) return;
            if(trayMenu!=null) trayMenu.Hide();
            if(flyout!=null && flyout.IsVisible) flyout.Dismiss(true);
            if(settingsWindow!=null) {settingsWindow.Activate();return;}
            bool wasHidden=!IsVisible;
            Show();
            if(WindowState==WindowState.Minimized) WindowState=WindowState.Normal;
            if(wasHidden) Motion.Enter(root,5);
            Activate();
        }

        internal void Shutdown()
        {
            allowExit=true;
            Close();
        }

        private void UpdateTray()
        {
            if(tray==null) return;
            var connected=devices.Where(d=>d.Connected).ToList();
            var key=AppArt.TrayIconSize+"|"+Preferences.Current.Accent+"|"+
                string.Join("|",connected.Select(d=>d.Id+":"+d.Power+":"+d.RetainedBattery));
            var label=AppName+" · "+connected.Count+" 台已连接设备";
            if(key!=trayReadingKey)
            {
                var nextIcon=AppArt.TrayIcon(connected.Count==0?null:connected);
                tray.Update(nextIcon,label);
                trayIcon.Dispose();
                trayIcon=nextIcon;trayReadingKey=key;
            }
            else tray.Update(trayIcon,label);
        }

        private void PreviewTray(System.Drawing.Rectangle bounds)
        {
            if(closed||trayMenu.IsVisible||flyout==null) return;
            flyout.PreviewAt(bounds,EffectiveSeconds);
        }

        private System.Drawing.Point TrayPoint()
        {
            if(tray==null) return Forms.Cursor.Position;
            var bounds=tray.Bounds();
            return new System.Drawing.Point(bounds.Left+bounds.Width/2,bounds.Top+bounds.Height/2);
        }

        internal static Viewbox IconFor(string type,double size,Brush stroke)
        {
            string path;
            switch(type)
            {
                case "鼠标": path="M12,3 C7,3 6,6 6,9 V15 C6,19 8,21 12,21 C16,21 18,19 18,15 V9 C18,6 17,3 12,3 Z M12,6 V10";break;
                case "键盘": path="M4,6 H20 Q22,6 22,8 V16 Q22,18 20,18 H4 Q2,18 2,16 V8 Q2,6 4,6 Z M6,10 H7 M11,10 H12 M16,10 H17 M6,14 H8 M11,14 H18";break;
                case "音频": path="M4,14 V12 C4,7 7,4 12,4 C17,4 20,7 20,12 V14 M4,13 H8 V20 H5 Q3,20 3,18 V15 Q3,13 4,13 M20,13 H16 V20 H19 Q21,20 21,18 V15 Q21,13 20,13";break;
                case "手柄": path="M7,7 Q4,7 3,11 L2,16 Q2,20 5,19 L9,16 H15 L19,19 Q22,20 22,16 L21,11 Q20,7 17,7 Z M6,12 H10 M8,10 V14 M15,12 H15.1 M18,10 H18.1";break;
                case "电池": path="M3,7 H19 V10 H22 V14 H19 V17 H3 Z M7,12 H10 L12,9 L14,15 L16,12 H17";break;
                default: path="M4,12 L12,4 L20,12 L12,20 Z";break;
            }
            var picture=new System.Windows.Shapes.Path { Data=Geometry.Parse(path),Stroke=stroke,StrokeThickness=1.6,
                StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round };
            var pad=new Canvas {Width=24,Height=24};
            pad.Children.Add(picture);
            return new Viewbox { Width=size,Height=size,Child=pad,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center };
        }

        internal static Button ButtonFor(string label,Brush background,Brush foreground,double width)
        {
            return new Button { Content=label,Width=width,Template=PixelButton,Background=background,Foreground=foreground,
                FontWeight=FontWeights.SemiBold,FontSize=12.5,FocusVisualStyle=PixelUi.KeyboardFocus,BorderThickness=new Thickness(0) };
        }

        private static TextBlock Text(string value,double size,Brush color,FontWeight? weight=null)
        {
            return new TextBlock { Text=value,FontSize=size,Foreground=color,FontWeight=weight??FontWeights.Normal,
                VerticalAlignment=VerticalAlignment.Center };
        }

        private static void Place(Grid grid,UIElement child,int column)
        { Grid.SetColumn(child,column);grid.Children.Add(child); }

        internal static Brush BrushOf(string hex)
        {
            var b=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }
    }

    internal static class RefreshSettings
    {
        internal static string PortablePath
        { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"LinkScope.refresh"); } }
        internal static int Load(string path)
        {
            try
            {
                int value;
                if(File.Exists(path) && int.TryParse(File.ReadAllText(path),out value) && Dashboard.Intervals.Contains(value)) return value;
            }
            catch(IOException) { }
            catch(UnauthorizedAccessException) { }
            return 30;
        }
        internal static void Save(string path,int value)
        {
            try { File.WriteAllText(path,value.ToString(CultureInfo.InvariantCulture)); }
            catch(IOException) { }
            catch(UnauthorizedAccessException) { }
        }
    }
}
