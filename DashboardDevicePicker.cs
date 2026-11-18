using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace LinkScope
{
    internal sealed class DashboardDevicePicker
    {
        private const string DragFormat="PeriLink.VisibilityCard";
        private const string Eye="M2,12 C5,7 9,5 12,5 C15,5 19,7 22,12 C19,17 15,19 12,19 C9,19 5,17 2,12 Z M12,9 A3,3 0 1 1 12,15 A3,3 0 1 1 12,9";
        private static readonly ControlTemplate CardTemplate=(ControlTemplate)XamlReader.Parse(@"
          <ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
              xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Button}'>
            <Border x:Name='card' CornerRadius='10' Background='{TemplateBinding Background}'
                    BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'>
              <ContentPresenter HorizontalAlignment='Stretch' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='card' Property='Opacity' Value='.84'/></Trigger>
              <Trigger Property='IsPressed' Value='True'><Setter TargetName='card' Property='Opacity' Value='.63'/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>");

        internal readonly Border Surface;
        private readonly TextBlock hint,title,instruction;
        private readonly Button choose;
        private readonly System.Windows.Shapes.Path chooseIcon;
        private readonly Popup popup;
        private readonly Border panel;
        private readonly Canvas dragLayer=new Canvas {IsHitTestVisible=false,ClipToBounds=true};
        private readonly Border insertionLine=new Border {Height=3,CornerRadius=new CornerRadius(1.5),Visibility=Visibility.Collapsed};
        private readonly DispatcherTimer scrollTimer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(35)};
        private readonly Lane shown=new Lane("主界面显示"),hidden=new Lane("已隐藏");
        private readonly Func<string,bool> shows;
        private readonly Action<string,bool,string,bool> placeDevice;
        private IList<Device> visibleCards=new List<Device>(),hiddenCards=new List<Device>();
        private Button pressed;
        private Point start,pointer;
        private string dragging,arriving;
        private Border ghost;
        private Lane target;

        internal DashboardDevicePicker(Func<string,bool> shows,Action<string,bool,string,bool> placeDevice)
        {
            this.shows=shows;this.placeDevice=placeDevice;
            var row=new DockPanel {LastChildFill=true,Margin=new Thickness(11,3,5,3)};
            choose=Dashboard.ButtonFor("",Preferences.Current.Tonal,Preferences.Current.Ink,101);
            choose.Height=28;
            chooseIcon=DeviceArt.Stroke(Eye,Preferences.Current.IconInk,14);
            chooseIcon.Margin=new Thickness(0,0,6,0);
            var chooseLabel=new StackPanel {Orientation=Orientation.Horizontal};
            chooseLabel.Children.Add(chooseIcon);
            chooseLabel.Children.Add(new TextBlock {Text="显示设备",VerticalAlignment=VerticalAlignment.Center});
            choose.Content=chooseLabel;
            AutomationProperties.SetName(choose,"显示设备");
            DockPanel.SetDock(choose,Dock.Right);
            row.Children.Add(choose);
            hint=new TextBlock {Text="拖动卡片调整顺序",TextTrimming=TextTrimming.CharacterEllipsis,
                FontSize=12,VerticalAlignment=VerticalAlignment.Center};
            row.Children.Add(hint);
            Surface=new Border {Child=row,Visibility=Visibility.Collapsed,CornerRadius=new CornerRadius(12),
                BorderThickness=new Thickness(1),Margin=new Thickness(4,0,4,6)};

            var body=new DockPanel();
            var header=new StackPanel {Margin=new Thickness(3,1,3,11)};
            title=new TextBlock {Text="显示设备",FontSize=16,FontWeight=FontWeights.SemiBold};
            instruction=new TextBlock {Text="上下拖动排序，拖到另一组切换显示",FontSize=11.5,Margin=new Thickness(0,3,0,0)};
            header.Children.Add(title);header.Children.Add(instruction);
            DockPanel.SetDock(header,Dock.Top);
            body.Children.Add(header);
            var lanes=new UniformGrid {Rows=1,Columns=2};
            shown.Surface.Margin=new Thickness(0,0,4,0);
            hidden.Surface.Margin=new Thickness(4,0,0,0);
            lanes.Children.Add(shown.Surface);lanes.Children.Add(hidden.Surface);
            body.Children.Add(lanes);
            var layers=new Grid();
            dragLayer.Children.Add(insertionLine);
            layers.Children.Add(body);layers.Children.Add(dragLayer);
            panel=new Border {Width=450,CornerRadius=new CornerRadius(19),BorderThickness=new Thickness(1),
                Padding=new Thickness(12),Child=layers};
            popup=new Popup {PlacementTarget=choose,Placement=PlacementMode.Custom,
                AllowsTransparency=true,StaysOpen=false,Child=panel};
            popup.CustomPopupPlacementCallback=(size,anchor,offset)=>new[]
            {
                new CustomPopupPlacement(new Point(anchor.Width-size.Width,anchor.Height+6),PopupPrimaryAxis.Horizontal),
                new CustomPopupPlacement(new Point(anchor.Width-size.Width,-size.Height-6),PopupPrimaryAxis.Horizontal),
            };
            popup.Opened+=delegate {Motion.Enter(panel,7,true);};
            popup.Closed+=delegate {pressed=null;ClearDragPreview();};
            Surface.SizeChanged+=delegate {if(popup.IsOpen) FitPanel();};
            panel.PreviewKeyDown+=delegate(object sender,KeyEventArgs e)
            {
                if(e.Key!=Key.Escape||dragging!=null) return;
                popup.IsOpen=false;
                choose.Focus();e.Handled=true;
            };
            AttachDrop(shown,true);
            AttachDrop(hidden,false);
            scrollTimer.Tick+=delegate {ScrollDropLane();};
            choose.Click+=delegate
            {
                if(popup.IsOpen) {popup.IsOpen=false;return;}
                FitPanel();
                FillCards();
                popup.IsOpen=true;
            };
        }

        private void FitPanel()
        {panel.Width=Math.Max(210,Math.Min(455,Surface.ActualWidth));}

        internal void ShowEditor(bool edit)
        {
            Surface.Visibility=edit?Visibility.Visible:Visibility.Collapsed;
            if(!edit) popup.IsOpen=false;
        }

        internal void ApplyLook()
        {
            var theme=Preferences.Current;
            Surface.Background=panel.Background=theme.Card;
            Surface.BorderBrush=panel.BorderBrush=theme.Line;
            hint.Foreground=instruction.Foreground=theme.Muted;
            choose.Foreground=title.Foreground=theme.Ink;
            chooseIcon.Stroke=theme.IconInk;
            insertionLine.Background=theme.IconInk;
            choose.Background=theme.Tonal;
            shown.ApplyLook();hidden.ApplyLook();
            if(popup.IsOpen) FillCards();
        }

        internal void Update(IList<Device> visible,IList<Device> hidden)
        {
            bool changed=popup.IsOpen&&(!SameDevices(visibleCards,visible)||!SameDevices(hiddenCards,hidden));
            visibleCards=visible;hiddenCards=hidden;
            if(changed||popup.IsOpen&&arriving!=null) FillCards();
        }

        private static bool SameDevices(IList<Device> first,IList<Device> second)
        {
            if(first.Count!=second.Count) return false;
            for(int i=0;i<first.Count;i++)
                if(first[i].Id!=second[i].Id||first[i].Name!=second[i].Name||first[i].Link!=second[i].Link||
                    first[i].Kind!=second[i].Kind) return false;
            return true;
        }

        private void FillCards()
        {
            var focused=Keyboard.FocusedElement as Button;
            string focus=focused==null?null:focused.Tag as string;
            FillLane(shown,visibleCards,true,focus);
            FillLane(hidden,hiddenCards,false,focus);
        }

        private void FillLane(Lane lane,IList<Device> items,bool visible,string focus)
        {
            lane.Cards.Children.Clear();
            lane.Count.Text=items.Count.ToString();
            lane.Empty.Text=visibleCards.Count==0&&hiddenCards.Count==0?"暂无设备":visible?"拖到这里显示":"拖到这里隐藏";
            lane.Empty.Visibility=items.Count==0?Visibility.Visible:Visibility.Collapsed;
            foreach(var device in items)
            {
                var card=DeviceCard(device,visible);
                lane.Cards.Children.Add(card);
                if(device.Id==focus) card.Focus();
                if(device.Id!=arriving) continue;
                card.Dispatcher.BeginInvoke(new Action(delegate
                {if(card.IsVisible) card.BringIntoView();}),System.Windows.Threading.DispatcherPriority.Loaded);
                Motion.Enter(card,8,true);
            }
        }

        private Button DeviceCard(Device device,bool visible)
        {
            var theme=Preferences.Current;
            var label=new Grid {Margin=new Thickness(6,4,5,4)};
            label.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
            label.ColumnDefinitions.Add(new ColumnDefinition());
            label.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
            var art=DeviceArt.Illustration(DeviceArt.Category(device));
            art.Width=23;art.Height=23;
            var badge=new Border {Width=30,Height=30,CornerRadius=new CornerRadius(8),Background=theme.Tonal,
                Margin=new Thickness(0,0,7,0),Child=art};
            label.Children.Add(badge);
            var words=new StackPanel {VerticalAlignment=VerticalAlignment.Center};
            words.Children.Add(new TextBlock {Text=device.Name,TextTrimming=TextTrimming.CharacterEllipsis,
                Foreground=theme.Ink,FontSize=12,FontWeight=FontWeights.SemiBold});
            words.Children.Add(new TextBlock {Text=DeviceArt.Category(device)+" · "+device.Link,TextTrimming=TextTrimming.CharacterEllipsis,
                Foreground=theme.Muted,FontSize=10,Margin=new Thickness(0,1,0,0)});
            Grid.SetColumn(words,1);label.Children.Add(words);
            var handle=new TextBlock {Text="⠿",Foreground=theme.Muted,FontSize=14,Margin=new Thickness(5,0,2,0),
                VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};
            Grid.SetColumn(handle,2);label.Children.Add(handle);
            string action=visible?"从主界面隐藏":"在主界面显示";
            var card=new Button {Content=label,Template=CardTemplate,Tag=device.Id,Cursor=Cursors.SizeAll,MinHeight=42,
                Background=theme.Card,BorderBrush=theme.Line,BorderThickness=new Thickness(1),Margin=new Thickness(0,0,0,5),
                FocusVisualStyle=PixelUi.KeyboardFocus,ToolTip=device.Name+" · "+action};
            AutomationProperties.SetName(card,device.Name+"，"+action);
            card.Click+=delegate {if(dragging==null) MoveCard(device.Id,!visible,new DropSlot());};
            card.PreviewMouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e)
            {pressed=card;start=e.GetPosition(panel);};
            card.PreviewMouseLeftButtonUp+=delegate {pressed=null;};
            card.PreviewMouseMove+=delegate(object sender,MouseEventArgs e) {StartDrag(card,device,e);};
            return card;
        }

        private void StartDrag(Button card,Device device,MouseEventArgs e)
        {
            if(card!=pressed) return;
            if(e.LeftButton!=MouseButtonState.Pressed) {pressed=null;return;}
            var delta=e.GetPosition(panel)-start;
            if(Math.Abs(delta.X)<SystemParameters.MinimumHorizontalDragDistance&&
                Math.Abs(delta.Y)<SystemParameters.MinimumVerticalDragDistance) return;
            e.Handled=true;
            pressed=null;
            dragging=device.Id;
            // A popup that captures outside clicks would close when WPF transfers capture to the drag loop.
            popup.StaysOpen=true;
            if(card.IsMouseCaptured) card.ReleaseMouseCapture();
            card.Opacity=.40;
            try
            {
                CreateGhost(device);
                MoveGhost(e.GetPosition(dragLayer));
                DragDrop.DoDragDrop(card,new DataObject(DragFormat,"card"),DragDropEffects.Move);
            }
            finally
            {
                ClearDragPreview();
                dragging=null;
                card.Opacity=1;
                popup.StaysOpen=false;
            }
        }

        private void AttachDrop(Lane lane,bool visible)
        {
            lane.Surface.AllowDrop=true;
            lane.Surface.PreviewDragOver+=delegate(object sender,DragEventArgs e)
            {
                pointer=e.GetPosition(dragLayer);
                var slot=lane.SlotAt(dragging,e.GetPosition(lane.Scroll).Y);
                var allowed=CanDrop(e,visible,slot);
                if(allowed)
                {
                    ShowTarget(lane);
                    ShowInsertion(lane,slot);
                    if(EdgeScroll(lane)!=0) scrollTimer.Start();else scrollTimer.Stop();
                }
                else ClearTarget();
                MoveGhost(pointer);
                e.Effects=allowed?DragDropEffects.Move:DragDropEffects.None;
                e.Handled=true;
            };
            lane.Surface.PreviewDragLeave+=delegate {ClearTarget();if(ghost!=null) ghost.Visibility=Visibility.Hidden;};
            lane.Surface.PreviewDrop+=delegate(object sender,DragEventArgs e)
            {
                var slot=lane.SlotAt(dragging,e.GetPosition(lane.Scroll).Y);
                bool allowed=CanDrop(e,visible,slot);
                e.Effects=allowed?DragDropEffects.Move:DragDropEffects.None;
                e.Handled=true;
                ClearDragPreview();
                if(allowed) MoveCard(dragging,visible,slot);
            };
        }

        private bool CanDrop(DragEventArgs e,bool visible,DropSlot slot)
        {
            if(dragging==null||!e.Data.GetDataPresent(DragFormat,false)) return false;
            bool sourceVisible=shows(dragging);
            return (sourceVisible!=visible||slot.Card!=null)&&
                (sourceVisible?visibleCards:hiddenCards).Any(d=>d.Id==dragging);
        }

        private void MoveCard(string id,bool visible,DropSlot slot)
        {
            arriving=id;
            try {placeDevice(id,visible,slot.Card==null?null:(string)slot.Card.Tag,slot.After);}
            finally {arriving=null;}
        }

        private void ShowInsertion(Lane lane,DropSlot slot)
        {
            insertionLine.Visibility=slot.Card==null?Visibility.Collapsed:Visibility.Visible;
            if(slot.Card==null) return;
            var at=slot.Card.TranslatePoint(new Point(0,slot.After?slot.Card.ActualHeight+1:-3),dragLayer);
            var top=lane.Scroll.TranslatePoint(new Point(),dragLayer).Y;
            insertionLine.Width=slot.Card.ActualWidth;
            Canvas.SetLeft(insertionLine,at.X);
            Canvas.SetTop(insertionLine,Math.Max(top,Math.Min(at.Y,top+lane.Scroll.ViewportHeight-insertionLine.Height)));
        }

        private double EdgeScroll(Lane lane)
        {
            var scroll=lane.Scroll;
            double y=dragLayer.TranslatePoint(pointer,scroll).Y;
            double height=scroll.ViewportHeight;
            if(y<0||y>height) return 0;
            const double edge=25;
            if(y<edge&&scroll.VerticalOffset>0) return -Math.Max(4,(edge-y)*.58);
            if(y>height-edge&&scroll.VerticalOffset<scroll.ScrollableHeight) return Math.Max(4,(y-height+edge)*.58);
            return 0;
        }

        private void ScrollDropLane()
        {
            if(target==null||dragging==null) {scrollTimer.Stop();return;}
            double step=EdgeScroll(target);
            if(step==0) {scrollTimer.Stop();return;}
            var scroll=target.Scroll;
            scroll.JumpTo(scroll.VerticalOffset+step);
            ShowInsertion(target,target.SlotAt(dragging,dragLayer.TranslatePoint(pointer,scroll).Y));
        }

        private void ClearTarget()
        {
            scrollTimer.Stop();
            ShowTarget(null);
            insertionLine.Visibility=Visibility.Collapsed;
        }

        private void ShowTarget(Lane lane)
        {
            if(target==lane) return;
            if(target!=null) target.Highlight(false);
            target=lane;
            if(target!=null) target.Highlight(true);
        }

        private void CreateGhost(Device device)
        {
            var theme=Preferences.Current;
            var label=new DockPanel();
            var art=DeviceArt.Illustration(DeviceArt.Category(device));
            art.Width=20;art.Height=20;art.Margin=new Thickness(0,0,6,0);
            label.Children.Add(art);
            label.Children.Add(new TextBlock {Text=device.Name,Foreground=theme.Ink,FontSize=11.5,
                FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center});
            ghost=new Border {Width=142,Height=32,Background=theme.Card,BorderBrush=theme.IconInk,BorderThickness=new Thickness(1),
                CornerRadius=new CornerRadius(10),Padding=new Thickness(7,3,7,3),Child=label};
            dragLayer.Children.Add(ghost);
        }

        private void MoveGhost(Point point)
        {
            if(ghost==null) return;
            ghost.Visibility=Visibility.Visible;
            Canvas.SetLeft(ghost,Math.Max(0,Math.Min(point.X+9,dragLayer.ActualWidth-ghost.Width)));
            Canvas.SetTop(ghost,Math.Max(0,Math.Min(point.Y+10,dragLayer.ActualHeight-ghost.Height)));
        }

        private void ClearDragPreview()
        {
            ClearTarget();
            if(ghost!=null) dragLayer.Children.Remove(ghost);
            ghost=null;
        }

        private struct DropSlot
        {
            internal Button Card;
            internal bool After;
        }

        private sealed class Lane
        {
            internal readonly Border Surface;
            internal readonly StackPanel Cards=new StackPanel();
            internal readonly TextBlock Count,Empty;
            private readonly TextBlock title;
            internal readonly SmoothScroll Scroll;

            internal Lane(string name)
            {
                var body=new DockPanel();
                var header=new DockPanel {Margin=new Thickness(1,0,1,7)};
                Count=new TextBlock {FontSize=11,VerticalAlignment=VerticalAlignment.Center};
                DockPanel.SetDock(Count,Dock.Right);header.Children.Add(Count);
                title=new TextBlock {Text=name,FontSize=12,FontWeight=FontWeights.SemiBold};
                header.Children.Add(title);
                DockPanel.SetDock(header,Dock.Top);body.Children.Add(header);
                var stack=new Grid();
                Scroll=new SmoothScroll {Content=Cards,MaxHeight=220,
                    VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
                stack.Children.Add(Scroll);
                Empty=new TextBlock {FontSize=11,HorizontalAlignment=HorizontalAlignment.Center,
                    VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};
                stack.Children.Add(Empty);body.Children.Add(stack);
                Surface=new Border {CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1),MinHeight=122,
                    Padding=new Thickness(7,7,7,3),Child=body};
            }

            internal void ApplyLook()
            {
                var theme=Preferences.Current;
                title.Foreground=theme.Ink;
                Count.Foreground=Empty.Foreground=theme.Muted;
                Scroll.ApplyLook();
                Highlight(false);
            }

            internal DropSlot SlotAt(string id,double y)
            {
                var last=new DropSlot();
                foreach(Button card in Cards.Children)
                {
                    if((string)card.Tag==id||card.ActualHeight<=0) continue;
                    double top=card.TranslatePoint(new Point(),Scroll).Y;
                    if(top+card.ActualHeight<=0||top>=Scroll.ViewportHeight) continue;
                    if(y<top+card.ActualHeight/2) return new DropSlot {Card=card};
                    last=new DropSlot {Card=card,After=true};
                }
                return last;
            }

            internal void Highlight(bool active)
            {
                var theme=Preferences.Current;
                Surface.Background=active?theme.Tonal:theme.Shell;
                Surface.BorderBrush=active?theme.IconInk:theme.Line;
            }
        }
    }
}
