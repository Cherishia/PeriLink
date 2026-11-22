using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace LinkScope
{
    internal sealed class NotificationIcon : IDisposable
    {
        private const uint Id=1;
        private const int Callback=0x800A;
        private const int MouseMove=0x0200, LeftUp=0x0202, LeftDouble=0x0203, ContextMenu=0x007B;
        private const int Select=0x0400, KeySelect=0x0401, PopupOpen=0x0406, PopupClose=0x0407;
        private readonly HwndSource source;
        private readonly int taskbarCreated;
        private IntPtr icon;
        private bool registered,disposed;
        private int lastClick;
        private long lastClickAt;
        private string title=Dashboard.AppName+" · 设备电量";
        private readonly DispatcherTimer hoverDelay;
        internal event Action Clicked,DoubleClicked,RightClicked;
        internal event Action DevicesChanged;
        internal event Action<Rectangle> HoverOpened;
        internal bool IsRegistered {get {return registered;}}

        internal NotificationIcon(Icon image)
        {
            icon=image.Handle;
            source=new HwndSource(new HwndSourceParameters("LinkScope 通知区域")
                {WindowStyle=unchecked((int)0x80000000),Width=0,Height=0});
            taskbarCreated=RegisterWindowMessage("TaskbarCreated");
            source.AddHook(WndProc);
            hoverDelay=new DispatcherTimer(DispatcherPriority.Background,source.Dispatcher) {Interval=TimeSpan.FromMilliseconds(360)};
            hoverDelay.Tick+=delegate
            {
                hoverDelay.Stop();
                var bounds=Bounds();
                if(bounds.Contains(Forms.Cursor.Position)) RaiseHover(bounds);
            };
            Register();
        }

        private void Register()
        {
            var data=Data();
            registered=ShellNotify(0,ref data);
            if(!registered) return;
            data.Version=4;
            ShellNotify(4,ref data);
        }

        internal void Update(Icon image,string label)
        {
            var next=image.Handle;
            if(icon==next&&title==label&&registered) return;
            icon=next;title=label;
            if(!registered) {Register();return;}
            var data=Data();
            if(!ShellNotify(1,ref data)) Register();
        }

        private NotifyData Data()
        {
            return new NotifyData {Size=(uint)Marshal.SizeOf(typeof(NotifyData)),Window=source.Handle,
                Id=Id,Flags=7,Callback=Callback,Icon=icon,Tip=title,Info="",InfoTitle=""};
        }

        internal Rectangle Bounds()
        {
            var identity=new NotifyIdentifier {Size=(uint)Marshal.SizeOf(typeof(NotifyIdentifier)),Window=source.Handle,Id=Id};
            NativeRect box;
            if(Shell_NotifyIconGetRect(ref identity,out box)==0&&box.Right>box.Left&&box.Bottom>box.Top)
                return Rectangle.FromLTRB(box.Left,box.Top,box.Right,box.Bottom);
            var pointer=Forms.Cursor.Position;
            return new Rectangle(pointer.X-8,pointer.Y-8,16,16);
        }

        private IntPtr WndProc(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
        {
            if(message==taskbarCreated) {Register();return IntPtr.Zero;}
            if(message==0x0219 && wParam.ToInt64()==7) Post(DevicesChanged);
            if(message!=Callback) return IntPtr.Zero;
            int command=(int)(lParam.ToInt64()&0xffff);
            switch(command)
            {
                case MouseMove:
                    if(!hoverDelay.IsEnabled) hoverDelay.Start();
                    break;
                case PopupOpen:
                    hoverDelay.Stop();
                    source.Dispatcher.BeginInvoke(new Action(delegate {if(!disposed) RaiseHover(Bounds());}));
                    break;
                case PopupClose:
                    hoverDelay.Stop();
                    break;
                case LeftUp:
                case Select:
                    hoverDelay.Stop();
                    var ticks=System.Diagnostics.Stopwatch.GetTimestamp();
                    bool duplicate=lastClick!=command&&ticks-lastClickAt<System.Diagnostics.Stopwatch.Frequency/8;
                    lastClick=command;lastClickAt=ticks;
                    if(!duplicate) Post(Clicked);
                    break;
                case KeySelect:
                    hoverDelay.Stop();
                    Post(Clicked);
                    break;
                case LeftDouble:
                    hoverDelay.Stop();
                    Post(DoubleClicked);
                    break;
                case ContextMenu:
                    hoverDelay.Stop();
                    SetForegroundWindow(hwnd);
                    Post(RightClicked);
                    break;
            }
            handled=true;
            return IntPtr.Zero;
        }

        private void RaiseHover(Rectangle bounds)
        {
            var handler=HoverOpened;
            if(handler!=null) source.Dispatcher.BeginInvoke(new Action(delegate {if(!disposed) handler(bounds);}));
        }
        private void Post(Action handler)
        {if(handler!=null) source.Dispatcher.BeginInvoke(handler);}

        public void Dispose()
        {
            if(disposed) return;
            disposed=true;hoverDelay.Stop();
            var data=Data();
            ShellNotify(2,ref data);
            source.Dispose();
        }

        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        private struct NotifyData
        {
            public uint Size;
            public IntPtr Window;
            public uint Id,Flags;
            public int Callback;
            public IntPtr Icon;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Tip;
            public uint State,StateMask;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string Info;
            public uint Version;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)] public string InfoTitle;
            public uint InfoFlags;
            public Guid Identity;
            public IntPtr BalloonIcon;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct NotifyIdentifier {public uint Size;public IntPtr Window;public uint Id;public Guid Identity;}
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect {public int Left,Top,Right,Bottom;}
        [DllImport("shell32.dll",EntryPoint="Shell_NotifyIconW",CharSet=CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellNotify(uint message,ref NotifyData data);
        [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref NotifyIdentifier identity,out NativeRect rect);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int RegisterWindowMessage(string message);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    }
}
