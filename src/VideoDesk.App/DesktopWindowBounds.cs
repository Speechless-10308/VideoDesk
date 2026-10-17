using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VideoDesk.App;

internal static class DesktopWindowBounds
{
    internal const double CornerRadius = 24;
    public static void Attach(Window window, Func<bool> isFullscreen)
    {
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            (int Width,int Height,int Radius) lastShape=default;
            void Round()
            {
                var preference=1;DwmSetWindowAttribute(handle,33,ref preference,sizeof(int));
                if(!GetWindowRect(handle,out var rect))return;
                var width=rect.Right-rect.Left;var height=rect.Bottom-rect.Top;
                var radius=window.WindowState==WindowState.Normal&&!isFullscreen()?(int)Math.Round(CornerRadius*GetDpiForWindow(handle)/96d):0;
                var shape=(width,height,radius);if(shape==lastShape||width<=0||height<=0)return;lastShape=shape;
                if(radius==0){SetWindowRgn(handle,IntPtr.Zero,true);return;}
                var region=CreateRoundRectRgn(0,0,width+1,height+1,radius*2,radius*2);
                // On success Windows owns the region and releases the previous one.
                if(region!=IntPtr.Zero&&SetWindowRgn(handle,region,true)==0)DeleteObject(region);
            }
            Round();window.StateChanged+=(_,_)=>Round();window.SizeChanged+=(_,_)=>Round();
            HwndSource.FromHwnd(handle)?.AddHook((IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if(message==0x02E0)Round();
                // Ordinary maximization uses the work area; video fullscreen uses
                // the complete monitor, including the area occupied by the taskbar.
                if (message != 0x0024) return IntPtr.Zero;
                var monitor = MonitorFromWindow(hwnd, 2);
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (!GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;
                var area=isFullscreen()?info.Monitor:info.Work;
                var limits = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                limits.MaxPosition = new PointI { X = area.Left - info.Monitor.Left, Y = area.Top - info.Monitor.Top };
                limits.MaxSize = new PointI { X = area.Right - area.Left, Y = area.Bottom - area.Top };
                Marshal.StructureToPtr(limits, lParam, false);
                handled = true;
                return IntPtr.Zero;
            });
        };
    }

    internal static void ApplyFullscreenBounds(Window window)
    {
        var handle=new WindowInteropHelper(window).Handle;
        var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
        if(handle==IntPtr.Zero||!GetMonitorInfo(MonitorFromWindow(handle,2),ref info))return;
        // Setting Maximized again is a no-op when the user already maximized
        // the window. Apply physical monitor bounds without changing z-order.
        var area=info.Monitor;
        SetWindowPos(handle,IntPtr.Zero,area.Left,area.Top,area.Right-area.Left,area.Bottom-area.Top,0x0004|0x0010|0x0020|0x0200);
    }
    internal static void RoundPreview(Window window)
    {
        var handle=new WindowInteropHelper(window).Handle;if(!GetWindowRect(handle,out var rect))return;
        var radius=(int)Math.Round(12*GetDpiForWindow(handle)/96d);var region=CreateRoundRectRgn(0,0,rect.Right-rect.Left+1,rect.Bottom-rect.Top+1,radius*2,radius*2);
        if(region!=IntPtr.Zero&&SetWindowRgn(handle,region,true)==0)DeleteObject(region);
    }

    [StructLayout(LayoutKind.Sequential)] private struct PointI { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RectI { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public RectI Monitor, Work; public int Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public PointI Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd,out RectI rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd,IntPtr region,bool redraw);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left,int top,int right,int bottom,int width,int height);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
}
