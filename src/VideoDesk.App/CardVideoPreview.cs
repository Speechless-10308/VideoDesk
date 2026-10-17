using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VideoDesk.Core;

namespace VideoDesk.App;

// A single, short-lived native player is shared by hover interactions. Two
// owned windows preserve mpv airspace while leaving the card's actions clickable.
internal sealed class CardVideoPreview : IDisposable
{
    private readonly Window owner,videoWindow,chrome;
    private readonly FrameworkElement anchor;
    private readonly DispatcherTimer poll=new(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(100)};
    private readonly MpvHost player;
    private readonly DanmakuSurface danmaku;
    private readonly TextBlock time=new(){Foreground=Brushes.White,FontSize=12,Margin=new(8),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Background=new SolidColorBrush(Color.FromArgb(120,0,0,0))};
    private readonly Action stopped;
    private readonly Func<bool> pointerInside;
    private Rect bounds;
    private bool disposed,shown;
    private readonly DateTime created=DateTime.UtcNow;
    internal bool Ready=>player.Ready&&player.Number("video-params/w")>0;
    internal double Position=>player.Number("time-pos");
    internal bool Muted=>player.Text("mute")=="yes";
    internal int MessageCount=>danmaku.MessageCount;
    internal void SaveScreenshot(string path)=>player.Command("screenshot-to-file",path,"video");
    public CardVideoPreview(Window owner,FrameworkElement anchor,bool hardware,DanmakuPreferences settings,Button later,Action open,Action stopped,Func<bool>? pointerInside=null)
    {
        this.owner=owner;this.anchor=anchor;this.stopped=stopped;this.pointerInside=pointerInside??(()=>ContainsPointer(anchor));player=new(hardware,preview:true);
        videoWindow=new(){Owner=owner,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowActivated=false,ShowInTaskbar=false,Background=Brushes.Black,Content=player,Width=1,Height=1};
        var prefs=new DanmakuPreferences{FontScale=.6,Area=.38,Opacity=settings.Opacity,Fixed=false,Advanced=false,PersonMask=false,Colored=settings.Colored,FontFamily=settings.FontFamily,BlockedWords=settings.BlockedWords,SyncedFilters=settings.SyncedFilters.ToList(),ScaleWithFullscreen=false,Speed=1.1};
        danmaku=new(()=>player.Ready?(player.Number("time-pos"),player.Text("pause")=="yes"||player.Text("paused-for-cache")=="yes",1):(0,true,1),prefs){IsHitTestVisible=false};
        var surface=new Grid{Background=new SolidColorBrush(Color.FromArgb(1,0,0,0)),ClipToBounds=true};surface.Children.Add(danmaku);surface.Children.Add(time);later.HorizontalAlignment=HorizontalAlignment.Right;later.VerticalAlignment=VerticalAlignment.Top;later.Margin=new(6);later.Focusable=false;later.IsTabStop=false;surface.Children.Add(later);
        chrome=new(){Owner=owner,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowActivated=false,ShowInTaskbar=false,AllowsTransparency=true,Background=Brushes.Transparent,Content=surface,Width=1,Height=1,UseLayoutRounding=true,FontFamily=owner.FontFamily};
        chrome.Closed+=(_,_)=>Dispose();videoWindow.Closed+=(_,_)=>Dispose();
        chrome.SourceInitialized+=(_,_)=>HwndSource.FromHwnd(new WindowInteropHelper(chrome).Handle)?.AddHook((IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)=>{if(message==0x21){handled=true;return new IntPtr(3);}return IntPtr.Zero;});
        surface.PreviewMouseLeftButtonDown+=(_,e)=>{if(later.IsMouseOver)return;e.Handled=true;Dispose();owner.Activate();open();};
        surface.MouseLeave+=(_,_)=>{if(!this.pointerInside())Dispose();};
        player.PlaybackFailed+=_=>Dispose();
        owner.Deactivated+=Deactivated;anchor.Unloaded+=Unloaded;anchor.IsVisibleChanged+=Visible;
        // Keep the original cover visible until decoding has produced video.
        // An offscreen native host can initialize without flashing a black card.
        try{bounds=ScreenBounds(anchor);PositionWindows();videoWindow.Left=videoWindow.Top=-16000;videoWindow.Show();surface.SizeChanged+=(_,e)=>surface.Clip=new RectangleGeometry(new Rect(e.NewSize),12,12);}
        catch{Dispose();throw;}
        poll.Tick+=(_,_)=>
        {
            if(!anchor.IsLoaded||!anchor.IsVisible||!this.pointerInside()||ScreenBounds(anchor)!=bounds||owner.WindowState==WindowState.Minimized){Dispose();return;}
            if(!shown){if(!Ready){if(DateTime.UtcNow-created>TimeSpan.FromSeconds(15))Dispose();return;}shown=true;PositionWindows();chrome.Show();DesktopWindowBounds.RoundPreview(videoWindow);}
            time.Text=BilibiliClient.DurationLabel(player.Number("time-pos"))+" / "+BilibiliClient.DurationLabel(player.Number("duration"));
            if(player.Text("eof-reached")=="yes")player.Command("seek","0","absolute+exact");
        };poll.Start();
    }
    public void Load(PlayStream stream)=>player.Load(stream with{AudioUrl=null});
    public void SetDanmaku(IReadOnlyList<DanmakuItem> messages)
    {
        if(disposed)return;
        // One line every three seconds, at most twenty messages. No full-video
        // downloads, mask requests, account actions or watch-history reporting.
        var sparse=messages.Where(m=>m.Mode==1&&m.Time<60).GroupBy(m=>(int)(m.Time/3)).Select(g=>g.First()).Take(20).ToArray();danmaku.SetMessages(sparse);
    }
    private static Rect ScreenBounds(FrameworkElement element)=>new(element.PointToScreen(new Point()),element.PointToScreen(new Point(element.ActualWidth,element.ActualHeight)));
    internal static bool ContainsPointer(FrameworkElement element)=>element.IsLoaded&&element.IsVisible&&new Rect(element.RenderSize).Contains(Mouse.GetPosition(element));
    private void PositionWindows()
    {
        var matrix=PresentationSource.FromVisual(owner)?.CompositionTarget?.TransformFromDevice??Matrix.Identity;var top=matrix.Transform(bounds.TopLeft);var bottom=matrix.Transform(bounds.BottomRight);
        foreach(var window in new[]{videoWindow,chrome}){window.Left=top.X;window.Top=top.Y;window.Width=Math.Max(1,bottom.X-top.X);window.Height=Math.Max(1,bottom.Y-top.Y);}
    }
    private void Deactivated(object? sender,EventArgs e)=>_ = owner.Dispatcher.BeginInvoke(()=>{if(!owner.IsActive&&!videoWindow.IsActive&&!chrome.IsActive)Dispose();});
    private void Unloaded(object sender,RoutedEventArgs e)=>Dispose();
    private void Visible(object sender,DependencyPropertyChangedEventArgs e){if(!anchor.IsVisible)Dispose();}
    public void Dispose()
    {
        if(disposed)return;disposed=true;poll.Stop();owner.Deactivated-=Deactivated;anchor.Unloaded-=Unloaded;anchor.IsVisibleChanged-=Visible;
        danmaku.Dispose();chrome.Close();player.Dispose();videoWindow.Close();stopped();
    }
}
