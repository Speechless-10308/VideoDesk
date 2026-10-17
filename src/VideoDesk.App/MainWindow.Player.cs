using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using VideoDesk.Core;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private PlayerOverlay? overlay;
    private StoryboardPreview? storyboardPreview;
    private void ResetStoryboardPreview()
    {
        storyboardPreview?.Dispose();storyboardPreview=null;
        if(seek is {} timeline&&overlay!=null&&detail!=null&&part!=null)storyboardPreview=new(timeline,overlay,api,images,detail.Video.Bvid,part.Cid);
        ResetPlayerFeatures();
    }
    private Button? qualityButton,speedButton;
    private Slider? volumeSlider;
    private Button? videoTabButton,videoCommentTabButton;
    private bool danmakuEnabled=true,interactionBusy,followingCreator;
    private IReadOnlyList<DanmakuItem> danmakuMessages=[];
    private long maskCid;
    private VideoAccountState videoState=new(false,0,false);
    private DateTimeOffset lastCloudReport;
    private bool reportingHistory;
    private Task historySync=Task.CompletedTask;
    private WatchProgress? lastWatched;
    private long watchStartedAt,lastPlaybackTick;
    private double playedSeconds;
    private bool closingApproved;
    private bool closingStarted;
    private Button? openingVideoBack;
    private PlayerLoadingView? openingVideoFeedback;
    private async Task OpenVideo(string bvid,double? startOverride=null,bool firstPart=false,bool rememberPage=true,long? preferredCid=null)
    {
        if(player?.Ready==true&&playerPage!=null&&playbackCollection?.Videos.Any(video=>video.Bvid==bvid)==true)
        {
            await OpenCollectionVideo(bvid,startOverride,firstPart,rememberPage,preferredCid);return;
        }
        watchLaterWindow?.SuspendPlayback();var supplied=creatorScroll?.Visibility==Visibility.Visible&&content.Children.Contains(creatorScroll)&&creatorState?.Collection is {} selected?new VideoCollectionContext(selected.Id,creatorState.Profile.Mid,selected.Title,creatorState.Videos.ToArray(),creatorState.Total,selected.Series):playbackCollection?.Videos.Any(a=>a.Bvid==bvid)==true?playbackCollection:null;
        var sourceCollectionPage=creatorState?.Collection!=null&&creatorScroll?.Visibility==Visibility.Visible?creatorState.Page:supplied!=null?collectionPage:1;
        var ct=BeginPage(rememberPage);
        var opening=new Grid();opening.RowDefinitions.Add(new(){Height=GridLength.Auto});opening.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});
        var back=BackButton();back.HorizontalAlignment=HorizontalAlignment.Left;back.Margin=new(0,0,0,16);back.Click+=async(_,_)=>await Run(GoBack);openingVideoBack=back;opening.Children.Add(back);
        var feedback=new PlayerLoadingView();openingVideoFeedback=feedback;
        var stage=new Border{Background=Brushes.Black,CornerRadius=new(8),Child=feedback};Grid.SetRow(stage,1);opening.Children.Add(stage);content.Children.Add(opening);
        try
        {
        var fetched=await api.GetDetail(bvid,ct);ct.ThrowIfCancellationRequested();detail=fetched;collectionPage=sourceCollectionPage;playbackCollection=supplied??fetched.Collection;related.Clear();danmakuMessages=[];videoState=new(false,0,false);followingCreator=false;watchStartedAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds();playedSeconds=0;lastPlaybackTick=Stopwatch.GetTimestamp();
        part=detail.Parts.FirstOrDefault()??throw new InvalidDataException("这个视频没有可播放分集");
        if(preferredCid is {} wanted)part=detail.Parts.FirstOrDefault(p=>p.Cid==wanted)??part;var progress=local.GetProgress(bvid);if(progress!=null&&!firstPart)part=detail.Parts.FirstOrDefault(p=>p.Cid==progress.Cid)??part;
        await MpvHost.WaitForShutdownCapacityAsync(ct);ct.ThrowIfCancellationRequested();content.Children.Remove(opening);BuildPlayer();FocusManager.SetFocusedElement(this,playerFrame);Keyboard.Focus(playerFrame);searchPopup.IsOpen=false;var start=progress!=null&&progress.Cid==part.Cid&&progress.Position<progress.Duration-10?progress.Position:0;if(startOverride.HasValue)start=startOverride.Value;
        var host=player!;host.PrepareLoad(start);var openingPart=part;var revision=host.LoadRevision;
        StartDanmakuSession(openingPart,ct);var first=FirstDanmaku(openingPart,start,ct);
        try
        {
            var stream=await api.GetPlayStream(bvid,openingPart.Cid,preferences.DefaultQuality,ct:ct);ct.ThrowIfCancellationRequested();
            if(!ReferenceEquals(player,host)||host.LoadRevision!=revision||part?.Cid!=openingPart.Cid)return;
            PopulateQualities(stream);host.Load(stream,start);
        }
        catch(Exception) when(ct.IsCancellationRequested||!ReferenceEquals(player,host)||host.LoadRevision!=revision||streamOpen!=null){return;}
        _=LoadRelatedBoth(bvid,ct);_=LoadAccountActions(bvid,fetched.Video.Aid,fetched.Video.Mid,ct);_=LoadDanmaku(openingPart,ct,first,firstIndex:(int)(start/360)+1);_=RefreshWatchMetadata(openingPart,ct);_=LoadVideoTags(bvid,fetched.Video.Aid,ct);
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){}
        catch(Exception error)
        {
            if(ct.IsCancellationRequested)return;
            EndDanmakuSession();overlay?.Dispose();overlay=null;player?.Dispose();player=null;detail=null;part=null;playerPage=null;
            content.Children.Clear();content.Children.Add(opening);
            feedback.Failed("",error.Message,()=>Run(()=>OpenVideo(bvid,startOverride,firstPart,rememberPage,preferredCid)));
        }
    }
    private void BuildPlayer()
    {
        SetPageSurface(playback:true);playerPage=new Grid();playerPage.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});playerPage.ColumnDefinitions.Add(new(){Width=new GridLength(400)});var activePage=playerPage;activePage.SizeChanged+=(_,e)=>{if(!fullscreen&&ReferenceEquals(activePage,playerPage))activePage.ColumnDefinitions[1].Width=new GridLength(Math.Clamp(e.NewSize.Width*.3,300,400));};
        leftPlayer=new Grid{Margin=new Thickness(0,0,14,0),Background=CanvasBrush,VerticalAlignment=VerticalAlignment.Stretch};for(int i=0;i<3;i++)leftPlayer.RowDefinitions.Add(new(){Height=i==2?new GridLength(1,GridUnitType.Star):GridLength.Auto});playerPage.Children.Add(leftPlayer);
        RoundClip(leftPlayer,24);var titlePanel=new StackPanel{Margin=new Thickness(28,18,16,16)};playerTitle=titlePanel;RefreshPlayerTitle();leftPlayer.Children.Add(titlePanel);
        player=new MpvHost(preferences.HardwareDecoding);var host=player;host.PlaybackFailed+=message=>{if(host.Presentation!=PlayerPresentation.Failed)Notify(message);};host.FileLoaded+=()=>{if(player==host){timer.Start();PollPlayer(false);}};host.PlaybackEnded+=revision=>QueuePlaybackCompletion(host,revision);
        host.PresentationChanged+=()=>UpdatePlayerPresentation(host);
        timer.Start();host.StateChanged+=name=>OnPlayerStateChanged(host,name);
        var frame=new Grid{Background=Brushes.Black};frame.Children.Add(player);playerFrame=new Border{Margin=new Thickness(16,0,16,0),Child=frame,Background=Brushes.Black,CornerRadius=new CornerRadius(10),ClipToBounds=true,Focusable=true};InputMethod.SetIsInputMethodEnabled(playerFrame,false);Grid.SetRow(playerFrame,1);leftPlayer.Children.Add(playerFrame);
        var layoutLeft=leftPlayer;var nativeFrame=playerFrame;var titleArea=playerTitle;
        leftPlayer.SizeChanged+=(_,_)=>{if(ReferenceEquals(leftPlayer,layoutLeft))UpdatePlayerHeight();};titleArea.SizeChanged+=(_,_)=>{if(ReferenceEquals(leftPlayer,layoutLeft))UpdatePlayerHeight();};
        var playerControls=BuildControls();playerFrame.Loaded+=(_,_)=>
        {
            if(player!=host||!nativeFrame.IsLoaded||detail==null||part==null)return;
            overlay?.Dispose();var renderer=new DanmakuSurface(()=>host.Ready&&host.Number("video-params/w")>0?(host.Number("time-pos"),host.Text("pause")=="yes"||host.Text("paused-for-cache")=="yes",host.Number("speed")):(0,true,1),preferences.Danmaku,()=>!host.CanInteract||host.Seeking);
            renderer.MeasureFrames=App.Diagnostics.Enabled;
            overlay=new(this,playerFrame,playerControls,TogglePause,forward=>player?.Command("seek",forward?"10":"-10","relative+exact"),e=>HandleKey(this,e),renderer,
                async(item,liked)=>{if(!RequireLogin())throw new InvalidOperationException("请先登录");await api.LikeDanmaku(part!.Cid,item.Id,liked);},ReportHoveredDanmaku,item=>api.GetDanmakuLikeState(part!.Cid,item.Id,navigation.Token));
            overlay.ConfigureWindowDrag(()=>fullscreen,ToggleFullscreen);UpdatePlayerPresentation(host);ResetStoryboardPreview();
            renderer.Mask=new(api,navigation.Token);maskCid=part!.Cid;_ = renderer.Mask.Initialize(detail!.Video.Aid,part.Cid);renderer.SetMessages(danmakuMessages);renderer.SettingsChanged();renderer.SetEnabled(danmakuEnabled);FocusManager.SetFocusedElement(this,playerFrame);Keyboard.Focus(playerFrame);
        };
        danmakuRow=new Border{Height=0};Grid.SetRow(danmakuRow,2);leftPlayer.Children.Add(danmakuRow);
        var sideLayout=new DockPanel{Margin=new Thickness(18,12,18,16)};var switcher=new Grid{Height=44,Margin=new Thickness(0,0,0,20),Background=Surface};switcher.ColumnDefinitions.Add(new());switcher.ColumnDefinitions.Add(new());var video=Button("视频");var comments=Button("评论");video.Background=Pink;video.Foreground=Brushes.White;comments.Background=Surface;
        videoTabButton=video;videoCommentTabButton=comments;video.Click+=async(_,_)=>await SelectVideoComments(false);
        comments.Click+=async(_,_)=>await Run(()=>SelectVideoComments(true));switcher.Children.Add(video);Grid.SetColumn(comments,1);switcher.Children.Add(comments);DockPanel.SetDock(switcher,Dock.Top);sideLayout.Children.Add(switcher);
        commentComposer=BuildCommentComposer();commentComposer.Visibility=Visibility.Collapsed;DockPanel.SetDock(commentComposer,Dock.Bottom);sideLayout.Children.Add(commentComposer);
        var sideContent=new Grid();videoSidebar=new();commentSidebar=new();videoScroll=new SmoothScrollViewer{Content=videoSidebar,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};commentScroll=new SmoothScrollViewer{Content=commentSidebar,ShowBackToTop=false,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};commentHeader=new Border{Margin=new Thickness(0,0,8,18)};commentView=new DockPanel{Visibility=Visibility.Collapsed};commentView.Children.Add(commentHeader);DockPanel.SetDock(commentHeader,Dock.Top);commentView.Children.Add(commentScroll);sideContent.Children.Add(videoScroll);sideContent.Children.Add(commentView);sideLayout.Children.Add(sideContent);var sideFrame=new Border{Child=sideLayout,Background=CanvasBrush,CornerRadius=new CornerRadius(24)};side=sideFrame;Grid.SetColumn(sideFrame,1);playerPage.Children.Add(sideFrame);content.Children.Add(playerPage);BuildVideoSidebar();
    }
    private async Task SelectVideoComments(bool selected)
    {
        videoTabButton!.Background=selected?Surface:Pink;videoTabButton.Foreground=selected?Muted:Brushes.White;videoCommentTabButton!.Background=selected?Pink:Surface;videoCommentTabButton.Foreground=selected?Brushes.White:Muted;
        videoScroll!.Visibility=selected?Visibility.Collapsed:Visibility.Visible;commentView!.Visibility=selected?Visibility.Visible:Visibility.Collapsed;commentComposer!.Visibility=selected?Visibility.Visible:Visibility.Collapsed;if(selected&&commentSidebar!.Children.Count==0)await LoadComments();
    }
    private FrameworkElement BuildControls()
    {
        var panel=new DockPanel{LastChildFill=true,Margin=new Thickness(12,12,12,10)};
        var timeline=new PlaybackTimeline{Minimum=0,Maximum=part!.Duration,Height=40,Foreground=Pink};seek=timeline;
        var host=player;AutomationProperties.SetName(timeline,"播放进度");DockPanel.SetDock(timeline,Dock.Top);panel.Children.Add(timeline);
        timeline.SeekRequested+=position=>
        {
            if(!ReferenceEquals(player,host)||!ReferenceEquals(seek,timeline))return;
            host?.Command("seek",position.ToString(CultureInfo.InvariantCulture),"absolute+exact");
        };
        var row=new Grid{Height=36};foreach(var width in new[]{new GridLength(32),GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto,new GridLength(46),new GridLength(32)})row.ColumnDefinitions.Add(new(){Width=width});
        void Place(FrameworkElement control,int column){control.VerticalAlignment=VerticalAlignment.Center;if(control is Button button){button.Height=32;button.FontSize=13;}Grid.SetColumn(control,column);row.Children.Add(control);}
        pause=IconButton(PackIconKind.Pause,"播放或暂停",32);pause.Foreground=Brushes.White;pause.Click+=(_,_)=>TogglePause();Place(pause,0);
        timeText=Text("00:00 / "+BilibiliClient.DurationLabel(part.Duration),11);timeText.Foreground=Brushes.White;timeText.Margin=new Thickness(6,0,10,0);Place(timeText,1);
        var tools=new StackPanel{Orientation=Orientation.Horizontal,Height=32};chapterCaption=new TextBlock{Text="章节",Style=(Style)Application.Current.FindResource("PlayerControlLabel"),MaxWidth=100,TextTrimming=TextTrimming.CharacterEllipsis};
        var chapterContent=new StackPanel{Orientation=Orientation.Horizontal};chapterContent.Children.Add(chapterCaption);var chapterArrow=Icon(PackIconKind.ChevronDown,12);chapterArrow.VerticalAlignment=VerticalAlignment.Center;chapterArrow.Margin=new(3,0,0,0);chapterContent.Children.Add(chapterArrow);
        chapterButton=Button("");chapterButton.Foreground=Brushes.White;chapterButton.Content=chapterContent;chapterButton.Padding=new(4,0,8,0);chapterButton.Height=32;chapterButton.Visibility=Visibility.Collapsed;chapterButton.ToolTip="跳转章节";chapterButton.Click+=(_,_)=>ShowVideoChapters();tools.Children.Add(chapterButton);tools.Children.Add(BuildDanmakuTools());Place(tools,2);var dm=BuildDanmakuInput();dm.Height=32;Place(dm,3);
        var volumeRow=new StackPanel{Orientation=Orientation.Horizontal,Height=32,Margin=new Thickness(4,0,6,0)};var volumeIcon=IconButton(PackIconKind.VolumeHigh,"静音 / 恢复声音",28);volumeIcon.Height=32;volumeIcon.VerticalAlignment=VerticalAlignment.Center;volumeIcon.Foreground=Brushes.White;volumeIcon.Click+=(_,_)=>player?.Command("cycle","mute");volumeRow.Children.Add(volumeIcon);
        var volume=new Slider{Minimum=0,Maximum=100,Value=60,Width=66,Height=24,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(3,0,5,0),Foreground=Pink};volumeSlider=volume;AutomationProperties.SetName(volume,"音量");volume.ValueChanged+=(_,_)=>player?.Set("volume",volume.Value.ToString(CultureInfo.InvariantCulture));volumeRow.Children.Add(volume);Place(volumeRow,4);
        quality=new ComboBox();qualityButton=PlayerTextButton("清晰度");qualityButton.Padding=new Thickness(8,0,8,0);qualityButton.Click+=(_,_)=>SelectionMenu(qualityButton,quality.Items.OfType<Quality>().Select(q=>(q.Label,q.Id.ToString())),(quality.SelectedItem as Quality)?.Id.ToString()??"",value=>Run(()=>ChangeQuality(int.Parse(value))));Place(qualityButton,5);
        speedButton=PlayerTextButton("1×");speedButton.Width=46;speedButton.Padding=new Thickness(4,0,4,0);speedButton.Click+=(_,_)=>SelectionMenu(speedButton,new[]{.5,1,1.25,1.5,2}.Select(a=>(a.ToString("0.##",CultureInfo.InvariantCulture)+"×",a.ToString(CultureInfo.InvariantCulture))),player?.Number("speed").ToString(CultureInfo.InvariantCulture)??"1",async value=>{player?.Set("speed",value);speedButton.Content=value+"×";await Task.CompletedTask;});Place(speedButton,6);
        var full=IconButton(PackIconKind.Fullscreen,"全屏 / 退出全屏",32);full.Foreground=Brushes.White;full.Click+=(_,_)=>ToggleFullscreen();Place(full,7);panel.Children.Add(row);
        panel.SizeChanged+=(_,e)=>{volume.Visibility=e.NewSize.Width<700?Visibility.Collapsed:Visibility.Visible;dm.Visibility=e.NewSize.Width<820?Visibility.Collapsed:Visibility.Visible;timeText.Visibility=e.NewSize.Width<500?Visibility.Collapsed:Visibility.Visible;};
        return new Border{Child=panel,Height=104,Background=new LinearGradientBrush(Color.FromArgb(0,0,0,0),Color.FromArgb(225,0,0,0),90)};
    }
    private static Button PlayerTextButton(string label)=>new(){Content=label,Style=(Style)Application.Current.FindResource("PlayerTextButton")};
    private void SelectionMenu(Button target,IEnumerable<(string Label,string Value)> values,string selected,Func<string,Task> choose)
    {
        var menu=new StackPanel{Width=180};
        foreach(var (label,value) in values){var item=Button((value==selected?"✓  ":"    ")+label);item.Height=40;item.HorizontalContentAlignment=HorizontalAlignment.Left;item.Padding=new Thickness(12,8,12,8);item.Foreground=value==selected?Pink:Ink;item.Background=value==selected?PalePink:Brushes.Transparent;item.Click+=async(_,_)=>{overlay?.ClosePanel();await choose(value);overlay?.Wake();};menu.Children.Add(item);}
        overlay?.ShowPanel(menu,target,CanvasBrush,Surface);
    }
    private void PopulateQualities(PlayStream stream){quality!.ItemsSource=stream.Qualities;quality.SelectedItem=stream.Qualities.FirstOrDefault(q=>q.Id==stream.Quality);if(qualityButton!=null)qualityButton.Content=stream.Qualities.FirstOrDefault(q=>q.Id==stream.Quality)?.Label??"清晰度";}
    private async Task ChangePart(VideoPart p,double start=0)
    {
        if(player is not {} host||detail==null||part==null||!detail.Parts.Any(item=>item.Cid==p.Cid))return;
        CancelCollectionOpen();var token=navigation.Token;var request=BeginStreamOpen(token);var ct=request.Token;
        var bvid=detail.Video.Bvid;var revision=host.LoadRevision;
        try
        {
            var stream=await api.GetPlayStream(bvid,p.Cid,preferences.DefaultQuality,ct:ct);ct.ThrowIfCancellationRequested();
            if(collectionOpen!=null||!ReferenceEquals(player,host)||host.LoadRevision!=revision||detail?.Video.Bvid!=bvid)return;
            SaveProgress();host.PrepareLoad(start);part=p;lastPlaybackTick=0;
            ResetStoryboardPreview();RefreshPartSelection();StartDanmakuSession(p,token);overlay?.Danmaku.SetMessages([]);
            seek?.SetHeatmap(null);seek?.SetBuffered([]);seek!.Maximum=Math.Max(1,p.Duration);seek.Value=start;
            PopulateQualities(stream);host.Load(stream,start);timer.Start();
            var first=FirstDanmaku(p,start,token);_=LoadDanmaku(p,token,first,firstIndex:(int)(start/360)+1);_=RefreshWatchMetadata(p,token);
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){}
        finally{EndStreamOpen(request);}
    }
    private async Task LoadDanmaku(VideoPart videoPart,CancellationToken ct,Task<IReadOnlyList<DanmakuItem>>? first=null,bool installed=false,int firstIndex=1)
    {
        if(first==null&&danmakuSessionReady&&danmakuSegments?.Cid==videoPart.Cid){RefreshDanmakuWindow();return;}
        try
        {
            if(overlay!=null&&(overlay.Danmaku.Mask==null||maskCid!=videoPart.Cid)){var renderer=overlay.Danmaku;maskCid=videoPart.Cid;renderer.Mask?.Dispose();renderer.Mask=new(api,ct);_ = renderer.Mask.Initialize(detail!.Video.Aid,videoPart.Cid);}
            if(!installed){var initial=await (first??FirstDanmaku(videoPart,0,ct));ct.ThrowIfCancellationRequested();if(part?.Cid!=videoPart.Cid)return;await InstallInitialDanmaku(initial,player?.Number("time-pos")??0,ct);}
            ct.ThrowIfCancellationRequested();if(part?.Cid!=videoPart.Cid||danmakuSegments?.Cid!=videoPart.Cid)return;
            danmakuSessionReady=true;RequestDanmakuWindow(Math.Max((firstIndex-1)*360,player?.Number("time-pos")??0));
        }
        catch(OperationCanceledException){}catch(Exception e){if(!ct.IsCancellationRequested)Notify("弹幕后续分段加载失败："+e.Message);}
    }
    private bool HasDanmakuTrack()=>overlay?.Danmaku.MessageCount>0;
    private void SetDanmakuVisibility(bool enabled){danmakuEnabled=enabled;overlay?.Danmaku.SetEnabled(enabled);UpdatePlayerFeatures(player?.Number("time-pos")??0);}
    private bool RequireLogin(){if(account?.LoggedIn==true)return true;Notify("请先点击左下角头像扫码登录");return false;}
    private async Task LoadAccountActions(string bvid,long aid,long mid,CancellationToken ct)
    {
        var revision=accountRevision;accountLoadRevision=revision;accountStateLoading=true;RefreshAccountControls();
        try
        {
            var state=api.GetVideoAccountState(bvid,aid,ct);var followed=api.IsFollowing(mid,ct);
            await Task.WhenAll(state,followed);ct.ThrowIfCancellationRequested();
            if(detail?.Video.Bvid!=bvid||revision!=accountRevision)return;
            videoState=state.Result;followingCreator=followed.Result;accountStateReady=true;
        }
        catch(OperationCanceledException){}catch(Exception e){if(!ct.IsCancellationRequested)Notify("账号状态："+e.Message);}
        finally{if(!ct.IsCancellationRequested&&revision==accountLoadRevision){accountStateLoading=false;RefreshAccountControls();}}
    }
    private void SaveProgress()
    {
        if(player?.MediaReady!=true||detail==null||part==null)return;var pos=player.Number("time-pos");if(pos<=0)return;var video=detail.Video;
        lastWatched=new(video.Bvid,part.Cid,pos,Math.Max(part.Duration,player.Number("duration")),DateTimeOffset.UtcNow,video.Title,video.Cover,video.Creator,video.Published,false,video.Aid,video.Mid,video.Level);local.SaveProgress(lastWatched);
        if(account?.LoggedIn==true&&!reportingHistory&&DateTimeOffset.UtcNow-lastCloudReport>TimeSpan.FromSeconds(30))
        {
            lastCloudReport=DateTimeOffset.UtcNow;reportingHistory=true;var value=lastWatched;var active=playedSeconds;var started=watchStartedAt;
            async Task Report(){try{await api.ReportHistory(video.Bvid,value.Cid,pos,aid:video.Aid,duration:value.Duration,playedSeconds:active,startedAt:started);}catch(Exception e){lastCloudReport=DateTimeOffset.UtcNow.AddMinutes(4);if(IsLoaded)Notify("B站历史同步失败，本地进度已保存："+e.Message);}finally{reportingHistory=false;}}historySync=Report();
        }
    }
    private async Task FlushCloudHistory()
    {
        var value=lastWatched;if(value==null||account?.LoggedIn!=true)return;lastWatched=null;var active=playedSeconds;var started=watchStartedAt;
        try{await historySync;await api.ReportHistory(value.Bvid,value.Cid,value.Position,aid:value.Aid,duration:value.Duration,playedSeconds:active,startedAt:started);}catch(Exception e){Debug.WriteLine("Final history sync: "+e.Message);}
    }
}












