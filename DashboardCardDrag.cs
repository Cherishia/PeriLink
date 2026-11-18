using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace LinkScope
{
    internal sealed class DashboardCardDrag
    {
        private const string Format="PeriLink.MainCard";
        private readonly SmoothScroll view;
        private readonly Panel cards;
        private readonly Func<bool> gridLayout;
        private readonly DispatcherTimer edgeScroll;
        private BatteryTile pressed,dragging,indicated;
        private Point down,over;
        internal bool Enabled {get;private set;}
        internal event Action<string,string,bool> Moved;

        internal DashboardCardDrag(SmoothScroll view,Panel cards,Func<bool> gridLayout)
        {
            this.view=view;this.cards=cards;this.gridLayout=gridLayout;
            edgeScroll=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(35)};
            edgeScroll.Tick+=delegate {ScrollEdge();};
            view.PreviewMouseLeftButtonDown+=OnDown;
            view.PreviewMouseLeftButtonUp+=delegate {pressed=null;};
            view.PreviewMouseMove+=OnMove;
            view.PreviewDragOver+=OnDragOver;
            view.PreviewDragLeave+=delegate {ClearTarget();};
            view.PreviewDrop+=OnDrop;
        }

        internal void Enable(bool enabled)
        {
            Enabled=view.AllowDrop=enabled;
            pressed=null;
            if(!enabled) ClearTarget();
            RefreshTiles();
        }

        internal void RefreshTiles()
        {
            foreach(BatteryTile card in cards.Children)
            {
                card.Cursor=Enabled?Cursors.SizeAll:null;
                if(indicated==card) card.SetDropHint(null);
            }
            indicated=null;
        }

        private Rect Viewport
        {get {return new Rect(0,0,Math.Min(view.ViewportWidth,view.ActualWidth),Math.Min(view.ViewportHeight,view.ActualHeight));}}

        private void OnDown(object sender,MouseButtonEventArgs e)
        {
            if(!Enabled) return;
            down=e.GetPosition(view);
            bool after;
            pressed=FindTarget(down,true,out after);
        }

        private void OnMove(object sender,MouseEventArgs e)
        {
            if(!Enabled||pressed==null) return;
            if(e.LeftButton!=MouseButtonState.Pressed) {pressed=null;return;}
            var delta=e.GetPosition(view)-down;
            if(Math.Abs(delta.X)<SystemParameters.MinimumHorizontalDragDistance&&
                Math.Abs(delta.Y)<SystemParameters.MinimumVerticalDragDistance) return;
            var source=pressed;
            pressed=null;
            if(!cards.Children.Contains(source)) return;
            dragging=source;
            source.Opacity=.54;
            try {DragDrop.DoDragDrop(source,new DataObject(Format,"card"),DragDropEffects.Move);}
            finally
            {
                ClearTarget();
                source.Opacity=1;
                dragging=null;
            }
        }

        private bool CanDrop(DragEventArgs e)
        {return Enabled&&dragging!=null&&cards.Children.Contains(dragging)&&e.Data.GetDataPresent(Format,false);}

        private void OnDragOver(object sender,DragEventArgs e)
        {
            e.Handled=true;
            e.Effects=DragDropEffects.None;
            if(!CanDrop(e)) return;
            over=e.GetPosition(view);
            bool after;
            var target=FindTarget(over,false,out after);
            ShowTarget(target!=dragging?target:null,after);
            if(indicated!=null) e.Effects=DragDropEffects.Move;
            if(ScrollDelta()!=0) edgeScroll.Start();else edgeScroll.Stop();
        }

        private void OnDrop(object sender,DragEventArgs e)
        {
            e.Handled=true;
            e.Effects=DragDropEffects.None;
            if(CanDrop(e))
            {
                bool after;
                var target=FindTarget(e.GetPosition(view),false,out after);
                if(target!=null&&target!=dragging)
                {
                    e.Effects=DragDropEffects.Move;
                    if(Moved!=null) Moved(dragging.DeviceId,target.DeviceId,after);
                }
            }
            ClearTarget();
        }

        private BatteryTile FindTarget(Point point,bool insideOnly,out bool after)
        {
            after=false;
            var area=Viewport;
            if(!area.Contains(point)) return null;
            BatteryTile best=null;
            double distance=double.PositiveInfinity;
            foreach(BatteryTile card in cards.Children)
            {
                if(card.ActualWidth<=0||card.ActualHeight<=0) continue;
                var bounds=new Rect(card.TranslatePoint(new Point(),view),card.RenderSize);
                if(!area.IntersectsWith(bounds)) continue;
                double x=Math.Max(0,Math.Max(bounds.Left-point.X,point.X-bounds.Right));
                double y=Math.Max(0,Math.Max(bounds.Top-point.Y,point.Y-bounds.Bottom));
                double score=x*x+y*y;
                if(score>=distance) continue;
                distance=score;
                best=card;
                after=point.X>bounds.Left+bounds.Width/2;
                if(score==0) break;
            }
            return !insideOnly||distance==0?best:null;
        }

        private double ScrollDelta()
        {
            var area=Viewport;
            if(!area.Contains(over)) return 0;
            bool vertical=gridLayout();
            double limit=vertical?view.ScrollableHeight:view.ScrollableWidth;
            double offset=vertical?view.VerticalOffset:view.HorizontalOffset;
            double length=vertical?area.Height:area.Width;
            double position=vertical?over.Y:over.X;
            double edge=Math.Min(41,length/4);
            if(position<edge&&offset>0) return -Math.Max(6,(edge-position)*.55);
            if(position>length-edge&&offset<limit) return Math.Max(6,(position-length+edge)*.55);
            return 0;
        }

        private void ScrollEdge()
        {
            double distance=ScrollDelta();
            if(dragging==null||distance==0) {edgeScroll.Stop();return;}
            view.JumpTo((gridLayout()?view.VerticalOffset:view.HorizontalOffset)+distance);
            bool after;
            var target=FindTarget(over,false,out after);
            ShowTarget(target!=dragging?target:null,after);
        }

        private void ShowTarget(BatteryTile card,bool after)
        {
            if(indicated!=null&&indicated!=card) indicated.SetDropHint(null);
            indicated=card;
            if(indicated!=null) indicated.SetDropHint(after);
        }

        private void ClearTarget()
        {
            edgeScroll.Stop();
            ShowTarget(null,false);
        }
    }
}
