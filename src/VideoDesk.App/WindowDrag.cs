using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace VideoDesk.App;

internal static class WindowDrag
{
    public static void Attach(Window window,FrameworkElement grip,Func<bool>? fullscreen=null,Action? exitFullscreen=null)
    {
        Point? start=null;double fraction=0,offset=0;
        grip.MouseLeftButtonDown+=(_,e)=>
        {
            for(DependencyObject? node=e.OriginalSource as DependencyObject;node!=null&&node!=grip;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))
                if(node is ButtonBase or TextBoxBase or Slider or ComboBox)return;
            if(e.ClickCount==2){if(fullscreen?.Invoke()==true)exitFullscreen?.Invoke();else window.WindowState=window.WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;e.Handled=true;return;}
            start=grip.PointToScreen(e.GetPosition(grip));var p=window.PointFromScreen(start.Value);fraction=Math.Clamp(p.X/Math.Max(1,window.ActualWidth),0,1);offset=Math.Clamp(p.Y,8,32);grip.CaptureMouse();e.Handled=true;
        };
        grip.MouseLeftButtonUp+=(_,_)=>{start=null;if(grip.IsMouseCaptured)grip.ReleaseMouseCapture();};
        grip.LostMouseCapture+=(_,_)=>start=null;
        grip.MouseMove+=(_,e)=>
        {
            if(start is not {} initial||e.LeftButton!=MouseButtonState.Pressed)return;
            var point=grip.PointToScreen(e.GetPosition(grip));var dpi=VisualTreeHelper.GetDpi(window);
            if(Math.Abs(point.X-initial.X)<SystemParameters.MinimumHorizontalDragDistance*dpi.DpiScaleX&&Math.Abs(point.Y-initial.Y)<SystemParameters.MinimumVerticalDragDistance*dpi.DpiScaleY)return;
            start=null;grip.ReleaseMouseCapture();
            var wasFullscreen=fullscreen?.Invoke()==true;if(wasFullscreen)exitFullscreen?.Invoke();
            Restore(window,point,fraction,offset,wasFullscreen);
            window.Activate();if(Mouse.LeftButton==MouseButtonState.Pressed)window.DragMove();e.Handled=true;
        };
    }
    internal static void Restore(Window window,Point screen,double fraction,double offset,bool force=false)
    {
        if(window.WindowState!=WindowState.Maximized&&!force)return;
        var bounds=window.RestoreBounds;var source=PresentationSource.FromVisual(window);var point=source?.CompositionTarget?.TransformFromDevice.Transform(screen)??screen;
        window.WindowState=WindowState.Normal;
        if(bounds.Width>0&&bounds.Height>0){window.Width=bounds.Width;window.Height=bounds.Height;}
        window.Left=point.X-window.Width*Math.Clamp(fraction,0,1);window.Top=point.Y-offset;
    }
}
