using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;

namespace LinkScope
{
    internal sealed class SettingsWindow : Window
    {
        private readonly Preferences prefs;
        private readonly Action change;
        private readonly Func<IList<Device>> getCards;
        private readonly SmoothScroll scroll;
        private readonly RoundedSurface surface;
        private readonly GlassLayer glass;
        private readonly Image signatureIcon;
        private readonly TextBlock signatureText;
        private readonly System.Windows.Shapes.Path closeGlyph;
        private readonly ContentControl page=new ContentControl();
        private readonly List<Button> navigation=new List<Button>();
        private CheckBox startup;
        private int selectedPage;
        private static readonly string[] Swatches=ThemeColorPicker.PresetHex;
        private static readonly string[] ColorNames={"银灰","红","暖橙","金","绿","青","蓝","紫","玫红"};

        internal SettingsWindow(Window owner,Preferences preferences,Action changed,Func<IList<Device>> cards)
        {
            prefs=preferences;change=changed;getCards=cards;
            Owner=owner;Title=Dashboard.AppName+" · 设置";WindowStartupLocation=WindowStartupLocation.CenterOwner;
            var work=DisplayPlacement.WorkSize(owner??this);
            Width=Math.Min(641,work.Width-12);Height=Math.Min(658,work.Height-12);
            MinWidth=Math.Min(310,Width);MinHeight=Math.Min(290,Height);ShowInTaskbar=false;
            WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
            AllowsTransparency=true;Background=Brushes.Transparent;
            FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI");
            Icon=AppArt.Symbol();
            UseLayoutRounding=true;
            var layout=new DockPanel {Margin=new Thickness(23,20,19,22)};
            var titleSection=new DockPanel {Background=Brushes.Transparent,Margin=new Thickness(6,0,7,13),LastChildFill=true};
            var close=Dashboard.ButtonFor("",Dashboard.BrushOf("#16FFFFFF"),Preferences.Current.Ink,30);
            closeGlyph=DeviceArt.Stroke("M5,5 L19,19 M19,5 L5,19",null,11,1.45);
            close.Height=28;close.Content=closeGlyph;
            System.Windows.Automation.AutomationProperties.SetName(close,"关闭设置");
            close.Click+=delegate {Close();};DockPanel.SetDock(close,Dock.Right);titleSection.Children.Add(close);
            var signature=new StackPanel {Orientation=Orientation.Horizontal,Background=Brushes.Transparent};
            signatureIcon=new Image {Width=25,Height=25,Margin=new Thickness(0,0,7,0)};
            signatureText=new TextBlock {Text="设置",FontSize=14,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center};
            signature.Children.Add(signatureIcon);
            signature.Children.Add(signatureText);
            signature.MouseLeftButtonDown+=delegate {try {DragMove();} catch(InvalidOperationException) {}};
            titleSection.Children.Add(signature);DockPanel.SetDock(titleSection,Dock.Top);layout.Children.Add(titleSection);
            var nav=new StackPanel {Margin=new Thickness(5)};
            var sideRail=new Border {CornerRadius=new CornerRadius(15),Margin=new Thickness(0,0,15,6),Child=nav};
            DockPanel.SetDock(sideRail,Dock.Left);layout.Children.Add(sideRail);
            AddNav(nav,"外观","M12,3 V6 M12,18 V21 M3,12 H6 M18,12 H21 M6,6 L8,8 M16,16 L18,18 M6,18 L8,16 M16,8 L18,6 M12,8 A4,4 0 1 1 12,16 A4,4 0 1 1 12,8",ShowAppearance);
            AddNav(nav,"显示样式","M5,5 H19 Q21,5 21,7 V17 Q21,19 19,19 H5 Q3,19 3,17 V7 Q3,5 5,5 Z M7,12 H10 M13,12 H17",ShowDisplay);
            AddNav(nav,"托盘卡片","M4,5 H20 V17 H4 Z M4,13 H20 M7,15 H9",ShowCards);
            scroll=new SmoothScroll {Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
            layout.Children.Add(scroll);
            surface=new RoundedSurface(24);
            glass=new GlassLayer(surface);
            surface.Children.Add(glass);
            surface.Children.Add(layout);
            Content=surface;
            if(Motion.Enabled) surface.Opacity=0;
            Loaded+=delegate
            {
                Motion.Reveal(surface,19,42,235,.966);
                Motion.Reveal(titleSection,5,92,165);
                Motion.Reveal(sideRail,8,120,185);
                Motion.Reveal(scroll,12,156,215);
            };
            SizeChanged+=delegate
            {
                bool narrow=ActualWidth<592;
                DockPanel.SetDock(sideRail,narrow?Dock.Top:Dock.Left);
                nav.Orientation=narrow?Orientation.Horizontal:Orientation.Vertical;
                sideRail.Margin=narrow?new Thickness(0,0,0,9):new Thickness(0,0,15,6);
                foreach(var item in navigation) item.Width=narrow?84:112;
            };
            DpiChanged+=delegate
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if(!IsVisible) return;
                    var available=DisplayPlacement.WorkSize(this);
                    MinWidth=Math.Min(310,available.Width);MinHeight=Math.Min(290,available.Height);
                    Width=Math.Min(Width,available.Width);Height=Math.Min(Height,available.Height);
                }));
            };
            PreviewKeyDown+=delegate(object sender,System.Windows.Input.KeyEventArgs e)
            {if(e.Key==System.Windows.Input.Key.Escape) {e.Handled=true;Close();}};
            Activated+=delegate {if(startup!=null) startup.IsChecked=StartupRegistration.IsEnabled;};
            ApplyLook();
            ShowAppearance();
        }

        private void AddNav(Panel nav,string title,string path,Action action)
        {
            var item=Dashboard.ButtonFor("",Brushes.Transparent,Preferences.Current.Ink,112);
            var contents=new StackPanel {Orientation=Orientation.Horizontal};
            var glyph=DeviceArt.Stroke(path,Preferences.Current.IconInk,13,1.6);
            glyph.Margin=new Thickness(0,0,6,0);
            contents.Children.Add(glyph);
            contents.Children.Add(new TextBlock {Text=title,VerticalAlignment=VerticalAlignment.Center});
            item.Content=contents;
            System.Windows.Automation.AutomationProperties.SetName(item,title);
            int index=navigation.Count;
            item.Height=36;item.Margin=new Thickness(0,0,0,4);
            item.Click+=delegate
            {
                if(selectedPage==index) return;
                selectedPage=index;ApplyLook();action();scroll.ScrollToTop();
                Motion.Reveal(page,8,0,175);
            };
            nav.Children.Add(item);
            navigation.Add(item);
        }

        internal void SyncLook()
        {
            ApplyLook();
            if(selectedPage==0) ShowAppearance();
            else if(selectedPage==1) ShowDisplay();
            else ShowCards();
        }
        private void Commit()
        {
            prefs.Use();
            prefs.Save();
            ApplyLook();
            change();
        }
        private void ApplyLook()
        {
            var theme=Preferences.Current;
            surface.Background=theme.Backdrop(prefs.Material,true);
            surface.Edge=theme.Line;
            Icon=signatureIcon.Source=AppArt.Symbol();
            signatureText.Foreground=theme.Ink;
            closeGlyph.Stroke=theme.IconInk;
            scroll.ApplyLook();
            glass.Apply();
            for(int i=0;i<navigation.Count;i++)
            {
                navigation[i].Background=i==selectedPage?theme.Tonal:Brushes.Transparent;
                navigation[i].Foreground=theme.Ink;
                ((System.Windows.Shapes.Path)((StackPanel)navigation[i].Content).Children[0]).Stroke=theme.IconInk;
            }
        }
        private static StackPanel PanelOf(string title)
        {
            var content=new StackPanel { Margin=new Thickness(0,1,8,0) };
            content.Children.Add(new TextBlock {Text=title,FontSize=23,FontWeight=FontWeights.SemiBold,Foreground=Preferences.Current.Ink,
                Margin=new Thickness(0,0,0,11)});
            return content;
        }
        private static TextBlock Note(string text)
        {
            return new TextBlock {Text=text,FontSize=12,LineHeight=19,TextWrapping=TextWrapping.Wrap,
                Foreground=Preferences.Current.Muted,Margin=new Thickness(0,4,0,10)};
        }
        private static Button ActionButton(string text,double width)
        {
            var button=Dashboard.ButtonFor(text,Preferences.Current.Tonal,Preferences.Current.Ink,width);
            button.Height=34;button.HorizontalAlignment=HorizontalAlignment.Left;
            button.Margin=new Thickness(0,5,8,5);return button;
        }
        private static readonly ControlTemplate Switch=(ControlTemplate)XamlReader.Parse(@"
          <ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type CheckBox}'>
            <Grid Background='Transparent' MinHeight='34'>
              <Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
              <ContentPresenter VerticalAlignment='Center'/>
              <Border x:Name='pill' Grid.Column='1' Width='40' Height='22' CornerRadius='10' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' Background='{TemplateBinding Background}'>
                <Ellipse x:Name='knob' Fill='White' Width='15' Height='15' HorizontalAlignment='Left' Margin='3,0,3,0'/>
              </Border>
            </Grid>
            <ControlTemplate.Triggers>
              <Trigger Property='IsChecked' Value='True'>
                <Setter TargetName='pill' Property='Background' Value='{Binding Tag, RelativeSource={RelativeSource TemplatedParent}}'/>
                <Setter TargetName='knob' Property='HorizontalAlignment' Value='Right'/>
              </Trigger>
              <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='pill' Property='Opacity' Value='.82'/></Trigger>
              <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='pill' Property='BorderBrush' Value='{Binding Tag, RelativeSource={RelativeSource TemplatedParent}}'/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>");

        private static CheckBox Toggle(string label,bool isChecked,Action<bool> update=null)
        {
            var theme=Preferences.Current;
            var check=new CheckBox {Content=label,IsChecked=isChecked,FontSize=13.5,Margin=new Thickness(0,1,0,2),
                Foreground=theme.Ink,Tag=theme.Ink,Background=theme.Tonal,
                BorderBrush=new SolidColorBrush(Look.Blend(theme.Surface.Color,theme.Ink.Color,.2)),
                Template=Switch,FocusVisualStyle=null};
            if(update!=null)
            {
                check.Checked+=delegate {update(true);};
                check.Unchecked+=delegate {update(false);};
            }
            return check;
        }

        private static Button ColorChip(string name,Brush fill,bool selected,Action picked)
        {
            var button=Dashboard.ButtonFor("",Brushes.Transparent,Preferences.Current.Ink,36);
            button.Height=36;
            var mark=new Grid {Width=32,Height=32};
            mark.Children.Add(new System.Windows.Shapes.Ellipse {Width=24,Height=24,Fill=fill,
                HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center});
            if(selected) mark.Children.Add(new System.Windows.Shapes.Ellipse {Width=32,Height=32,StrokeThickness=2,
                Stroke=Preferences.Current.Ink,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center});
            button.Content=mark;
            button.ToolTip=name;
            System.Windows.Automation.AutomationProperties.SetName(button,name);
            button.Click+=delegate {picked();};
            return button;
        }

        private void ShowAppearance()
        {
            var p=PanelOf("外观");
            var themeGroup=GroupBox();
            themeGroup.Children.Add(SectionLabel("主题"));
            var themes=new UniformGrid {Columns=3};
            for(int i=0;i<Preferences.ThemeNames.Length;i++)
            {
                int index=i;
                var item=Choice(Preferences.ThemeNames[i],i==prefs.ThemeMode);
                item.Click+=delegate {prefs.ThemeMode=index;Commit();ShowAppearance();};
                themes.Children.Add(item);
            }
            themeGroup.Children.Add(themes);
            p.Children.Add(Shell(themeGroup));
            var colorBlock=GroupBox();
            colorBlock.Children.Add(SectionLabel("背景色"));
            var palette=new UniformGrid {Columns=5,Width=180,HorizontalAlignment=HorizontalAlignment.Center};
            colorBlock.SizeChanged+=delegate
            {
                bool wide=colorBlock.ActualWidth>=360;
                palette.Columns=wide?10:5;
                palette.Width=wide?360:180;
            };
            colorBlock.Children.Add(palette);
            foreach(var item in Swatches.Select((color,index)=>new {Color=color,Name=ColorNames[index]}))
            {
                var fill=Dashboard.BrushOf(item.Color??"#C7C7D2");
                palette.Children.Add(ColorChip(item.Name,fill,prefs.CustomColor==item.Color,delegate
                {prefs.CustomColor=item.Color;Commit();ShowAppearance();}));
            }
            var inputRow=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,9,0,1)};
            Color initial;
            if(!Look.TryColor(prefs.CustomColor,out initial)) initial=Look.Neutral.Surface.Color;
            var hex=new TextBox {Text=Look.Hex(initial),FontSize=12.5,Width=89,Height=28,
                Foreground=Preferences.Current.Ink,Background=Brushes.Transparent,BorderThickness=new Thickness(0),
                VerticalContentAlignment=VerticalAlignment.Center,MaxLength=7,Padding=new Thickness(10,0,4,0)};
            System.Windows.Automation.AutomationProperties.SetName(hex,"背景色十六进制");
            inputRow.Children.Add(new Border {CornerRadius=new CornerRadius(9),Child=hex,
                Background=Preferences.Current.Card,
                BorderThickness=new Thickness(1),BorderBrush=Preferences.Current.Tonal,Margin=new Thickness(0,0,6,0)});
            var apply=ActionButton("应用",55);
            apply.Margin=new Thickness(0);apply.Height=29;
            inputRow.Children.Add(apply);colorBlock.Children.Add(inputRow);
            var colorFeedback=Note("");
            colorFeedback.Visibility=Visibility.Collapsed;colorBlock.Children.Add(colorFeedback);
            Action accept=delegate
            {
                Color selected;
                if(!Look.TryColor(hex.Text,out selected))
                {colorFeedback.Text="输入六位颜色值，例如 #A4B2C9。";colorFeedback.Visibility=Visibility.Visible;return;}
                prefs.CustomColor=Look.Hex(selected);Commit();ShowAppearance();
            };
            apply.Click+=delegate {accept();};
            hex.KeyDown+=delegate(object s,System.Windows.Input.KeyEventArgs e) {if(e.Key==System.Windows.Input.Key.Enter) {accept();e.Handled=true;}};
            var rainbow=new LinearGradientBrush();
            rainbow.StartPoint=new Point(0,0);rainbow.EndPoint=new Point(1,1);
            rainbow.GradientStops.Add(new GradientStop(Color.FromRgb(236,105,148),0));
            rainbow.GradientStops.Add(new GradientStop(Color.FromRgb(255,208,111),.35));
            rainbow.GradientStops.Add(new GradientStop(Color.FromRgb(117,211,160),.6));
            rainbow.GradientStops.Add(new GradientStop(Color.FromRgb(116,110,241),1));
            palette.Children.Add(ColorChip("自定义色",rainbow,prefs.CustomColor!=null&&!Swatches.Contains(prefs.CustomColor),delegate
            {
                var dialog=new ThemeColorPicker(this,initial);
                if(dialog.ShowDialog()!=true) return;
                prefs.CustomColor=Look.Hex(dialog.SelectedColor);
                Commit();ShowAppearance();
            }));
            p.Children.Add(Shell(colorBlock));

            var materialGroup=GroupBox();
            materialGroup.Children.Add(SectionLabel("材质"));
            var materials=new UniformGrid {Columns=3,Margin=new Thickness(0,7,0,0)};
            for(int i=0;i<Look.MaterialNames.Length;i++)
            {
                int index=i;
                var selection=Dashboard.ButtonFor("",Brushes.Transparent,Preferences.Current.Ink,145);
                selection.Height=91;selection.Margin=new Thickness(3,0,3,0);
                selection.HorizontalAlignment=HorizontalAlignment.Stretch;selection.ClearValue(FrameworkElement.WidthProperty);
                if(i==prefs.Material)
                {selection.BorderBrush=Preferences.Current.Ink;selection.BorderThickness=new Thickness(1);selection.Background=Preferences.Current.Tonal;}
                var view=new StackPanel {Margin=new Thickness(8,5,8,5)};
                var previewGrid=new Grid();
                previewGrid.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(25)});
                previewGrid.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
                var tint=Preferences.Current.IconInk.Color;
                var side=new Border {Background=new SolidColorBrush(Color.FromArgb(45,tint.R,tint.G,tint.B)),CornerRadius=new CornerRadius(5),Margin=new Thickness(6)};
                previewGrid.Children.Add(side);
                var letters=new StackPanel {VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(1,0,9,0)};
                letters.Children.Add(new Border{Height=3,Width=21,CornerRadius=new CornerRadius(2),HorizontalAlignment=HorizontalAlignment.Left,
                    Background=Preferences.Current.Ink,Opacity=.45,Margin=new Thickness(0,0,0,5)});
                letters.Children.Add(new Border{Height=4,CornerRadius=new CornerRadius(2),Background=Brushes.White,Opacity=i==2?.57:.9});
                Grid.SetColumn(letters,1);previewGrid.Children.Add(letters);
                var thumb=new Border{Width=75,Height=49,CornerRadius=new CornerRadius(11),Background=Preferences.Current.Backdrop(index),
                    BorderBrush=PixelUi.LightEdge,BorderThickness=new Thickness(1),Child=previewGrid};
                selection.SizeChanged+=delegate {thumb.Width=Math.Max(44,Math.Min(104,selection.ActualWidth-17));};
                view.Children.Add(thumb);
                view.Children.Add(new TextBlock{Text=Look.MaterialNames[i],FontWeight=FontWeights.SemiBold,FontSize=12,
                    HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,5,0,0)});
                selection.Content=view;
                System.Windows.Automation.AutomationProperties.SetName(selection,Look.MaterialNames[i]);
                selection.Click+=delegate {prefs.Material=index;Commit();ShowAppearance();};
                materials.Children.Add(selection);
            }
            materialGroup.Children.Add(materials);
            p.Children.Add(Shell(materialGroup));

            var backgroundGroup=GroupBox();
            backgroundGroup.Children.Add(SectionLabel("背景样式"));
            var backgrounds=new UniformGrid {Columns=4};
            var previewLook=Look.Custom(Preferences.Current.Accent,true);
            foreach(int index in new[]{3,4,2,5,6,0,1})
            {
                var option=Choice(Look.BackdropNames[index],index==prefs.BackdropStyle);
                option.Height=80;
                var swatch=new Grid {Width=54,Height=32,Margin=new Thickness(0,0,0,5),
                    Background=AmbientBackdrop.PreviewBase(previewLook,index)};
                var clip=new RectangleGeometry(new Rect(0,0,54,32),7,7);
                swatch.Clip=clip;
                option.SizeChanged+=delegate
                {
                    swatch.Width=Math.Max(28,Math.Min(84,option.ActualWidth-12));
                    swatch.Height=Math.Min(45,swatch.Width*.58);
                    clip.Rect=new Rect(0,0,swatch.Width,swatch.Height);
                };
                var preview=new AmbientBackdrop(false);
                preview.Apply(previewLook,index);
                swatch.Children.Add(preview);
                var sample=new StackPanel();
                sample.Children.Add(swatch);
                sample.Children.Add(new TextBlock {Text=Look.BackdropNames[index],HorizontalAlignment=HorizontalAlignment.Center});
                option.Content=sample;
                option.Click+=delegate {prefs.BackdropStyle=index;Commit();ShowAppearance();};
                backgrounds.Children.Add(option);
            }
            backgroundGroup.Children.Add(backgrounds);
            p.Children.Add(Shell(backgroundGroup));

            var motionGroup=GroupBox();
            motionGroup.Children.Add(Toggle("指针高光",prefs.Glass,on=>{prefs.Glass=on;Commit();}));
            motionGroup.Children.Add(Toggle("省电模式",prefs.Eco,on=>{prefs.Eco=on;Commit();}));
            var startupFeedback=Note("");
            startupFeedback.Visibility=Visibility.Collapsed;
            startup=Toggle("开机自启",StartupRegistration.IsEnabled);
            var toggleStartup=startup;
            toggleStartup.Click+=delegate
            {
                if(StartupRegistration.SetEnabled(toggleStartup.IsChecked==true))
                {startupFeedback.Visibility=Visibility.Collapsed;return;}
                toggleStartup.IsChecked=StartupRegistration.IsEnabled;
                startupFeedback.Text="Windows 未能保存启动设置。";
                startupFeedback.Visibility=Visibility.Visible;
            };
            startup.ToolTip="登录 Windows 后在托盘启动";
            motionGroup.Children.Add(startup);
            motionGroup.Children.Add(startupFeedback);
            p.Children.Add(Shell(motionGroup));
            page.Content=p;
        }
        private static StackPanel GroupBox()
        {return new StackPanel {Margin=new Thickness(13,8,13,8)};}
        private static Border Shell(FrameworkElement item)
        {
            var theme=Preferences.Current;
            var fill=Preferences.CurrentMaterial==Look.GlassMaterial&&!SystemParameters.HighContrast
                ?Look.Paint(Look.Alpha(theme.Card.Color,224)):theme.Card;
            return new Border {Child=item,CornerRadius=new CornerRadius(16),Margin=new Thickness(0,0,0,9),
                Background=fill,BorderBrush=theme.Line,BorderThickness=new Thickness(1)};
        }
        private static TextBlock SectionLabel(string word)
        {
            return new TextBlock {Text=word,FontSize=13.5,FontWeight=FontWeights.SemiBold,
                Foreground=Preferences.Current.Ink,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,1,0,5)};
        }

        private static Button Choice(string title,bool selected)
        {
            var look=Preferences.Current;
            var button=Dashboard.ButtonFor(title,selected?look.Tonal:Brushes.Transparent,look.Ink,75);
            button.ClearValue(WidthProperty);
            button.Margin=new Thickness(2);
            button.Height=32;
            if(selected) {button.BorderBrush=look.IconInk;button.BorderThickness=new Thickness(1);}
            System.Windows.Automation.AutomationProperties.SetName(button,title);
            return button;
        }

        private void ShowDisplay()
        {
            var p=PanelOf("显示样式");
            var group=GroupBox();
            group.Children.Add(Note("同时应用于主窗口与托盘卡片。"));
            var options=new UniformGrid {Columns=4};
            group.SizeChanged+=delegate {options.Columns=group.ActualWidth<330?2:4;};
            for(int i=0;i<Preferences.CardNames.Length;i++)
            {
                int index=i;
                var option=Choice(Preferences.CardNames[i],i==prefs.CardStyle);
                option.Height=105;
                var sample=new StackPanel {HorizontalAlignment=HorizontalAlignment.Center};
                var icon=new PowerStage {Width=52,Height=60};
                icon.Show("鼠标",72,false,false,i);
                sample.Children.Add(icon);
                sample.Children.Add(new TextBlock {Text=Preferences.CardNames[i],Margin=new Thickness(0,6,0,0),
                    HorizontalAlignment=HorizontalAlignment.Center});
                option.Content=sample;
                option.Click+=delegate {prefs.CardStyle=index;Commit();ShowDisplay();};
                options.Children.Add(option);
            }
            group.Children.Add(options);
            p.Children.Add(Shell(group));
            page.Content=p;
        }

        private void ShowCards()
        {
            var p=PanelOf("托盘卡片");
            var options=GroupBox();
            options.Children.Add(SectionLabel("显示数量"));
            var counts=new UniformGrid {Columns=Preferences.MaxTrayCards,Margin=new Thickness(0,2,0,3)};
            for(int i=1;i<=Preferences.MaxTrayCards;i++)
            {
                int limit=i;
                var count=Dashboard.ButtonFor(i.ToString(),i==prefs.TrayLimit?Preferences.Current.Ink:Preferences.Current.Tonal,
                    i==prefs.TrayLimit?Preferences.Current.Shell:Preferences.Current.Ink,38);
                count.ClearValue(FrameworkElement.WidthProperty);
                count.Height=29;count.Margin=new Thickness(1,0,3,0);
                count.FontFamily=PixelUi.Numbers;
                System.Windows.Automation.AutomationProperties.SetName(count,"显示 "+i+" 台");
                count.Click+=delegate {prefs.TrayLimit=limit;Commit();ShowCards();};
                counts.Children.Add(count);
            }
            options.Children.Add(counts);
            p.Children.Add(Shell(options));
            var cards=getCards();
            var selected=prefs.Pins==null?prefs.Peek(cards).Select(d=>Preferences.Key(d.Id)).ToList():new List<string>(prefs.Pins);
            var choices=GroupBox();
            choices.Children.Add(SectionLabel("选择设备"));
            var feedback=Note("按选择顺序，最多显示 "+prefs.TrayLimit+" 台。");
            choices.Children.Add(feedback);
            if(cards.Count==0) choices.Children.Add(Note("连接并刷新设备后可以选择。"));
            bool restoring=false;
            foreach(var d in cards)
            {
                string key=Preferences.Key(d.Id);
                CheckBox check=null;
                check=Toggle(d.Name+"  ·  "+d.Power,selected.Contains(key),on=>
                {
                    if(restoring) return;
                    if(on&&selected.Count>=Preferences.MaxTrayCards)
                    {
                        restoring=true;check.IsChecked=false;restoring=false;
                        feedback.Text="已选满 "+Preferences.MaxTrayCards+" 台。";return;
                    }
                    if(on) selected.Add(key);else selected.Remove(key);
                    prefs.Pins=new List<string>(selected);
                    Commit();feedback.Text="已选 "+selected.Count+" 台，最多显示 "+prefs.TrayLimit+" 台。";
                });
                check.ToolTip=d.Name;choices.Children.Add(check);
            }
            p.Children.Add(Shell(choices));
            var actions=new StackPanel {Orientation=Orientation.Horizontal};
            var reset=ActionButton("自动选择",104);
            reset.Click+=delegate {prefs.Pins=null;Commit();ShowCards();};
            actions.Children.Add(reset);
            var position=ActionButton("恢复默认位置",122);
            position.Click+=delegate {prefs.ClearTrayPosition();Commit();feedback.Text="已恢复默认位置。";};
            actions.Children.Add(position);
            p.Children.Add(actions);
            page.Content=p;
        }
    }
}
