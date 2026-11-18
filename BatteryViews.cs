using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace LinkScope
{
    internal static class Motion
    {
        internal const int TargetFps=60;
        internal static bool Enabled { get { return !Preferences.PowerSaver && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast; } }

        internal static T At60<T>(T effect) where T:Timeline
        {
            Timeline.SetDesiredFrameRate(effect,TargetFps);
            return effect;
        }

        internal static DoubleAnimation Tween(double from,double to,int ms)
        {
            return At60(new DoubleAnimation(from,to,new Duration(TimeSpan.FromMilliseconds(ms)))
            { EasingFunction=new CubicEase {EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop });
        }

        internal static void Enter(FrameworkElement element,int distance,bool quick=false)
        {
            if(!Enabled) return;
            var shift=new TranslateTransform();
            element.RenderTransform=shift;
            element.BeginAnimation(UIElement.OpacityProperty,Tween(quick?.65:0,1,quick?110:190));
            shift.BeginAnimation(TranslateTransform.YProperty,Tween(distance,0,quick?155:250));
        }

        internal static void Reveal(FrameworkElement element,int offset,int delay,int duration,double scale=1)
        {
            element.Opacity=1;
            if(!Enabled) return;
            var offsetTransform=new TranslateTransform();
            element.RenderTransform=offsetTransform;
            if(scale<1)
            {
                var zoom=new ScaleTransform();
                var transforms=new TransformGroup();
                transforms.Children.Add(zoom);
                transforms.Children.Add(offsetTransform);
                element.RenderTransform=transforms;
                element.RenderTransformOrigin=new Point(.5,.5);
                zoom.BeginAnimation(ScaleTransform.ScaleXProperty,RevealTween(scale,1,delay,duration));
                zoom.BeginAnimation(ScaleTransform.ScaleYProperty,RevealTween(scale,1,delay,duration));
            }
            element.BeginAnimation(UIElement.OpacityProperty,RevealTween(0,1,delay,duration));
            offsetTransform.BeginAnimation(TranslateTransform.YProperty,RevealTween(offset,0,delay,duration));
        }

        private static DoubleAnimationUsingKeyFrames RevealTween(double from,double to,int delay,int duration)
        {
            var motion=At60(new DoubleAnimationUsingKeyFrames {FillBehavior=FillBehavior.Stop,
                Duration=new Duration(TimeSpan.FromMilliseconds(delay+duration))});
            motion.KeyFrames.Add(new DiscreteDoubleKeyFrame(from,KeyTime.FromTimeSpan(TimeSpan.Zero)));
            if(delay>0) motion.KeyFrames.Add(new DiscreteDoubleKeyFrame(from,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay))));
            motion.KeyFrames.Add(new EasingDoubleKeyFrame(to,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay+duration)),
                new CubicEase {EasingMode=EasingMode.EaseOut}));
            return motion;
        }
    }

    internal sealed class BatteryList
    {
        private static readonly ControlTemplate CarouselBar=(ControlTemplate)XamlReader.Parse(@"
          <ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
              xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ScrollBar}'>
            <Grid Background='Transparent'>
              <Grid.Resources>
                <Style TargetType='{x:Type RepeatButton}'>
                  <Setter Property='Template'>
                    <Setter.Value><ControlTemplate TargetType='{x:Type RepeatButton}'>
                      <Border Background='Transparent'/>
                    </ControlTemplate></Setter.Value>
                  </Setter>
                </Style>
              </Grid.Resources>
              <Border Height='4' Background='{TemplateBinding Background}' CornerRadius='2' VerticalAlignment='Center'/>
              <Track x:Name='PART_Track' Orientation='Horizontal'>
                <Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageLeftCommand' Focusable='False'/></Track.DecreaseRepeatButton>
                <Track.Thumb>
                  <Thumb Background='{TemplateBinding Foreground}' Focusable='False'>
                    <Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'>
                      <Grid Background='Transparent'>
                        <Border x:Name='pill' Height='6' Background='{TemplateBinding Background}' CornerRadius='3' VerticalAlignment='Center'/>
                      </Grid>
                      <ControlTemplate.Triggers>
                        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='pill' Property='Height' Value='8'/></Trigger>
                        <Trigger Property='IsDragging' Value='True'><Setter TargetName='pill' Property='Height' Value='8'/></Trigger>
                      </ControlTemplate.Triggers>
                    </ControlTemplate></Thumb.Template>
                  </Thumb>
                </Track.Thumb>
                <Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageRightCommand' Focusable='False'/></Track.IncreaseRepeatButton>
              </Track>
            </Grid>
          </ControlTemplate>");
        private readonly Panel stack;
        private readonly bool compact;
        private readonly TextBlock empty;
        private readonly Dictionary<string,BatteryTile> tiles=new Dictionary<string,BatteryTile>(StringComparer.Ordinal);
        internal readonly SmoothScroll View;
        internal readonly FrameworkElement Surface;
        private readonly ScrollBar carousel;
        private readonly TextBlock range;
        private readonly DockPanel navigation;
        private bool syncingScroll;
        private double cardWidth=255;
        private int compactColumns=2;
        internal bool GridLayout {get;private set;}
        internal int Count { get { return tiles.Count; } }
        private int itemCount;
        internal BatteryList(bool tray)
        {
            compact=tray;
            stack=tray?(Panel)new UniformGrid {Columns=2}:new WrapPanel {Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Top};
            empty=new TextBlock {Text="暂无电池设备",FontSize=13,Foreground=Preferences.Current.Muted,
                Margin=new Thickness(15,21,15,21),HorizontalAlignment=HorizontalAlignment.Center,Visibility=Visibility.Collapsed};
            var content=new DockPanel();
            DockPanel.SetDock(empty,Dock.Top);
            content.Children.Add(empty);content.Children.Add(stack);
            View=new SmoothScroll(!tray) {Content=content,
                VerticalScrollBarVisibility=tray?ScrollBarVisibility.Auto:ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility=tray?ScrollBarVisibility.Disabled:ScrollBarVisibility.Hidden,
                PanningMode=tray?PanningMode.VerticalOnly:PanningMode.HorizontalOnly};
            if(tray) {Surface=View;return;}
            var host=new DockPanel {Margin=new Thickness(0,0,0,1)};
            navigation=new DockPanel {LastChildFill=true,Margin=new Thickness(8,2,8,6)};
            range=new TextBlock {FontSize=11.5,MinWidth=47,Foreground=Preferences.Current.Muted,VerticalAlignment=VerticalAlignment.Center};
            DockPanel.SetDock(range,Dock.Left);navigation.Children.Add(range);
            carousel=new ScrollBar {Template=CarouselBar,Orientation=Orientation.Horizontal,Height=22,Focusable=false};
            AutomationProperties.SetName(carousel,"横向浏览设备卡片");
            carousel.ValueChanged+=delegate(object sender,RoutedPropertyChangedEventArgs<double> e)
            {if(!syncingScroll&&!GridLayout) View.JumpTo(e.NewValue);};
            navigation.Children.Add(carousel);
            DockPanel.SetDock(navigation,Dock.Bottom);host.Children.Add(navigation);
            host.Children.Add(View);
            Surface=host;
            View.SizeChanged+=delegate {SizeCards();};
            View.ScrollChanged+=delegate(object sender,ScrollChangedEventArgs e)
            {
                if(GridLayout&&Math.Abs(e.ViewportWidthChange)>.1) SizeCards();
                ShowRange();
            };
            View.PreviewKeyDown+=delegate(object sender,System.Windows.Input.KeyEventArgs e)
            {
                if(GridLayout||e.Key!=System.Windows.Input.Key.Left&&e.Key!=System.Windows.Input.Key.Right) return;
                View.ShiftBy((e.Key==System.Windows.Input.Key.Right?1:-1)*(cardWidth+8));e.Handled=true;
            };
            ShowRange();
        }
        private void SizeCards()
        {
            if(compact||View.ActualWidth<20) return;
            bool grid=itemCount>1&&(GridLayout?View.ActualHeight>=330:View.ActualHeight>=355);
            if(grid!=GridLayout)
            {
                GridLayout=grid;
                View.SetHorizontal(!grid);
            }
            double width=View.ActualWidth;
            if(grid) width=Math.Max(20,View.ViewportWidth>20?Math.Min(width,View.ViewportWidth)-2:width-19);
            if(itemCount<=1) cardWidth=Math.Min(370,width-8);
            else
            {
                int visible=Math.Max(1,Math.Min(itemCount,(int)(width/244)));
                double peek=grid||itemCount<=visible?0:50;
                cardWidth=Math.Max(Math.Min(190,width-8),Math.Floor((width-peek)/visible)-8);
            }
            ((WrapPanel)stack).ItemWidth=cardWidth+8;
            foreach(var tile in tiles.Values) tile.Width=cardWidth;
            ShowRange();
        }

        internal void SetCompactWidth(double available)
        {
            if(!compact) return;
            compactColumns=available<354?1:2;
            ((UniformGrid)stack).Columns=itemCount==1?1:compactColumns;
        }
        private void ShowRange()
        {
            bool scroll=!GridLayout&&View.ScrollableWidth>2;
            navigation.Visibility=scroll?Visibility.Visible:Visibility.Collapsed;
            syncingScroll=true;
            carousel.Maximum=View.ScrollableWidth;
            carousel.ViewportSize=View.ViewportWidth;
            carousel.LargeChange=Math.Max(1,View.ViewportWidth*.85);
            carousel.SmallChange=Math.Max(1,cardWidth/4);
            carousel.Value=View.HorizontalOffset;
            syncingScroll=false;
            int index=Math.Min(itemCount,1+(int)Math.Round(View.HorizontalOffset/(cardWidth+8)));
            range.Text=scroll?index+" / "+itemCount:"";
        }
        internal void Update(IList<Device> devices,bool animate)
        {
            var theme=Preferences.Current;
            empty.Foreground=theme.Muted;
            View.ApplyLook();
            if(!compact)
            {
                range.Foreground=theme.Muted;
                carousel.Background=theme.Line;
                carousel.Foreground=Look.Paint(Look.Blend(theme.IconInk.Color,theme.Surface.Color,.35));
            }
            var wanted=new HashSet<string>(devices.Select(d=>d.Id),StringComparer.Ordinal);
            foreach(string stale in tiles.Keys.Where(id=>!wanted.Contains(id)).ToArray())
            {
                stack.Children.Remove(tiles[stale]);
                tiles.Remove(stale);
            }
            itemCount=devices.Count;
            if(compact) ((UniformGrid)stack).Columns=itemCount==1?1:compactColumns;
            for(int index=0;index<devices.Count;index++)
            {
                Device d=devices[index];
                BatteryTile tile;
                if(!tiles.TryGetValue(d.Id,out tile))
                {
                    tile=new BatteryTile(compact);
                    tiles.Add(d.Id,tile);
                    stack.Children.Insert(index,tile);
                    tile.Update(d,animate);
                    if(animate) Motion.Enter(tile,5);
                    continue;
                }
                if(stack.Children.IndexOf(tile)!=index)
                {
                    stack.Children.Remove(tile);stack.Children.Insert(index,tile);
                }
                tile.Update(d,animate);
            }
            empty.Visibility=devices.Count==0 ? Visibility.Visible : Visibility.Collapsed;
            if(!compact) SizeCards();
        }
    }

    internal sealed class BatteryTile : Border
    {
        private readonly TextBlock name,descriptor,time;
        private readonly PowerText power=new PowerText();
        private readonly Border badge,signalBadge;
        private readonly System.Windows.Shapes.Path bolt;
        private readonly PowerStage art=new PowerStage();
        private readonly bool compact;
        private int? previous;
        private string iconKind,brand,link;
        private Look oldLook;
        private bool oldGlass;

        internal BatteryTile(bool tray)
        {
            compact=tray;
            BorderThickness=new Thickness(1);
            CornerRadius=new CornerRadius(tray?17:23);
            Padding=tray?new Thickness(11,10,9,9):new Thickness(16,14,13,13);
            Margin=new Thickness(4);
            MinHeight=tray?130:156;

            name=Text("",tray?13:16,Preferences.Current.Ink,FontWeights.SemiBold);
            name.Margin=new Thickness(0,4,0,0);
            if(!tray) {name.TextWrapping=TextWrapping.Wrap;name.LineHeight=19;name.MaxHeight=38;}
            descriptor=Text("",tray?10.8:11.5,Preferences.Current.Muted,FontWeights.Medium);
            descriptor.Margin=new Thickness(0,2,0,0);
            time=Text("",10.25,Preferences.Current.Muted);
            time.Margin=new Thickness(0,2,0,0);
            name.TextTrimming=descriptor.TextTrimming=time.TextTrimming=TextTrimming.CharacterEllipsis;
            power.SetSize(tray?3:4);

            var body=new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
            body.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(tray?70:103)});
            var words=new StackPanel {VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,4,0)};
            badge=new Border {Height=19,Padding=new Thickness(0,0,1,0),HorizontalAlignment=HorizontalAlignment.Left};
            words.Children.Add(badge);
            words.Children.Add(name);
            words.Children.Add(descriptor);
            var meter=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,2,0,0)};
            meter.Children.Add(power);
            bolt=new System.Windows.Shapes.Path {Width=tray?10:13,Height=tray?15:18,Stretch=Stretch.Uniform,Fill=Preferences.Current.IconInk,
                Data=Geometry.Parse("M9,0 L1,10 H6 L4,19 L14,7 H8 Z"),
                Margin=new Thickness(5,1,0,0),VerticalAlignment=VerticalAlignment.Center,Visibility=Visibility.Collapsed};
            meter.Children.Add(bolt);
            words.Children.Add(meter);words.Children.Add(time);
            body.Children.Add(words);
            var stage=new Grid {VerticalAlignment=VerticalAlignment.Center};
            art.Width=tray?66:94;
            art.Height=tray?76:110;
            art.HorizontalAlignment=HorizontalAlignment.Right;
            stage.Children.Add(art);
            signalBadge=new Border {Width=tray?25:30,Height=tray?25:30,CornerRadius=new CornerRadius(tray?12.5:15),
                Background=Dashboard.BrushOf("#F5F9F5"),BorderBrush=PixelUi.LightEdge,BorderThickness=new Thickness(.75),
                HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,
                Margin=new Thickness(0,-5,-5,0)};
            stage.Children.Add(signalBadge);
            Grid.SetColumn(stage,1);
            body.Children.Add(stage);
            Child=body;
            if(!tray) SizeChanged+=delegate
            {
                bool tight=ActualWidth<250;
                body.ColumnDefinitions[1].Width=new GridLength(tight?86:103);
                art.Width=tight?80:94;
                art.Height=tight?94:110;
                power.SetSize(tight?3:4);
            };
            var raise=new TranslateTransform();
            body.RenderTransform=raise;
            MouseEnter+=delegate
            {
                art.SetHovered(true);
                if(Motion.Enabled&&Preferences.GlassOn) raise.BeginAnimation(TranslateTransform.YProperty,
                    Motion.At60(new DoubleAnimation(raise.Y,-2,new Duration(TimeSpan.FromMilliseconds(155))){EasingFunction=new CubicEase()}));
            };
            MouseLeave+=delegate
            {
                art.SetHovered(false);
                raise.BeginAnimation(TranslateTransform.YProperty,Motion.Enabled
                    ? Motion.At60(new DoubleAnimation(raise.Y,0,new Duration(TimeSpan.FromMilliseconds(200))){EasingFunction=new CubicEase()}):null);
            };
        }

        internal void Update(Device d,bool animate)
        {
            string currentBrand=BrandBadge.Identify(d);
            string category=DeviceArt.Category(d);
            name.Text=BrandBadge.ModelName(d,currentBrand);
            bool charging=d.Connected&&!d.RetainedBattery&&Hardware.Has(d.Source,"充电中");
            string state=!d.Connected?"离线":d.RetainedBattery?"保留读数":charging?"充电中":
                !Tracking.HasBattery(d)?"待上报":"";
            descriptor.Text=category+" · "+(state==""?d.Link:state);
            time.Text=Tracking.ReadTime(d,DateTime.Now);
            time.Visibility=time.Text.Length==0 ? Visibility.Collapsed : Visibility.Visible;
            bool glassMaterial=Preferences.CurrentMaterial==Look.GlassMaterial;
            if(oldLook!=Preferences.Current || oldGlass!=glassMaterial || iconKind!=category || brand!=currentBrand || link!=d.Link)
            {
                oldLook=Preferences.Current;
                brand=currentBrand;
                oldGlass=glassMaterial;
                Background=oldGlass&&!SystemParameters.HighContrast?Look.Paint(Look.Alpha(oldLook.Card.Color,232)):oldLook.Card;
                BorderBrush=oldLook.Line;
                name.Foreground=oldLook.Ink;
                descriptor.Foreground=time.Foreground=oldLook.Muted;
                iconKind=category;
                link=d.Link;
                badge.Child=brand==null?Text(category,10.5,oldLook.Muted,FontWeights.SemiBold):BrandBadge.Create(brand,category,oldLook.Ink);
                AutomationProperties.SetName(badge,brand??category);
                signalBadge.Background=oldLook.Card;signalBadge.BorderBrush=oldLook.Line;
                signalBadge.Child=DeviceArt.Signal(link,oldLook.IconInk,compact?15:19);
                signalBadge.Visibility=signalBadge.Child==null?Visibility.Collapsed:Visibility.Visible;
                signalBadge.ToolTip=link=="2.4G"?"2.4G 无线接收器":link;
                bolt.Fill=oldLook.IconInk;
            }
            bool stale=d.RetainedBattery||!d.Connected;
            power.Foreground=stale||!d.Battery.HasValue?oldLook.Muted:oldLook.Power(d.Battery.Value);
            bool changed=previous!=d.Battery;
            bool transition=animate&&Motion.Enabled&&changed;
            power.Show(d.Battery,d.Power,transition&&previous.HasValue);
            bolt.Visibility=charging?Visibility.Visible:Visibility.Collapsed;
            art.Show(category,d.Battery,charging,transition);
            previous=d.Battery;
            ToolTip=(brand==null?d.Name:brand+" "+name.Text)+" · "+d.Power+" · "+(d.Source??"等待设备上报电量");
            AutomationProperties.SetName(this,d.Name+"，"+category+"，"+d.Link+"，"+state+"，"+d.Power+"，"+time.Text);
        }

        private static TextBlock Text(string text,double size,Brush color,FontWeight? weight=null)
        { return new TextBlock {Text=text,FontSize=size,Foreground=color,FontWeight=weight??FontWeights.Normal}; }
    }

    internal sealed class TrayFlyout : Window
    {
        private const double PreferredWidth=432;
        private const double Rounding=24;
        private readonly BatteryList list=new BatteryList(true);
        private readonly RoundedSurface surface;
        private readonly Grid layersForMotion;
        private readonly TextBlock caption;
        private readonly Preferences preferences;
        private readonly GlassLayer glass;
        private readonly BatteryMark batteryMark;
        private readonly Button reload;
        private readonly TextBlock titleLabel;
        private readonly Button open;
        private readonly DispatcherTimer pointerWatch;
        private readonly Border grip;
        private readonly AmbientBackdrop backdrop;
        private bool closing;
        private bool dragging;
        private bool dragMoved;
        private System.Drawing.Point dragStart;
        private System.Drawing.Point dragCursor;
        private Vector grabOffset;
        private bool suppressHover;
        private int pointerAway;
        private int totalDevices;
        private System.Drawing.Rectangle anchor;
        internal bool IsPinned {get;private set;}

        internal TrayFlyout(Func<System.Threading.Tasks.Task> refresh,Action main,Preferences preferences)
        {
            this.preferences=preferences;
            Title=Dashboard.AppName+" · 托盘速览";
            Width=PreferredWidth;
            SizeToContent=SizeToContent.Height;
            ResizeMode=ResizeMode.NoResize;
            WindowStyle=WindowStyle.None;
            AllowsTransparency=true;
            Background=Brushes.Transparent;
            ShowActivated=false;
            Topmost=true;
            ShowInTaskbar=false;
            FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI");
            UseLayoutRounding=true;
            SnapsToDevicePixels=true;
            var root=new Grid {Margin=new Thickness(20,18,19,18)};
            layersForMotion=root;
            TextOptions.SetTextFormattingMode(root,TextFormattingMode.Display);
            root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
            root.RowDefinitions.Add(new RowDefinition {Height=new GridLength(1,GridUnitType.Star)});
            root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
            var head=new Grid {Margin=new Thickness(4,1,3,8)};
            head.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
            head.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
            var trayHeading=new StackPanel {Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
            batteryMark=new BatteryMark {Width=29,Height=29,Margin=new Thickness(0,0,8,0)};
            trayHeading.Children.Add(batteryMark);
            titleLabel=new TextBlock {Text="设备电量",FontSize=17,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center};
            trayHeading.Children.Add(titleLabel);
            grip=new Border {Background=Brushes.Transparent,Cursor=System.Windows.Input.Cursors.SizeAll,Child=trayHeading,
                Padding=new Thickness(2,1,0,1),ToolTip="拖住标题可移动，松开后记住位置"};
            AutomationProperties.SetName(grip,"拖动电量面板");
            grip.PreviewMouseLeftButtonDown+=BeginDrag;
            grip.PreviewMouseMove+=DragPointer;
            grip.PreviewMouseLeftButtonUp+=EndDrag;
            grip.LostMouseCapture+=delegate
            {
                if(dragging) ApplyDragPosition(Forms.Cursor.Position);
                FinishDrag();
            };
            head.Children.Add(grip);
            reload=Dashboard.ButtonFor("刷新",Brushes.Transparent,Preferences.Current.Ink,58);
            reload.Height=29;
            reload.Click+=async delegate
            {
                reload.IsEnabled=false;reload.Content="读取中";
                try { await refresh(); }
                finally { reload.IsEnabled=true;reload.Content="刷新"; }
            };
            Grid.SetColumn(reload,1);head.Children.Add(reload);
            root.Children.Add(head);
            list.View.MaxHeight=383;
            Grid.SetRow(list.View,1);root.Children.Add(list.View);

            var footer=new Grid {Margin=new Thickness(5,8,3,0)};
            footer.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
            footer.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
            caption=new TextBlock {FontSize=11,VerticalAlignment=VerticalAlignment.Center};
            footer.Children.Add(caption);
            open=Dashboard.ButtonFor("打开主窗口",Brushes.Transparent,Preferences.Current.Ink,84);
            open.Height=27;
            open.Click+=delegate { Dismiss();main(); };
            Grid.SetColumn(open,1);footer.Children.Add(open);
            Grid.SetRow(footer,2);root.Children.Add(footer);
            surface=new RoundedSurface(Rounding);
            backdrop=new AmbientBackdrop();
            surface.Children.Add(backdrop);
            surface.Children.Add(root);
            glass=new GlassLayer(surface,true);
            surface.Children.Add(glass);
            Content=surface;
            pointerWatch=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(75)};
            pointerWatch.Tick+=delegate {CheckPointer();};
            ApplyLook();
            DpiChanged+=delegate
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if(!IsVisible) return;
                    if(!dragging) PlaceAtAnchor();
                }));
            };
            Deactivated+=delegate { if(IsVisible&&IsPinned&&!dragging&&!NearIcon(Forms.Cursor.Position)) Dismiss(); };
            PreviewKeyDown+=delegate(object sender,System.Windows.Input.KeyEventArgs e)
            {if(e.Key==System.Windows.Input.Key.Escape) {e.Handled=true;Dismiss(true);}};
            Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e)
            { if(!closing) { e.Cancel=true;Dismiss(); } };
        }

        internal int DeviceCount { get { return list.Count; } }

        internal void ApplyLook()
        {
            var p=Preferences.Current;
            Icon=AppArt.Symbol();
            batteryMark.ApplyLook();
            surface.Background=p.Backdrop(Preferences.CurrentMaterial);
            surface.Edge=p.Line;
            backdrop.Apply(p,Preferences.CurrentBackdrop);
            reload.Background=p.Tonal;
            reload.Foreground=open.Foreground=titleLabel.Foreground=p.Ink;
            caption.Foreground=p.Muted;
            glass.Apply();
        }

        internal void Update(IList<Device> devices,int seconds,int total=-1)
        {
            list.Update(devices,IsVisible);
            totalDevices=total;
            ShowInterval(seconds);
            if(IsVisible&&!dragging) PlaceAtAnchor();
        }

        private void ShowInterval(int seconds)
        {
            int count=list.Count;
            caption.Text=(totalDevices>count?count+" / "+totalDevices+" 台设备":count+" 台设备")+"  ·  "+seconds+" 秒刷新";
        }

        internal void Prepare()
        {
            new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
            surface.Measure(new Size(Width,528));
        }

        internal void PreviewAt(System.Drawing.Rectangle icon,int seconds)
        {
            if(suppressHover||IsVisible) return;
            OpenAt(icon,seconds,false);
        }

        internal void TogglePin(System.Drawing.Rectangle icon,int seconds)
        {
            if(IsVisible&&IsPinned) {Dismiss(true);return;}
            OpenAt(icon,seconds,true);
        }

        private void OpenAt(System.Drawing.Rectangle icon,int seconds,bool pin)
        {
            anchor=icon;pointerAway=0;suppressHover=false;
            IsPinned=pin;
            ShowInterval(seconds);
            PlaceAtAnchor();
            if(!IsVisible) {Show();PlaceAtAnchor();Motion.Enter(layersForMotion,3,true);}
            if(pin) {pointerWatch.Stop();Activate();}
            else pointerWatch.Start();
        }

        private void PlaceAtAnchor()
        {
            var at=new System.Drawing.Point(anchor.Left+anchor.Width/2,anchor.Top+anchor.Height/2);
            double oldScale=DisplayPlacement.Scale(this);
            bool legacy=preferences.TrayMonitor==null&&preferences.Left.HasValue&&preferences.Top.HasValue;
            var legacyPoint=legacy?new System.Drawing.Point((int)Math.Round(preferences.Left.Value*oldScale),
                (int)Math.Round(preferences.Top.Value*oldScale)):at;
            var screen=preferences.TrayMonitor==null?null:Forms.Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==preferences.TrayMonitor);
            bool remembered=screen!=null&&preferences.TrayX.HasValue&&preferences.TrayY.HasValue;
            if(!remembered) screen=Forms.Screen.FromPoint(legacy?legacyPoint:at);
            new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
            DisplayPlacement.SeatOnMonitor(this,screen);
            double scale=DisplayPlacement.Scale(this);
            var area=screen.WorkingArea;
            Width=Math.Min(PreferredWidth,Math.Max(40,area.Width/scale-16));
            list.SetCompactWidth(Width-55);
            MaxHeight=Math.Min(646,Math.Max(92,area.Height/scale-16));
            list.View.MaxHeight=Math.Min(553,Math.Max(80,MaxHeight-92));
            surface.Measure(new Size(Width,MaxHeight));
            int w=(int)Math.Ceiling(Width*scale),h=(int)Math.Ceiling(Math.Min(MaxHeight,surface.DesiredSize.Height)*scale);
            int margin=(int)Math.Round(8*scale);
            int travelX=DisplayPlacement.Travel(area.Width,w,margin),travelY=DisplayPlacement.Travel(area.Height,h,margin);
            bool above=at.Y>=area.Top+area.Height/2;
            int desiredLeft=remembered?area.Left+margin+(int)Math.Round(preferences.TrayX.Value*travelX):
                legacy?legacyPoint.X:at.X-w+(int)(35*scale);
            int desiredTop=remembered?area.Top+margin+(int)Math.Round(preferences.TrayY.Value*travelY):
                legacy?legacyPoint.Y:above?area.Bottom-h-margin:area.Top+margin;
            DisplayPlacement.Move(this,new System.Drawing.Point(
                Math.Max(area.Left+margin,Math.Min(desiredLeft,area.Left+margin+travelX)),
                Math.Max(area.Top+margin,Math.Min(desiredTop,area.Top+margin+travelY))));
        }

        private void BeginDrag(object sender,System.Windows.Input.MouseButtonEventArgs e)
        {
            if(dragging||!IsVisible) return;
            var bounds=DisplayPlacement.Bounds(this);
            if(bounds.IsEmpty) return;
            double scale=DisplayPlacement.Scale(this);
            var hit=PointToScreen(e.GetPosition(this));
            dragStart=bounds.Location;dragCursor=new System.Drawing.Point((int)Math.Round(hit.X),(int)Math.Round(hit.Y));
            grabOffset=new Vector((dragCursor.X-bounds.Left)/scale,(dragCursor.Y-bounds.Top)/scale);
            dragMoved=false;
            dragging=true;
            Activate();
            if(!grip.CaptureMouse()) {dragging=false;return;}
            IsPinned=true;pointerWatch.Stop();
            e.Handled=true;
        }

        private void DragPointer(object sender,System.Windows.Input.MouseEventArgs e)
        {
            if(!dragging) return;
            ApplyDragPosition(Forms.Cursor.Position);
            if(e.LeftButton!=System.Windows.Input.MouseButtonState.Pressed) {FinishDrag();return;}
            e.Handled=true;
        }

        private void ApplyDragPosition(System.Drawing.Point cursor)
        {
            double scale=DisplayPlacement.Scale(this);
            if(!dragMoved&&Math.Abs(cursor.X-dragCursor.X)+Math.Abs(cursor.Y-dragCursor.Y)<2*scale) return;
            dragMoved=true;
            DisplayPlacement.Move(this,new System.Drawing.Point((int)Math.Round(cursor.X-grabOffset.X*scale),
                (int)Math.Round(cursor.Y-grabOffset.Y*scale)));
        }

        private void EndDrag(object sender,System.Windows.Input.MouseButtonEventArgs e)
        {
            if(!dragging) return;
            ApplyDragPosition(Forms.Cursor.Position);
            FinishDrag();
            e.Handled=true;
        }

        private void FinishDrag()
        {
            if(!dragging) return;
            dragging=false;
            if(grip.IsMouseCaptured) grip.ReleaseMouseCapture();
            if(!dragMoved) return;
            var bounds=DisplayPlacement.Bounds(this);
            if(bounds.IsEmpty||bounds.Location==dragStart) return;
            var screen=DisplayPlacement.ScreenFor(this);
            var area=screen.WorkingArea;
            int margin=(int)Math.Round(8*DisplayPlacement.Scale(this));
            preferences.TrayMonitor=screen.DeviceName;
            preferences.TrayX=DisplayPlacement.Fraction(bounds.Left-area.Left-margin,DisplayPlacement.Travel(area.Width,bounds.Width,margin));
            preferences.TrayY=DisplayPlacement.Fraction(bounds.Top-area.Top-margin,DisplayPlacement.Travel(area.Height,bounds.Height,margin));
            preferences.Left=preferences.Top=null;
            PlaceAtAnchor();
            preferences.Save();
        }

        private bool NearIcon(System.Drawing.Point point)
        {
            var pad=anchor;
            pad.Inflate(8,12);
            return pad.Contains(point);
        }

        private void CheckPointer()
        {
            var cursor=Forms.Cursor.Position;
            if(suppressHover)
            {
                if(!NearIcon(cursor)) {suppressHover=false;pointerWatch.Stop();}
                return;
            }
            if(!IsVisible||IsPinned) {pointerWatch.Stop();return;}
            Point first=PointToScreen(new Point()),last=PointToScreen(new Point(ActualWidth,ActualHeight));
            var region=System.Drawing.Rectangle.FromLTRB((int)first.X,(int)first.Y,(int)last.X,(int)last.Y);
            region.Inflate(6,6);
            pointerAway=NearIcon(cursor)||region.Contains(cursor)?0:pointerAway+1;
            if(pointerAway>=3) Dismiss();
        }

        internal void Dismiss(bool suppress=false)
        {
            FinishDrag();
            IsPinned=false;suppressHover=suppress;
            if(IsVisible) Hide();
            if(suppress) pointerWatch.Start();else pointerWatch.Stop();
        }

        internal void Shutdown() { closing=true;FinishDrag();pointerWatch.Stop();Close(); }
    }
}
