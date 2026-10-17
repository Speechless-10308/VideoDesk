using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MaterialDesignThemes.Wpf;
using VideoDesk.Core;
using System.Windows.Threading;

namespace VideoDesk.App;

// An owned transparent surface avoids WPF/HwndHost airspace without shrinking the native video.
internal sealed class PlayerOverlay : Window,IDisposable
{
    private readonly Window owner;
    private readonly FrameworkElement anchor,controls;
    private readonly DispatcherTimer idle=new(DispatcherPriority.Normal){Interval=TimeSpan.FromMilliseconds(100)};
    private readonly Grid surface;
    private readonly PlayerLoadingView loadingView=new();
    private bool playbackBlocked;
    private readonly Border timelinePreview=new(){Width=160,Background=new SolidColorBrush(Color.FromArgb(240,20,20,23)),CornerRadius=new(5),Visibility=Visibility.Collapsed,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,IsHitTestVisible=false,ClipToBounds=true};
    private readonly Image timelineImage=new(){Width=160,Height=90,Stretch=Stretch.UniformToFill};
    private readonly TextBlock timelineTime=new(){Foreground=Brushes.White,Background=new SolidColorBrush(Color.FromArgb(195,0,0,0)),FontSize=12,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Bottom,Padding=new(4,1,4,1)};
    private readonly TextBlock timelineChapter=new(){Width=160,Foreground=Brushes.White,FontSize=12,FontWeight=FontWeights.Medium,Padding=new(8,5,8,5),TextTrimming=TextTrimming.CharacterEllipsis,Visibility=Visibility.Collapsed};
    private Border? windowGrip;
    private Func<bool>? dragFullscreen;
    private readonly Border feedback=new(){Background=new SolidColorBrush(Color.FromArgb(155,0,0,0)),CornerRadius=new CornerRadius(36),Padding=new Thickness(22,17,22,17),VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,IsHitTestVisible=false,Opacity=0};
    private readonly TextBlock feedbackText=new(){Foreground=Brushes.White,FontSize=19,FontWeight=FontWeights.SemiBold};
    private readonly Border hoverCard=new(){Visibility=Visibility.Collapsed,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top};
    private PlayerMenuPanel? menuPanel;
    private Action? panelClosed;
    private bool HoverOpen=>hoverCard.Visibility==Visibility.Visible;
    private readonly Func<DanmakuItem,bool,Task> likeDanmaku;
    private readonly Func<DanmakuItem,Task> reportDanmaku;
    private readonly Func<DanmakuItem,Task<DanmakuLikeState>> likeState;
    private readonly Dictionary<long,DanmakuLikeState> likeStates=[];
    private readonly HashSet<long> liked=[];
    private DateTime hoverOpened;
    public DanmakuSurface Danmaku {get;}
    public PlayerInteractionLayer Interactions {get;}=new();
    private readonly Action pause;
    private readonly Action<bool> seek;
    private DateTime lastMotion=DateTime.UtcNow;
    private Point lastPoint=new(-1,-1);
    private bool disposed,suspended;
    public bool ControlsVisible=>controls.Visibility==Visibility.Visible;
    private bool Editing=>controls.IsKeyboardFocusWithin&&Keyboard.FocusedElement is TextBoxBase;
    public bool MenuOpen {get;set;}
    public PlayerOverlay(Window owner,FrameworkElement anchor,FrameworkElement controls,Action pause,Action<bool> seek,Action<KeyEventArgs> key,DanmakuSurface danmaku,Func<DanmakuItem,bool,Task> likeDanmaku,Func<DanmakuItem,Task> reportDanmaku,Func<DanmakuItem,Task<DanmakuLikeState>> likeState,bool allowDanmakuActions=true)
    {
        this.owner=owner;this.anchor=anchor;this.controls=controls;this.pause=pause;this.seek=seek;Danmaku=danmaku;this.likeDanmaku=likeDanmaku;this.reportDanmaku=reportDanmaku;this.likeState=likeState;
        Owner=owner;Title="VideoDesk 播放控制";Width=1;Height=1;WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;ShowActivated=false;
        surface=new Grid{Background=new SolidColorBrush(Color.FromArgb(1,0,0,0)),Focusable=true};InputMethod.SetIsInputMethodEnabled(surface,false);controls.VerticalAlignment=VerticalAlignment.Bottom;surface.Children.Add(danmaku);surface.Children.Add(feedback);feedback.Child=feedbackText;surface.Children.Add(controls);Content=surface;
        surface.Children.Add(hoverCard);
        surface.Children.Add(Interactions);Panel.SetZIndex(Interactions,2);Panel.SetZIndex(controls,3);Panel.SetZIndex(hoverCard,4);
        Panel.SetZIndex(loadingView,90);surface.Children.Add(loadingView);loadingView.Clear();
        var previewContent=new StackPanel();var previewPicture=new Grid();previewPicture.Children.Add(timelineImage);previewPicture.Children.Add(timelineTime);previewContent.Children.Add(previewPicture);previewContent.Children.Add(timelineChapter);timelinePreview.Child=previewContent;Panel.SetZIndex(timelinePreview,5);surface.Children.Add(timelinePreview);
        surface.MouseMove+=(_,e)=>{var p=surface.PointToScreen(e.GetPosition(surface));if(Math.Abs(p.X-lastPoint.X)+Math.Abs(p.Y-lastPoint.Y)>1){lastPoint=p;Wake();}if(menuPanel!=null||!allowDanmakuActions||Interactions.IsMouseOver)return;if(!HoverOpen&&!controls.IsMouseOver){Danmaku.HoverAt(e.GetPosition(Danmaku));if(Danmaku.Hovered is {} item)ShowHover(item);}else if(!HoverOpen)Danmaku.ClearHover();};
        surface.MouseLeave+=(_,_)=>{lastMotion=DateTime.UtcNow.AddSeconds(-2);if(!HoverOpen)Danmaku.ClearHover();};
        surface.PreviewMouseLeftButtonDown+=(_,e)=>
        {
            if(windowGrip?.IsMouseOver==true||Interactions.IsMouseOver)return;
            for(DependencyObject? node=e.OriginalSource as DependencyObject;node!=null;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))if(node is ButtonBase or Slider or TextBox or MenuItem)return;
            if(playbackBlocked){e.Handled=true;return;}if(MenuOpen)return;e.Handled=true;FocusManager.SetFocusedElement(owner,anchor);Keyboard.Focus(surface);ActivateSurface(e.ClickCount,e.GetPosition(surface).X);
        };
        PreviewKeyDown+=(_,e)=>{Wake();if(playbackBlocked&&e.Key is not (Key.Escape or Key.Tab)){if(!loadingView.IsKeyboardFocusWithin)e.Handled=true;return;}if(e.Key==Key.Escape&&menuPanel!=null){ClosePanel();e.Handled=true;}else key(e);};
        controls.GotKeyboardFocus+=(_,_)=>Wake();controls.LostKeyboardFocus+=(_,_)=>Wake();
        Activated+=(_,_)=>{if(FocusManager.GetFocusedElement(this) is TextBoxBase editor&&controls.IsAncestorOf(editor))Keyboard.Focus(editor);else Keyboard.Focus(surface);Wake();};
        Deactivated+=(_,_)=>_ = Dispatcher.BeginInvoke(()=>{if(!owner.IsActive&&!IsActive)Hide();});
        idle.Tick+=(_,_)=>{if(playbackBlocked){Cursor=Cursors.Arrow;return;}if(HoverOpen&&DateTime.UtcNow-hoverOpened>TimeSpan.FromMilliseconds(300)&&!hoverCard.IsMouseOver&&!Danmaku.IsOverHovered(Mouse.GetPosition(Danmaku)))CloseHover();if(Editing||timelinePreview.Visibility==Visibility.Visible){Wake();return;}if(DateTime.UtcNow-lastMotion<TimeSpan.FromSeconds(2)||Mouse.Captured!=null||MenuOpen)return;controls.Visibility=Visibility.Collapsed;Cursor=Cursors.None;};
        anchor.LayoutUpdated+=OnLayout;owner.IsVisibleChanged+=OnOwnerVisible;owner.StateChanged+=OnOwnerState;owner.Deactivated+=OnOwnerDeactivated;owner.Activated+=OnOwnerActivated;
        IsVisibleChanged+=(_,_)=>{if(IsVisible&&!disposed)idle.Start();else idle.Stop();};Show();Reposition();
    }
    public void Wake(){lastMotion=DateTime.UtcNow;controls.Visibility=playbackBlocked?Visibility.Collapsed:Visibility.Visible;Cursor=Cursors.Arrow;}
    public void SetPlaybackPresentation(PlayerPresentation state,string message,string details,Func<Task> retry)
    {
        playbackBlocked=state is PlayerPresentation.Loading or PlayerPresentation.Failed;
        controls.IsEnabled=!playbackBlocked;Danmaku.Visibility=Interactions.Visibility=playbackBlocked?Visibility.Hidden:Visibility.Visible;
        if(playbackBlocked){ClosePanel();CloseHover();HideTimelinePreview();if(controls.IsKeyboardFocusWithin)Keyboard.Focus(surface);}
        if(state==PlayerPresentation.Failed)loadingView.Failed(message,details,retry);
        else if(state is PlayerPresentation.Loading or PlayerPresentation.Buffering)loadingView.Loading(message,buffering:!playbackBlocked);
        else loadingView.Clear();
        Wake();
    }
    public void ConfigureWindowDrag(Func<bool> fullscreen,Action exitFullscreen)
    {
        dragFullscreen=fullscreen;
        windowGrip=new Border{Height=24,Background=Brushes.Transparent,VerticalAlignment=VerticalAlignment.Top,Visibility=fullscreen()?Visibility.Visible:Visibility.Collapsed};
        Panel.SetZIndex(windowGrip,100);surface.Children.Add(windowGrip);WindowDrag.Attach(owner,windowGrip,fullscreen,exitFullscreen);
    }
    public void ShowTimelinePreview(FrameworkElement timeline,double x,double seconds,System.Windows.Media.Imaging.BitmapSource? source,string? chapter=null)
    {
        if(disposed||!timeline.IsVisible)return;
        timelineImage.Source=source;timelineImage.Visibility=source==null?Visibility.Collapsed:Visibility.Visible;timelineTime.Text=BilibiliClient.DurationLabel(seconds);
        timelineChapter.Text=chapter??"";timelineChapter.Visibility=string.IsNullOrWhiteSpace(chapter)?Visibility.Collapsed:Visibility.Visible;
        var point=surface.PointFromScreen(timeline.PointToScreen(new Point(x,timeline.ActualHeight-8)));var height=(source==null?20:90)+(timelineChapter.Visibility==Visibility.Visible?27:0);
        timelinePreview.Margin=new(Math.Clamp(point.X-80,8,Math.Max(8,ActualWidth-168)),Math.Max(8,point.Y-height-13),0,0);timelinePreview.Visibility=Visibility.Visible;
    }
    public void HideTimelinePreview(){timelinePreview.Visibility=Visibility.Collapsed;timelineImage.Source=null;}
    internal void ActivateSurface(int clickCount,double x){if(playbackBlocked)return;Wake();pause();if(clickCount==2){var forward=x>=ActualWidth/2;seek(forward);ShowFeedback(forward?"前进 10 秒":"后退 10 秒",forward?1:-1);}}
    public void Suspend(){if(disposed)return;suspended=true;CloseHover();ClosePanel();HideTimelinePreview();Hide();}
    private void CloseHover(){hoverCard.Visibility=Visibility.Collapsed;MenuOpen=menuPanel!=null;Danmaku.ClearHover();}
    public void ShowPanel(FrameworkElement body,FrameworkElement button,Brush background,Brush outline,Action? closed=null)
    {
        ClosePanel();CloseHover();panelClosed=closed;menuPanel=new(body,background,outline,()=>new Rect(button.TranslatePoint(new Point(),surface),button.RenderSize),controls.Height,ClosePanel);Panel.SetZIndex(menuPanel,6);surface.Children.Add(menuPanel);MenuOpen=true;Wake();
    }
    public void ClosePanel(){if(menuPanel==null)return;surface.Children.Remove(menuPanel);menuPanel=null;MenuOpen=HoverOpen;var closed=panelClosed;panelClosed=null;closed?.Invoke();Wake();}
    public Rect? PanelBounds=>menuPanel?.CardBounds;
    public void Resume(){if(disposed)return;suspended=false;Reposition();Wake();}
    private void OnLayout(object? sender,EventArgs e)=>Reposition();
    private void OnOwnerState(object? sender,EventArgs e)=>Reposition();
    private void OnOwnerVisible(object sender,DependencyPropertyChangedEventArgs e)=>Reposition();
    private void OnOwnerDeactivated(object? sender,EventArgs e)=>_ = Dispatcher.BeginInvoke(()=>{if(!owner.IsActive&&!IsActive)Hide();});
    private void OnOwnerActivated(object? sender,EventArgs e){Reposition();Wake();}
    public void Reposition()
    {
        if(disposed)return;
        if(windowGrip!=null)windowGrip.Visibility=dragFullscreen?.Invoke()==true?Visibility.Visible:Visibility.Collapsed;
        if(suspended){if(IsVisible)Hide();return;}
        if(!owner.IsVisible||owner.WindowState==WindowState.Minimized||!anchor.IsVisible||anchor.ActualWidth<1||anchor.ActualHeight<1){if(IsVisible)Hide();return;}
        if(!owner.IsActive&&!IsActive)return;
        var source=PresentationSource.FromVisual(owner);if(source?.CompositionTarget==null)return;
        var screen=source.CompositionTarget.TransformFromDevice.Transform(anchor.PointToScreen(new Point()));
        if(Math.Abs(Left-screen.X)>.2)Left=screen.X;if(Math.Abs(Top-screen.Y)>.2)Top=screen.Y;
        if(Math.Abs(Width-anchor.ActualWidth)>.2)Width=anchor.ActualWidth;if(Math.Abs(Height-anchor.ActualHeight)>.2)Height=anchor.ActualHeight;
        if(!IsVisible)Show();
    }
    public void ShowFeedback(string text,int direction)
    {
        feedbackText.Text=(direction>0?"❯❯  ":direction<0?"❮❮  ":text=="暂停"?"Ⅱ  ":"▶  ")+text;feedback.HorizontalAlignment=direction==0?HorizontalAlignment.Center:direction>0?HorizontalAlignment.Right:HorizontalAlignment.Left;feedback.Margin=new Thickness(direction<0?ActualWidth*.16:0,0,direction>0?ActualWidth*.16:0,0);
        feedback.BeginAnimation(OpacityProperty,new DoubleAnimationUsingKeyFrames{KeyFrames={new DiscreteDoubleKeyFrame(1,TimeSpan.Zero),new LinearDoubleKeyFrame(1,TimeSpan.FromMilliseconds(350)),new LinearDoubleKeyFrame(0,TimeSpan.FromMilliseconds(720))}});
        var scale=new ScaleTransform(.88,.88);feedback.RenderTransformOrigin=new Point(.5,.5);feedback.RenderTransform=scale;scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(1,TimeSpan.FromMilliseconds(170)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(1,TimeSpan.FromMilliseconds(170)));
    }
    private async void ShowHover(DanmakuItem item)
    {
        var row=new StackPanel{Orientation=Orientation.Horizontal};Button B(string label)=>new(){Content=label,Style=(Style)Application.Current.FindResource("PlainButton"),Height=34,FontSize=12,Padding=new Thickness(10,0,10,0)};
        var like=B("读取点赞…");like.IsEnabled=false;like.Click+=async(_,_)=>{like.IsEnabled=false;try{var desired=!liked.Contains(item.Id);await likeDanmaku(item,desired);if(desired)liked.Add(item.Id);else liked.Remove(item.Id);var count=Math.Max(0,likeStates.GetValueOrDefault(item.Id)?.Likes??0)+(desired?1:-1);likeStates[item.Id]=new(desired,Math.Max(0,count));like.Content=(desired?"♥ 已赞":"♡ 点赞")+(count>0?$" {count}":"");}catch(Exception e){like.ToolTip=e.Message;ShowFeedback("操作未成功",0);}finally{like.IsEnabled=true;}};row.Children.Add(like);
        var report=B("举报");report.Click+=async(_,_)=>{CloseHover();await reportDanmaku(item);};row.Children.Add(report);var copy=B("复制");copy.Click+=(_,_)=>{Clipboard.SetText(item.Text);CloseHover();};row.Children.Add(copy);var close=B("×");close.Click+=(_,_)=>CloseHover();row.Children.Add(close);
        hoverCard.Child=row;hoverCard.Background=new SolidColorBrush(Color.FromRgb(250,251,253));hoverCard.BorderBrush=new SolidColorBrush(Color.FromRgb(224,228,237));hoverCard.BorderThickness=new Thickness(1);hoverCard.CornerRadius=new CornerRadius(8);hoverCard.Padding=new Thickness(4);var point=Danmaku.HoverLocation;hoverCard.Margin=new Thickness(Math.Clamp(point.X,8,Math.Max(8,ActualWidth-270)),Math.Clamp(point.Y,8,Math.Max(8,ActualHeight-100)),0,0);hoverOpened=DateTime.UtcNow;hoverCard.Visibility=Visibility.Visible;MenuOpen=true;
        try{if(!likeStates.TryGetValue(item.Id,out var state)){state=await likeState(item);if(disposed)return;if(likeStates.Count>=256)likeStates.Remove(likeStates.Keys.First());likeStates[item.Id]=state;}if(state.Liked)liked.Add(item.Id);else liked.Remove(item.Id);like.Content=(state.Liked?"♥ 已赞":"♡ 点赞")+(state.Likes>0?$" {state.Likes}":"");like.IsEnabled=true;}catch(Exception e){like.Content="状态读取失败";like.ToolTip=e.Message;}
    }
    public void Dispose(){if(disposed)return;disposed=true;idle.Stop();CloseHover();ClosePanel();Interactions.Dispose();Danmaku.Dispose();anchor.LayoutUpdated-=OnLayout;owner.IsVisibleChanged-=OnOwnerVisible;owner.StateChanged-=OnOwnerState;owner.Deactivated-=OnOwnerDeactivated;owner.Activated-=OnOwnerActivated;Close();}
}
