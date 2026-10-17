using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using VideoDesk.Core;

namespace VideoDesk.App;

// A modeless compact queue. Its playback lifetime is independent of navigation
// in the main window; only one of the two players produces audio at a time.
internal sealed class WatchLaterWindow : Window
{
    private readonly BilibiliClient api;
    private readonly ImageCache images;
    private readonly LocalStore local;
    private readonly ClientPreferences preferences;
    private readonly Brush ink,muted,accent,surface,canvas,selected;
    private readonly Func<int,CancellationToken,Task<WatchLaterPage>> fetch;
    private readonly Func<VideoCard,Task> remove;
    private readonly Func<WatchLaterItem,Task> openMain;
    private readonly Action beforePlay;
    private readonly Func<bool> canReport;
    private readonly CancellationTokenSource lifetime=new();
    private CancellationTokenSource? playback;
    private readonly List<WatchLaterItem> items=[];
    private readonly Grid root=new();
    private readonly Border stage=new(){Background=Brushes.Black,CornerRadius=new(12),ClipToBounds=true,Margin=new(16,0,16,0)};
    private readonly Image poster=new(){Stretch=Stretch.UniformToFill};
    private readonly Grid stageContent=new(){Background=Brushes.Black};
    private readonly SmoothScrollViewer scroll;
    private readonly ViewportListRows<WatchLaterItem> rows;
    private readonly TextBlock count=new(),notice=new(),time=new();
    private readonly Button play,next,previous,posterPlay,refresh;
    private readonly PlaybackTimeline timeline=new(){Height=26,Minimum=0,Maximum=1,Margin=new(14,0,14,0)};
    private readonly DispatcherTimer tick=new(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(250)};
    private readonly LoadingGlyph loading;
    private readonly FrameworkElement controlBar;
    private MpvHost? player;
    private PlayerOverlay? overlay;
    private StoryboardPreview? preview;
    private WatchLaterItem? current;
    private VideoPart? part;
    private IReadOnlyList<VideoPart> parts=[];
    private int page,revision,requestedSegment;
    private long total;
    private bool more,busy,closed,reporting;
    private DateTime lastSave,lastReport;
    internal Task InitialLoad {get;private set;}=Task.CompletedTask;
    internal Task HistorySync {get;private set;}=Task.CompletedTask;

    public WatchLaterWindow(Window owner,BilibiliClient api,ImageCache images,LocalStore local,ClientPreferences preferences,
        Brush ink,Brush muted,Brush accent,Brush surface,Brush canvas,Brush selected,
        Func<int,CancellationToken,Task<WatchLaterPage>> fetch,Func<VideoCard,Task> remove,Func<WatchLaterItem,Task> openMain,Action beforePlay,Func<bool> canReport)
    {
        this.api=api;this.images=images;this.local=local;this.preferences=preferences;this.ink=ink;this.muted=muted;this.accent=accent;this.surface=surface;this.canvas=canvas;this.selected=selected;this.fetch=fetch;this.remove=remove;this.openMain=openMain;this.beforePlay=beforePlay;this.canReport=canReport;
        Title="稍后再看 · VideoDesk";Icon=owner.Icon;FontFamily=owner.FontFamily;Foreground=ink;Background=canvas;
        var scale=Math.Clamp(preferences.InterfaceScale,.9,1.25);Width=440*scale;Height=780*scale;MinWidth=380*scale;MinHeight=580*scale;
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.CanResize;ShowInTaskbar=true;UseLayoutRounding=true;SnapsToDevicePixels=true;TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);
        WindowChrome.SetWindowChrome(this,new WindowChrome{CaptionHeight=0,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(0)});DesktopWindowBounds.Attach(this,()=>false);
        root.Background=canvas;root.LayoutTransform=new ScaleTransform(scale,scale);foreach(var height in new[]{GridLength.Auto,GridLength.Auto,GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star)})root.RowDefinitions.Add(new(){Height=height});Content=root;
        var header=new Grid{Height=56,Background=Brushes.Transparent,Margin=new(16,0,8,0)};header.ColumnDefinitions.Add(new());header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var title=new TextBlock{Text="稍后再看",FontSize=17,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center};header.Children.Add(title);
        var windowTools=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};var minimize=IconButton(PackIconKind.Minus,"最小化");minimize.Click+=(_,_)=>WindowState=WindowState.Minimized;windowTools.Children.Add(minimize);var close=IconButton(PackIconKind.Close,"关闭稍后再看");close.Click+=(_,_)=>Close();windowTools.Children.Add(close);Grid.SetColumn(windowTools,1);header.Children.Add(windowTools);WindowDrag.Attach(this,header);root.Children.Add(header);
        stageContent.Children.Add(poster);posterPlay=IconButton(PackIconKind.Play,"开始播放",48);posterPlay.Height=48;posterPlay.Foreground=Brushes.White;posterPlay.Background=new SolidColorBrush(Color.FromArgb(160,20,20,25));posterPlay.HorizontalAlignment=HorizontalAlignment.Center;posterPlay.VerticalAlignment=VerticalAlignment.Center;posterPlay.Click+=async(_,_)=>{if(current is {} item)await Safe(()=>Play(item));};stageContent.Children.Add(posterPlay);stage.Child=stageContent;Grid.SetRow(stage,1);root.Children.Add(stage);
        var controls=new StackPanel{Margin=new(0,2,0,0)};controlBar=controls;timeline.Foreground=accent;timeline.TrackBackground=muted;controls.Children.Add(timeline);var actions=new Grid{Height=36,Margin=new(16,0,16,0)};foreach(var width in new[]{new GridLength(32),new GridLength(32),new GridLength(32),new GridLength(1,GridUnitType.Star),new GridLength(32),new GridLength(32)})actions.ColumnDefinitions.Add(new(){Width=width});
        void Place(UIElement element,int column){Grid.SetColumn(element,column);actions.Children.Add(element);}
        previous=IconButton(PackIconKind.SkipPrevious,"上一条");previous.Click+=async(_,_)=>await Safe(()=>Step(-1));Place(previous,0);
        play=IconButton(PackIconKind.Play,"播放 / 暂停");play.Click+=async(_,_)=>{if(player==null&&current is {} item)await Safe(()=>Play(item));else TogglePause();};Place(play,1);
        next=IconButton(PackIconKind.SkipNext,"下一条");next.Click+=async(_,_)=>await Safe(()=>Step(1));Place(next,2);
        time.FontSize=11;time.Foreground=muted;time.VerticalAlignment=VerticalAlignment.Center;time.HorizontalAlignment=HorizontalAlignment.Center;time.Text="00:00 / 00:00";Place(time,3);
        var mute=IconButton(PackIconKind.VolumeHigh,"静音 / 恢复声音");mute.Click+=(_,_)=>{player?.Command("cycle","mute");((PackIcon)mute.Content).Kind=player?.Text("mute")=="yes"?PackIconKind.VolumeOff:PackIconKind.VolumeHigh;};Place(mute,4);
        var expand=IconButton(PackIconKind.OpenInNew,"在主窗口观看");expand.Click+=async(_,_)=>{if(current is {} item){Save();Pause();await Safe(()=>openMain(item));}};Place(expand,5);controls.Children.Add(actions);Grid.SetRow(controls,2);root.Children.Add(controls);
        timeline.SeekRequested+=position=>player?.Command("seek",position.ToString(System.Globalization.CultureInfo.InvariantCulture),"absolute+exact");
        var queueHeader=new DockPanel{Margin=new(20,10,16,8)};refresh=IconButton(PackIconKind.Refresh,"刷新稍后再看");refresh.Click+=async(_,_)=>await LoadPage(true);DockPanel.SetDock(refresh,Dock.Right);queueHeader.Children.Add(refresh);count.FontSize=14;count.FontWeight=FontWeights.SemiBold;count.VerticalAlignment=VerticalAlignment.Center;queueHeader.Children.Add(count);Grid.SetRow(queueHeader,3);root.Children.Add(queueHeader);
        var listHost=new Grid{Margin=new(10,0,8,12)};listHost.RowDefinitions.Add(new(){Height=GridLength.Auto});listHost.RowDefinitions.Add(new());scroll=new SmoothScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,ShowBackToTop=false};rows=new(()=>scroll,Row,item=>(item.Video.Aid,item.Video.Title,current?.Video.Aid==item.Video.Aid),88);scroll.Content=rows;Grid.SetRow(scroll,1);listHost.Children.Add(scroll);
        notice.FontSize=13;notice.Foreground=muted;notice.TextWrapping=TextWrapping.Wrap;notice.TextAlignment=TextAlignment.Center;notice.Margin=new(20);notice.VerticalAlignment=VerticalAlignment.Center;notice.Visibility=Visibility.Collapsed;listHost.Children.Add(notice);
        loading=new(accent){Width=22,Height=22,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Visibility=Visibility.Collapsed};listHost.Children.Add(loading);Grid.SetRow(listHost,4);root.Children.Add(listHost);
        scroll.ScrollChanged+=async(_,e)=>{if(e.VerticalChange>0&&more&&scroll.ScrollableHeight-scroll.VerticalOffset<180)await LoadPage();};
        root.SizeChanged+=(_,_)=>{stage.Height=Math.Min(Math.Max(1,root.ActualWidth-32)*9/16,Math.Max(120,root.ActualHeight*.36));};
        stageContent.SizeChanged+=(_,e)=>stageContent.Clip=new RectangleGeometry(new Rect(e.NewSize),12,12);
        Closing+=(_,_)=>Release();
        tick.Tick+=(_,_)=>Poll();PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Space){TogglePause();e.Handled=true;}else if(e.Key==Key.Escape){Close();e.Handled=true;}};
        Render();
    }
    private Button IconButton(PackIconKind kind,string label,double width=32)
    {
        var button=new Button{Content=new PackIcon{Kind=kind,Width=20,Height=20},Width=width,Height=32,Padding=new(4),Style=(Style)Application.Current.FindResource("PlainButton"),Foreground=ink,ToolTip=label};AutomationProperties.SetName(button,label);return button;
    }
    internal Task Initialize()=>InitialLoad=LoadPage(true);
    private async Task Safe(Func<Task> operation){try{await operation();}catch(OperationCanceledException){}catch(Exception error){notice.Text=error.Message;notice.Visibility=Visibility.Visible;if(player==null){poster.Visibility=posterPlay.Visibility=Visibility.Visible;if(current is {} item)_=LoadPoster(item,lifetime.Token);}}}
    internal async Task LoadPage(bool reset=false)
    {
        if(busy||closed)return;busy=true;refresh.IsEnabled=false;var ct=lifetime.Token;
        var slow=ShowSlowLoading(ct);
        try
        {
            var response=await fetch(reset?1:page+1,ct);ct.ThrowIfCancellationRequested();if(reset){items.Clear();page=0;scroll.ScrollToTop();}page++;total=response.Total;more=response.HasMore;
            foreach(var item in response.Items)if(items.All(v=>v.Video.Aid!=item.Video.Aid))items.Add(item);
        notice.Visibility=Visibility.Collapsed;if(current==null&&items.FirstOrDefault(v=>!v.Unavailable) is {} first)SelectPoster(first);Render();
            if(items.Count==0){notice.Text="还没有添加视频";notice.Visibility=Visibility.Visible;}
        }
        catch(OperationCanceledException){}catch(Exception error){notice.Text=error.Message;notice.Visibility=Visibility.Visible;}
        finally{busy=false;refresh.IsEnabled=true;loading.Visibility=Visibility.Collapsed;}
    }
    private async Task ShowSlowLoading(CancellationToken ct){try{await Task.Delay(550,ct);if(busy&&!closed)loading.Visibility=Visibility.Visible;}catch(OperationCanceledException){}}
    private async Task LoadPoster(WatchLaterItem item,CancellationToken ct)
    {
        try{var image=await images.Get(item.Video.Cover,640,ct);if(!ct.IsCancellationRequested&&current?.Video.Aid==item.Video.Aid&&player==null)poster.Source=image;}catch(OperationCanceledException){}
    }
    private void SelectPoster(WatchLaterItem item)
    {
        current=item;poster.Source=null;poster.Visibility=posterPlay.Visibility=Visibility.Visible;timeline.Maximum=Math.Max(1,item.Video.Duration);timeline.Value=item.Progress<0?item.Video.Duration:Math.Max(0,item.Progress);time.Text=BilibiliClient.DurationLabel(timeline.Value)+" / "+BilibiliClient.DurationLabel(item.Video.Duration);_=LoadPoster(item,lifetime.Token);
    }
    private void Render()
    {
        stage.Visibility=controlBar.Visibility=current==null?Visibility.Collapsed:Visibility.Visible;
        rows.SetItems(items.ToArray());var index=current==null?-1:items.FindIndex(v=>v.Video.Aid==current.Video.Aid);count.Text=total>0?$"播放队列  ·  {(index>=0?index+1:0)} / {total}":"播放队列";
        previous.IsEnabled=index>0;next.IsEnabled=index>=0&&(index+1<items.Count||more);play.IsEnabled=posterPlay.IsEnabled=current!=null;
    }
    private FrameworkElement Row(WatchLaterItem item)
    {
        var video=item.Video;var grid=new Grid{Margin=new(8)};grid.ColumnDefinitions.Add(new(){Width=new GridLength(104)});grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=new GridLength(28)});
        var cover=new Grid{Width=104,Height=59,Background=surface,ClipToBounds=true};var image=new Image{Stretch=Stretch.UniformToFill};cover.Children.Add(image);CancellationTokenSource? thumbnail=null;
        image.Loaded+=async(_,_)=>{if(closed)return;thumbnail?.Cancel();thumbnail?.Dispose();thumbnail=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);await LoadThumbnail(image,video.Cover,thumbnail.Token);};
        image.Unloaded+=(_,_)=>{thumbnail?.Cancel();thumbnail?.Dispose();thumbnail=null;image.Source=null;};cover.SizeChanged+=(_,e)=>cover.Clip=new RectangleGeometry(new Rect(e.NewSize),8,8);
        var duration=new TextBlock{Text=BilibiliClient.DurationLabel(video.Duration),FontSize=10,Foreground=Brushes.White,Background=new SolidColorBrush(Color.FromArgb(175,0,0,0)),Padding=new(3,1,3,1),VerticalAlignment=VerticalAlignment.Bottom,HorizontalAlignment=HorizontalAlignment.Right,Margin=new(3)};cover.Children.Add(duration);grid.Children.Add(cover);
        var words=new Grid{Margin=new(12,0,4,0)};words.RowDefinitions.Add(new(){Height=new GridLength(40)});words.RowDefinitions.Add(new(){Height=new GridLength(19)});
        var title=new TextBlock{Text=video.Title,FontSize=13,FontWeight=FontWeights.Medium,Foreground=item.Unavailable?muted:ink,TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis,LineHeight=19,LineStackingStrategy=LineStackingStrategy.BlockLineHeight,ToolTip=video.Title};words.Children.Add(title);
        var author=new TextBlock{Text=item.Unavailable?"视频已失效":video.Creator,FontSize=11,Foreground=muted,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Bottom};Grid.SetRow(author,1);words.Children.Add(author);Grid.SetColumn(words,1);grid.Children.Add(words);
        var delete=IconButton(PackIconKind.Close,"从稍后再看移除",28);delete.Height=28;delete.VerticalAlignment=VerticalAlignment.Center;delete.Visibility=Visibility.Hidden;Grid.SetColumn(delete,2);grid.Children.Add(delete);delete.Click+=async(_,e)=>{e.Handled=true;delete.IsEnabled=false;try{await remove(video);}catch(Exception error){notice.Text=error.Message;notice.Visibility=Visibility.Visible;}finally{delete.IsEnabled=true;}};
        var row=new Button{Content=grid,Padding=new(0),Margin=new(2,2,2,4),Height=79,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch,Background=current?.Video.Aid==video.Aid?selected:Brushes.Transparent,Style=(Style)Application.Current.FindResource("PlainButton")};AutomationProperties.SetName(row,video.Title);row.MouseEnter+=(_,_)=>delete.Visibility=Visibility.Visible;row.MouseLeave+=(_,_)=>{if(!row.IsKeyboardFocusWithin)delete.Visibility=Visibility.Hidden;};row.GotKeyboardFocus+=(_,_)=>delete.Visibility=Visibility.Visible;row.LostKeyboardFocus+=(_,_)=>{if(!row.IsMouseOver)delete.Visibility=Visibility.Hidden;};row.Click+=async(_,e)=>{if(ReferenceEquals(e.OriginalSource,row)&&!item.Unavailable)await Safe(()=>Play(item));};return row;
    }
    private async Task LoadThumbnail(Image target,string url,CancellationToken ct)
    {
        try{var bitmap=await images.Get(url,240,ct);if(!ct.IsCancellationRequested&&target.IsLoaded)target.Source=bitmap;}catch(OperationCanceledException){}
    }
    internal async Task Play(WatchLaterItem item,bool resume=true)
    {
        if(item.Unavailable||closed)return;Save();beforePlay();StopPlayback();current=item;Render();var stamp=++revision;playback=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);var ct=playback.Token;
        var info=await api.GetDetail(item.Video.Bvid,ct);ct.ThrowIfCancellationRequested();if(stamp!=revision)return;
        parts=info.Parts;var saved=resume?local.GetProgress(item.Video.Bvid):null;var cid=saved?.Cid??item.Cid;part=info.Parts.FirstOrDefault(v=>v.Cid==cid)??info.Parts[0];current=item with{Cid=part.Cid,Video=info.Video};var start=saved?.Cid==part.Cid?saved.Position:item.Cid==part.Cid?Math.Max(0,item.Progress):0;if(start>=part.Duration-10)start=0;
        var stream=await api.GetPlayStream(item.Video.Bvid,part.Cid,32,ct);ct.ThrowIfCancellationRequested();if(stamp!=revision)return;await MpvHost.WaitForShutdownCapacityAsync(ct);ct.ThrowIfCancellationRequested();
        player=new(preferences.HardwareDecoding);stageContent.Children.Insert(0,player);poster.Visibility=posterPlay.Visibility=Visibility.Collapsed;stage.UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Loaded);ct.ThrowIfCancellationRequested();
        var host=player;var dmSettings=new DanmakuPreferences{FontScale=.7,Area=.4,PersonMask=false,Advanced=false,Opacity=preferences.Danmaku.Opacity,BlockedWords=preferences.Danmaku.BlockedWords,SyncedFilters=preferences.Danmaku.SyncedFilters.ToList()};
        var renderer=new DanmakuSurface(()=>(host.Number("time-pos"),host.Text("pause")=="yes"||host.Text("paused-for-cache")=="yes",host.Number("speed")),dmSettings,()=>!host.CanInteract||host.Seeking);
        host.StateChanged+=name=>{if(name=="seeking")renderer.Wake();};
        overlay=new(this,stage,new Border{Height=0},TogglePause,forward=>host.Command("seek",forward?"10":"-10","relative+exact"),e=>{if(e.Key==Key.Space){TogglePause();e.Handled=true;}},renderer,(_,_)=>Task.CompletedTask,_=>Task.CompletedTask,_=>Task.FromResult(new DanmakuLikeState(false,0)),allowDanmakuActions:false);
        void UpdatePresentation()
        {
            if(!ReferenceEquals(player,host))return;
            overlay?.SetPlaybackPresentation(host.Presentation,host.LoadingMessage,host.LastNativeError,async()=>await Safe(()=>Play(current??item)));
            timeline.IsEnabled=play.IsEnabled=host.CanInteract;
        }
        host.PresentationChanged+=UpdatePresentation;UpdatePresentation();
        timeline.Maximum=part.Duration;preview=new(timeline,overlay,api,images,info.Video.Bvid,part.Cid);
        host.PlaybackEnded+=endedRevision=>
        {
            _=Dispatcher.BeginInvoke(async()=>
            {
                if(ReferenceEquals(player,host)&&endedRevision==host.LoadRevision&&host.CompletionPending)await Safe(CompleteVideo);
            });
        };
        host.PlaybackFailed+=message=>{notice.Text=message;notice.Visibility=Visibility.Visible;};host.Load(stream,start);play.Content=new PackIcon{Kind=PackIconKind.Pause,Width=20,Height=20};tick.Start();Poll();
    }
    private Task CompleteVideo()=>current is {} item&&parts.FirstOrDefault(p=>p.Page>part?.Page) is {} following?Play(item with{Cid=following.Cid,Progress=0},resume:false):Step(1);
    internal void Pause(){player?.Set("pause","yes");play.Content=new PackIcon{Kind=PackIconKind.Play,Width=20,Height=20};Save();}
    internal void SuspendPlayback(){Save(true);StopPlayback();poster.Visibility=posterPlay.Visibility=Visibility.Visible;play.Content=new PackIcon{Kind=PackIconKind.Play,Width=20,Height=20};if(current is {} item)_=LoadPoster(item,lifetime.Token);}
    private void TogglePause(){if(player?.CanInteract!=true)return;player.Command("cycle","pause");play.Content=new PackIcon{Kind=player.Text("pause")=="yes"?PackIconKind.Play:PackIconKind.Pause,Width=20,Height=20};Save();}
    private async Task Step(int direction)
    {
        var index=items.FindIndex(v=>v.Video.Aid==current?.Video.Aid);if(index<0)return;
        for(int nextIndex=index+direction;nextIndex>=0;nextIndex+=direction)
        {
            if(nextIndex>=items.Count){if(!more)return;await LoadPage();if(nextIndex>=items.Count&&!more)return;}
            if(nextIndex<items.Count&&!items[nextIndex].Unavailable){await Play(items[nextIndex]);return;}
        }
    }
    private void Poll()
    {
        if(player?.MediaReady!=true||current==null||part==null)return;var position=player.Number("time-pos");if(!player.Seeking)timeline.SetPlaybackPosition(position);time.Text=BilibiliClient.DurationLabel(position)+" / "+BilibiliClient.DurationLabel(part.Duration);
        if(DateTime.UtcNow-lastSave>TimeSpan.FromSeconds(5)){lastSave=DateTime.UtcNow;Save();}
        var segment=(int)(position/360)+1;if(segment!=requestedSegment){requestedSegment=segment;_=LoadDanmaku(part.Cid,segment,revision,playback!.Token);}
    }
    private async Task LoadDanmaku(long cid,int segment,int stamp,CancellationToken ct)
    {
        try{var messages=await api.GetDanmakuSegment(cid,segment,ct);ct.ThrowIfCancellationRequested();if(stamp==revision)overlay?.Danmaku.SetWindowMessages(messages);}catch(OperationCanceledException){}catch(Exception error) when(error is System.Net.Http.HttpRequestException or BilibiliException or System.IO.IOException or TimeoutException){}
    }
    internal void ApplyMutation(VideoCard video,bool add)
    {
        if(closed)return;var present=items.Any(v=>v.Video.Aid==video.Aid);if(add&&!present){items.Insert(0,new(video,0,0));total++;}else if(!add&&present){items.RemoveAll(v=>v.Video.Aid==video.Aid);total=Math.Max(0,total-1);}
        if(!add&&current?.Video.Aid==video.Aid){Save();StopPlayback();current=null;if(items.FirstOrDefault(v=>!v.Unavailable) is {} first)SelectPoster(first);else{poster.Source=null;poster.Visibility=posterPlay.Visibility=Visibility.Visible;}}
        page=Math.Max(0,page-1);more=true;Render();if(items.Count==0){notice.Text="还没有添加视频";notice.Visibility=Visibility.Visible;}
    }
    private void Save(bool final=false)
    {
        if(current==null||part==null||player?.Ready!=true||!canReport())return;var position=player.Number("time-pos");if(position<=0)return;var video=current.Video;
        var progress=new WatchProgress(video.Bvid,part.Cid,position,part.Duration,DateTimeOffset.UtcNow,video.Title,video.Cover,video.Creator,video.Published,false,video.Aid,video.Mid);local.SaveProgress(progress);
        if(!reporting&&(final||DateTime.UtcNow-lastReport>TimeSpan.FromSeconds(30)))
        {
            reporting=true;lastReport=DateTime.UtcNow;HistorySync=Report();async Task Report(){try{await api.ReportHistory(video.Bvid,progress.Cid,progress.Position,aid:video.Aid,duration:part.Duration);}catch(Exception){}finally{reporting=false;}}
        }
    }
    private void StopPlayback(){revision++;tick.Stop();play.IsEnabled=true;timeline.IsEnabled=false;playback?.Cancel();playback?.Dispose();playback=null;preview?.Dispose();preview=null;overlay?.Dispose();overlay=null;if(player!=null){stageContent.Children.Remove(player);player.Dispose();player=null;}requestedSegment=0;part=null;}
    private void Release(){if(closed)return;closed=true;Save(true);lifetime.Cancel();StopPlayback();poster.Source=null;rows.SetItems([]);lifetime.Dispose();}
}
