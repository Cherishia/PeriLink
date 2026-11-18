using System;
using System.Windows.Media;

namespace LinkScope
{
    internal sealed class RenderLoop
    {
        private readonly Action<double> update;
        private TimeSpan? lastRender;
        internal bool Active {get;private set;}

        internal RenderLoop(Action<double> tick) {update=tick;}
        internal void Start()
        {
            if(Active) return;
            Active=true;
            CompositionTarget.Rendering+=Frame;
        }
        internal void Stop()
        {
            if(!Active) return;
            Active=false;lastRender=null;
            CompositionTarget.Rendering-=Frame;
        }
        private void Frame(object sender,EventArgs args)
        {
            var time=((RenderingEventArgs)args).RenderingTime;
            if(lastRender==time) return;
            double elapsed=lastRender.HasValue?(time-lastRender.Value).TotalSeconds:1d/Motion.TargetFps;
            lastRender=time;
            update(Math.Max(.001,Math.Min(.05,elapsed)));
        }
    }
}
