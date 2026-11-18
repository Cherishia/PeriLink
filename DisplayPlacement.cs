using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Forms=System.Windows.Forms;
using PixelPoint=System.Drawing.Point;
using PixelRect=System.Drawing.Rectangle;

namespace LinkScope
{
    internal static class DisplayPlacement
    {
        private const uint MoveOnly=0x15;

        internal static IntPtr Handle(Window window)
        {return new WindowInteropHelper(window).Handle;}

        internal static double Scale(Window window)
        {
            var handle=Handle(window);
            if(handle!=IntPtr.Zero)
            {
                try
                {
                    var dpi=GetDpiForWindow(handle);
                    if(dpi>0) return dpi/96d;
                }
                catch(EntryPointNotFoundException) {}
            }
            return VisualTreeHelper.GetDpi(window).DpiScaleX;
        }

        internal static Forms.Screen ScreenFor(Window window)
        {
            var handle=Handle(window);
            return handle==IntPtr.Zero?Forms.Screen.PrimaryScreen:Forms.Screen.FromHandle(handle);
        }

        internal static PixelRect Bounds(Window window)
        {
            NativeRect rect;
            if(GetWindowRect(Handle(window),out rect)) return PixelRect.FromLTRB(rect.Left,rect.Top,rect.Right,rect.Bottom);
            return PixelRect.Empty;
        }

        internal static void Move(Window window,PixelPoint position)
        {
            var handle=Handle(window);
            if(handle!=IntPtr.Zero) SetWindowPos(handle,IntPtr.Zero,position.X,position.Y,0,0,MoveOnly);
        }

        internal static void SeatOnMonitor(Window window,Forms.Screen target)
        {
            if(ScreenFor(window).DeviceName==target.DeviceName) return;
            var area=target.WorkingArea;
            Move(window,new PixelPoint(area.Left+area.Width/3,area.Top+area.Height/3));
        }

        internal static Size WorkSize(Window window)
        {
            var work=ScreenFor(window).WorkingArea;
            double scale=Scale(window);
            return new Size(work.Width/scale,work.Height/scale);
        }

        internal static double Fraction(int offset,int travel)
        {return travel<=0?0:Math.Max(0,Math.Min(1,offset/(double)travel));}

        internal static int Travel(int area,int window,int margin)
        {return Math.Max(0,area-window-2*margin);}

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect {public int Left,Top,Right,Bottom;}
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window,out NativeRect rect);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window,IntPtr order,int x,int y,int w,int h,uint flags);
    }
}
