using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using QRCoder;
using VideoDesk.Core;

namespace VideoDesk.App;

public sealed partial class MainWindow : Window
{
    private static readonly DependencyProperty ManagedImageProperty=DependencyProperty.RegisterAttached("ManagedImage",typeof(bool),typeof(MainWindow),new PropertyMetadata(false));
    private static readonly DependencyProperty RetainImagesOnHideProperty=DependencyProperty.RegisterAttached("RetainImagesOnHide",typeof(bool),typeof(MainWindow),new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.Inherits));
    private static readonly Brush Pink=new SolidColorBrush(Color.FromRgb(255,71,140));
    private static readonly Brush Ink=new SolidColorBrush(Color.FromRgb(32,35,43));
    private static readonly Brush CanvasBrush=new SolidColorBrush(Colors.White);
    private static readonly Brush HeaderBrush=new SolidColorBrush(Color.FromRgb(250,251,253));
    private static readonly Brush RailBrush=new SolidColorBrush(Color.FromRgb(247,248,250));
    private static readonly Brush PalePink=new SolidColorBrush(Color.FromRgb(255,237,244));
    private static readonly Brush Muted=new SolidColorBrush(Color.FromRgb(133,139,157));
    private static readonly Brush Surface=new SolidColorBrush(Color.FromRgb(244,246,249));
    private readonly CaptureStore captures=new(Path.Combine(App.Workspace,"captures"),enabled:App.Diagnostics.Enabled,background:App.Diagnostics.Enabled);
    private readonly LocalStore local=new(App.DataDirectory);
    private readonly BilibiliClient api;
    private readonly ImageCache images=new(Path.Combine(App.DataDirectory,"image-cache"));
    private readonly ClientPreferences preferences=ClientPreferences.Load(App.DataDirectory);
    private readonly Grid shell=new();
    private readonly Grid content=new();
    private readonly TextBox search=new();
    private readonly Popup searchPopup=new(){StaysOpen=true,AllowsTransparency=true,Placement=PlacementMode.Custom,CustomPopupPlacementCallback=(_,target,_)=>[new(new Point(0,target.Height+6),PopupPrimaryAxis.Horizontal)]};
    private readonly TextBlock status=Text("",13);
    private readonly Border statusBar=new();
    private readonly LoadingGlyph loading=new(Pink){Visibility=Visibility.Collapsed,Margin=new(12),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top};
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(500)};
    private readonly List<string> searchHistory=new();
    private readonly Button homeNav,historyNav,avatar;
    private FrameworkElement? header,rail;
    private CancellationTokenSource navigation=new();
    private DiscoveryMode homeMode=DiscoveryMode.Regular,searchMode=DiscoveryMode.Roaming,relatedMode=DiscoveryMode.Regular;
    private bool popular,searching;
    private string keyword="";
    private AccountInfo? account;
    private MpvHost? player;
    private VideoDetail? detail;
    private VideoPart? part;
    private PlaybackTimeline? seek;
    private TextBlock? timeText;
    private Button? pause;
    private ComboBox? quality;
    private bool fullscreen;
    private DateTimeOffset lastSave;
    private Grid? playerPage,leftPlayer;
    private Border? playerFrame;
    private FrameworkElement? playerTitle,danmakuRow,side;
    private StackPanel? videoSidebar,commentSidebar;
    private ScrollViewer? videoScroll,commentScroll;
    private readonly Dictionary<DiscoveryMode,IReadOnlyList<VideoCard>> related=new();
    private Rect normalBounds;
    private WindowState normalState;
    private bool normalTopmost;

    public MainWindow()
    {
        Foreground=Ink;
        base.Icon=System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/AppIcon.ico"));
        api=new(captures,local);local.LoadProgress();LoadHistory();homeMode=preferences.HomeMode;searchMode=preferences.SearchMode;relatedMode=preferences.RelatedMode;
        PreviewMouseDown+=(_,_)=>{if(searchPopup.IsOpen&&searchPopup.PlacementTarget is FrameworkElement field&&!field.IsMouseOver)searchPopup.IsOpen=false;};
        Title="VideoDesk "+GetType().Assembly.GetName().Version?.ToString(3);Width=1480;Height=940;MinWidth=1060;MinHeight=720;WindowStartupLocation=WindowStartupLocation.CenterScreen;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.CanResize;Background=CanvasBrush;FontFamily=(FontFamily)FindResource("AppFont");UseLayoutRounding=true;SnapsToDevicePixels=true;TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);TextOptions.SetTextRenderingMode(this,TextRenderingMode.ClearType);ApplyTheme();images.MaxDiskBytes=preferences.ImageCacheMegabytes*1048576;
        WindowChrome.SetWindowChrome(this,new WindowChrome{CaptionHeight=0,ResizeBorderThickness=new Thickness(6),CornerRadius=new CornerRadius(0),GlassFrameThickness=new Thickness(0)});
        DesktopWindowBounds.Attach(this,()=>fullscreen);
        shell.RowDefinitions.Add(new RowDefinition{Height=new GridLength(72)});shell.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});shell.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        shell.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(84)});shell.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        header=BuildHeader();Grid.SetColumnSpan(header,2);shell.Children.Add(header);
        var railPanel=new DockPanel{Background=RailBrush,LastChildFill=false};
        homeNav=Nav(PackIconKind.Home,"首页",async()=>{searching=false;popular=false;following=false;await ShowHome();});
        historyNav=Nav(PackIconKind.History,"历史",ShowHistory);
        favoritesNav=Nav(PackIconKind.StarOutline,"收藏",ShowFavorites);var favorites=favoritesNav;
        var top=new StackPanel();top.Children.Add(homeNav);top.Children.Add(historyNav);top.Children.Add(favorites);DockPanel.SetDock(top,Dock.Top);railPanel.Children.Add(top);
        var bottom=new StackPanel{Margin=new Thickness(0,0,0,18)};
        settingsNav=Nav(PackIconKind.CogOutline,"设置",ShowSettings);bottom.Children.Add(settingsNav);
        avatar=IconButton(PackIconKind.AccountCircleOutline,"登录账号",44);avatar.Foreground=Muted;avatar.Margin=new Thickness(16,4,16,0);avatar.Click+=async(_,_)=>await Run(account?.LoggedIn==true?()=>OpenCreator(account.Mid):Login);bottom.Children.Add(avatar);DockPanel.SetDock(bottom,Dock.Bottom);railPanel.Children.Add(bottom);
        rail=new Border{Child=railPanel};Grid.SetRow(rail,1);shell.Children.Add(rail);
        shell.Background=HeaderBrush;var workspaceSurface=new Border{Background=BackdropBrush,CornerRadius=new CornerRadius(42,0,0,0)};Grid.SetRow(workspaceSurface,1);Grid.SetColumn(workspaceSurface,1);shell.Children.Add(workspaceSurface);pageSurface.Child=content;SetPageSurface();content.LayoutTransform=new ScaleTransform(preferences.InterfaceScale,preferences.InterfaceScale);Grid.SetRow(pageSurface,1);Grid.SetColumn(pageSurface,1);shell.Children.Add(pageSurface);
        Grid.SetRow(loading,1);Grid.SetColumn(loading,1);Panel.SetZIndex(loading,20);shell.Children.Add(loading);
        BuildFeedback();BuildLaterFab();
        Content=new System.Windows.Documents.AdornerDecorator{Child=shell};
        LocationChanged+=(_,_)=>overlay?.Reposition();SizeChanged+=(_,_)=>overlay?.Reposition();StateChanged+=(_,_)=>overlay?.Reposition();
        Activated+=(_,_)=>_ = Dispatcher.BeginInvoke(Mouse.Synchronize,DispatcherPriority.Input);
        Deactivated+=(_,_)=>searchPopup.IsOpen=false;
        content.SizeChanged+=ResizeCards;
        Loaded+=async(_,_)=>await LoadStartup();
        PreviewKeyDown+=HandleKey;
        timer.Tick+=(_,_)=>PollPlayer();
        local.ProgressWriteFailed+=error=>_ = Dispatcher.BeginInvoke(()=>{if(IsLoaded&&!closingStarted)Notify("本地观看进度保存失败："+error.Message);});
        Closing+=async(_,e)=>{if(closingApproved)return;e.Cancel=true;await CloseApplicationAsync();};
    }
    private FrameworkElement BuildHeader()
    {
        var grid=new Grid{Background=HeaderBrush};
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(290)});grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(160)});
        WindowDrag.Attach(this,grid);
        var brand=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(94,0,0,0),VerticalAlignment=VerticalAlignment.Center};brand.Children.Add(new Image{Source=BitmapFrame.Create(new Uri("pack://application:,,,/Assets/AppIcon.png")),Width=32,Height=32,Margin=new Thickness(0,0,12,0)});var logo=Text("VideoDesk",27,FontWeights.Bold);logo.FontFamily=new FontFamily("Segoe UI");brand.Children.Add(logo);grid.Children.Add(brand);
        var field=new Grid{Width=510,Height=44};field.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(46)});field.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});field.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var magnify=Icon(PackIconKind.Magnify,23);magnify.Foreground=Muted;magnify.VerticalAlignment=VerticalAlignment.Center;magnify.HorizontalAlignment=HorizontalAlignment.Center;field.Children.Add(magnify);
        search.FontSize=16;search.VerticalContentAlignment=VerticalAlignment.Center;search.BorderThickness=new Thickness(0);search.Background=Brushes.Transparent;search.Padding=new Thickness(0,0,12,0);search.Style=null;NativeInputTemplate(search);Grid.SetColumn(search,1);field.Children.Add(search);var clearSearch=ClearInputButton(search,"清空搜索");clearSearch.Margin=new Thickness(0,0,8,0);Grid.SetColumn(clearSearch,2);field.Children.Add(clearSearch);
        var hint=Text("搜索视频、UP主或关键词",16);hint.Foreground=Muted;hint.IsHitTestVisible=false;hint.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(hint,1);field.Children.Add(hint);
        search.TextChanged+=(_,_)=>{hint.Visibility=search.Text.Length==0?Visibility.Visible:Visibility.Collapsed;if(searchPopup.IsOpen)ScheduleSuggestions();};
        search.GotKeyboardFocus+=(_,_)=>ShowSearchPopup();
        search.PreviewMouseLeftButtonDown+=(_,_)=>{if(!searchPopup.IsOpen)ShowSearchPopup();};
        search.KeyDown+=async(_,e)=>{if(e.Key==Key.Enter){e.Handled=true;searchPopup.IsOpen=false;await Run(SubmitSearch);}else if(e.Key==Key.Escape)searchPopup.IsOpen=false;};
        var box=new Border{Child=field,CornerRadius=new CornerRadius(24),Background=Surface,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(box,1);grid.Children.Add(box);searchPopup.PlacementTarget=box;
        var controls=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top};
        var minimize=IconButton(PackIconKind.Minus,"最小化",48);minimize.Height=56;minimize.Focusable=false;minimize.IsTabStop=false;minimize.Click+=(_,_)=>WindowState=WindowState.Minimized;
        var maximize=IconButton(PackIconKind.WindowMaximize,"最大化",48);maximize.Height=56;maximize.Focusable=false;maximize.IsTabStop=false;maximize.Click+=(_,_)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
        var close=IconButton(PackIconKind.Close,"关闭",48);close.Height=56;close.Focusable=false;close.IsTabStop=false;close.Click+=(_,_)=>Close();controls.Children.Add(minimize);controls.Children.Add(maximize);controls.Children.Add(close);Grid.SetColumn(controls,2);grid.Children.Add(controls);
        return new Border{Child=grid};
    }
    private CancellationToken BeginPage(bool rememberPage=true)
    {
        CancelCollectionOpen();CancelStreamOpen();
        StopCardPreview();storyboardPreview?.Dispose();storyboardPreview=null;playerFeaturesSession?.Cancel();playerFeaturesSession?.Dispose();playerFeaturesSession=null;videoChapters=[];ReleasePageFocus();
        DismissFeedback();if(rememberPage)RememberPage();CloseDynamicSidebar(true);RememberSocialScroll();followingState=null;popularState=null;dynamicDetail=null;dynamicScroll=null;staticPage="";
        searchPopup.IsOpen=false;if(commentMenu!=null)commentMenu.IsOpen=false;if(composerPopup!=null)composerPopup.IsOpen=false;RememberFeedScroll();if(fullscreen)ToggleFullscreen();SaveProgress();_ = FlushCloudHistory();navigation.Cancel();navigation.Dispose();navigation=new();
        EndDanmakuSession();ReleaseFeedVisuals();overlay?.Dispose();overlay=null;player?.Dispose();player=null;detail=null;part=null;ReleasePageReferences();foreach(var page in content.Children.Cast<UIElement>().ToArray()){if(ReferenceEquals(page,historyLayout)||ReferenceEquals(page,favoritesLayout)||ReferenceEquals(page,cachedFeed?.Layout))page.Visibility=Visibility.Collapsed;else content.Children.Remove(page);}playerPage=null;currentFeed=null;creatorVersion++;creatorState=null;creatorBody=null;creatorScroll=null;creatorTabs=null;commentSidebar=null;videoSidebar=null;relatedCardsPanel=null;accountStateReady=false;accountStateLoading=false;interactionBusy=false;accountPending="";accountRevision++;SetPageSurface();return navigation.Token;
    }
    private void ShowCachedPage(FrameworkElement page){if(page.Parent!=content)content.Children.Add(page);page.Visibility=Visibility.Visible;}
    private Button VideoTile(VideoCard video)
    {
        var stack=new StackPanel();var cover=CardCover(video.Cover,video.Duration,$"▷ {BilibiliClient.CountLabel(video.Views)}    ▤ {BilibiliClient.CountLabel(video.Danmaku)}",out _);stack.Children.Add(cover);stack.Children.Add(CardTitle(video.Title));
        stack.Children.Add(CardMetadata(video));
        var tile=CardButton(stack,video.Title,()=>OpenVideo(video.Bvid));AttachCardPreview(tile,cover,video);AddLaterMenu(tile,video);
        tile.SizeChanged+=(_,e)=>cover.Height=Math.Max(100,e.NewSize.Width*9/16);
        return tile;
    }
    private Button RelatedTile(VideoCard v)
    {
        var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(142)});row.ColumnDefinitions.Add(new ColumnDefinition());
        var picture=new Grid{Width=132,Height=76,ClipToBounds=true,Background=Surface};var image=new Image{Stretch=Stretch.UniformToFill};LoadImage(image,v.Cover,200);picture.Children.Add(image);var duration=Text(BilibiliClient.DurationLabel(v.Duration),11);duration.Foreground=Brushes.White;duration.Background=new SolidColorBrush(Color.FromArgb(140,0,0,0));duration.HorizontalAlignment=HorizontalAlignment.Right;duration.VerticalAlignment=VerticalAlignment.Bottom;duration.Margin=new Thickness(3);picture.Children.Add(duration);row.Children.Add(picture);
        var text=new StackPanel();var title=Text(v.Title,14,FontWeights.Medium);title.TextWrapping=TextWrapping.Wrap;title.MaxHeight=42;title.TextTrimming=TextTrimming.CharacterEllipsis;text.Children.Add(title);var author=Text(v.Creator,12);author.Foreground=Muted;author.TextTrimming=TextTrimming.CharacterEllipsis;author.Margin=new Thickness(0,4,0,0);CreatorLink(author,v.Mid);text.Children.Add(author);var views=Text("▷ "+BilibiliClient.CountLabel(v.Views),12);views.Foreground=Muted;text.Children.Add(views);Grid.SetColumn(text,1);row.Children.Add(text);
        var button=Button("");button.Content=row;button.Height=double.NaN;button.Margin=new Thickness(0,0,0,13);button.Padding=new Thickness(0);button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.ToolTip=v.Title;AutomationProperties.SetName(button,v.Title);button.Click+=async(_,_)=>await Run(()=>OpenVideo(v.Bvid));AddLaterMenu(button,v);return button;
    }
    private async Task LoadRelatedBoth(string bvid,CancellationToken ct)
    {
        async Task Fetch(DiscoveryMode mode){try{var list=await api.GetRelated(bvid,mode,ct);if(!ct.IsCancellationRequested&&detail?.Video.Bvid==bvid){related[mode]=list;if(relatedMode==mode)RenderRelatedCards();}}catch(OperationCanceledException){}catch(Exception error){if(!ct.IsCancellationRequested&&detail?.Video.Bvid==bvid&&relatedMode==mode&&relatedCardsPanel!=null)SectionState(relatedCardsPanel).Failed("相关推荐加载失败",error.Message,async()=>{ct.ThrowIfCancellationRequested();await Run(RefreshRelated);});}}
        await Task.WhenAll(Fetch(DiscoveryMode.Regular),Fetch(DiscoveryMode.Roaming));
    }
    private async Task RefreshRelated()
    {
        var bvid=detail!.Video.Bvid;var ct=navigation.Token;var mode=relatedMode;var panel=relatedCardsPanel!;var feedback=SectionState(panel);
        try{if(!related.ContainsKey(mode)){feedback.Loading("正在加载相关推荐…");var list=await api.GetRelated(bvid,mode,ct);ct.ThrowIfCancellationRequested();related[mode]=list;}ct.ThrowIfCancellationRequested();if(relatedMode==mode&&detail?.Video.Bvid==bvid)RenderRelatedCards();}
        catch(Exception error) when(error is not OperationCanceledException){if(!ct.IsCancellationRequested&&detail?.Video.Bvid==bvid&&relatedMode==mode)feedback.Failed("相关推荐加载失败",error.Message,async()=>{ct.ThrowIfCancellationRequested();await Run(RefreshRelated);});}
    }
    private async Task ChangeQuality(int qn)
    {
        if(player is not {MediaReady:true} host||detail==null||part==null)return;
        CancelCollectionOpen();var request=BeginStreamOpen(navigation.Token);var ct=request.Token;
        var bvid=detail.Video.Bvid;var cid=part.Cid;var revision=host.LoadRevision;
        try
        {
            var stream=await api.GetPlayStream(bvid,cid,qn,ct);ct.ThrowIfCancellationRequested();
            if(collectionOpen!=null||!ReferenceEquals(player,host)||host.LoadRevision!=revision||detail?.Video.Bvid!=bvid||part?.Cid!=cid)return;
            var position=host.ResumePosition;var paused=host.Text("pause")=="yes";
            PopulateQualities(stream);host.Load(stream,position);host.Set("pause",paused?"yes":"no");
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){}
        finally{EndStreamOpen(request);}
    }
    private void TogglePause(){if(player?.CanInteract!=true)return;player.Command("cycle","pause");overlay?.Danmaku.Wake();if(pause?.Content is PackIcon icon)icon.Kind=player.Text("pause")=="yes"?PackIconKind.Play:PackIconKind.Pause;overlay?.ShowFeedback(player.Text("pause")=="yes"?"暂停":"播放",0);}
    private void PollPlayer(bool drainEvents=true)
    {
        if(player?.Ready!=true||part is null)return;
        if(drainEvents)player.PumpEvents();if(player?.MediaReady!=true||part==null)return;if(overlay!=null&&player.Number("video-params/h")>0)overlay.Danmaku.VideoAspect=player.Number("video-params/w")/player.Number("video-params/h");var pos=player.Number("time-pos");var duration=Math.Max(part.Duration,player.Number("duration"));if(player.Number("video-params/w")>0)RequestDanmakuWindow(pos);
        var playbackTick=Stopwatch.GetTimestamp();if(lastPlaybackTick>0&&player.Text("pause")!="yes"&&player.Number("video-params/w")>0)playedSeconds+=Math.Min(1,Stopwatch.GetElapsedTime(lastPlaybackTick,playbackTick).TotalSeconds);lastPlaybackTick=playbackTick;
        if(seek!=null){seek.Maximum=Math.Max(1,duration);if(!player.Seeking)seek.SetPlaybackPosition(pos);}
        if(seek is {} timeline&&DateTime.UtcNow-lastBufferPoll>TimeSpan.FromMilliseconds(600)){timeline.SetBuffered(player.CachedRanges());lastBufferPoll=DateTime.UtcNow;}
        if(timeText!=null)timeText.Text=BilibiliClient.DurationLabel(pos)+" / "+BilibiliClient.DurationLabel(duration);
        UpdatePlayerFeatures(pos);
        if(pause?.Content is PackIcon pauseIcon)pauseIcon.Kind=player.Text("pause")=="yes"?PackIconKind.Play:PackIconKind.Pause;
        if(DateTimeOffset.UtcNow-lastSave>TimeSpan.FromSeconds(5))
        {
            SaveProgress();lastSave=DateTimeOffset.UtcNow;
            if(App.Diagnostics.Enabled) _=App.Diagnostics.WriteAsync("playback-state.json",()=>
            {
                using var process=Process.GetCurrentProcess();
                var stats=new{capturedAt=DateTimeOffset.UtcNow,processId=Environment.ProcessId,bvid=detail!.Video.Bvid,cid=part.Cid,position=pos,duration,paused=player.Text("pause"),speed=player.Number("speed"),volume=player.Number("volume"),width=player.Number("video-params/w"),height=player.Number("video-params/h"),audioSampleRate=player.Number("audio-params/samplerate"),codec=player.Text("video-codec"),hardwareDecoder=player.Text("hwdec-current"),danmakuTrackLoaded=HasDanmakuTrack(),danmakuVisible=danmakuEnabled?"yes":"no",danmakuRenderer=overlay?.Danmaku.Diagnostics,workingSetMiB=process.WorkingSet64/1048576d};
                return JsonSerializer.Serialize(stats,new JsonSerializerOptions{WriteIndented=true});
            });
        }
    }
    private void HandleKey(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Left&&Keyboard.Modifiers==ModifierKeys.Alt){e.Handled=true;_ = Run(GoBack);return;}
        if(e.Key==Key.Escape){searchPopup.IsOpen=false;if(fullscreen)ToggleFullscreen();return;}
        if(Keyboard.FocusedElement is PlaybackTimeline&&e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown)return;
        if(player?.CanInteract!=true||Keyboard.FocusedElement is TextBoxBase or ComboBox or ComboBoxItem or MenuItem)return;
        if(e.Key==Key.Space){TogglePause();e.Handled=true;}else if(e.Key is Key.Left or Key.Right){player.Command("seek",e.Key==Key.Left?"-5":"5","relative");overlay?.ShowFeedback(e.Key==Key.Left?"后退 5 秒":"前进 5 秒",e.Key==Key.Left?-1:1);e.Handled=true;}else if(e.Key==Key.F){ToggleFullscreen();e.Handled=true;}
    }
    private void ToggleFullscreen()
    {
        if(playerPage==null)return;
        if(!fullscreen)
        {
            // Capture normal window state before focusing the owned video controls.
            normalState=WindowState;normalBounds=RestoreBounds;normalTopmost=Topmost;fullscreen=true;
            FocusManager.SetFocusedElement(this,playerFrame);Keyboard.Focus(playerFrame);searchPopup.IsOpen=false;
            header!.Visibility=rail!.Visibility=side!.Visibility=playerTitle!.Visibility=danmakuRow!.Visibility=Visibility.Collapsed;
            shell.RowDefinitions[0].Height=new GridLength(0);shell.ColumnDefinitions[0].Width=new GridLength(0);playerPage.ColumnDefinitions[1].Width=new GridLength(0);
            leftPlayer!.Margin=new Thickness(0);leftPlayer.VerticalAlignment=VerticalAlignment.Stretch;leftPlayer.RowDefinitions[1].Height=new GridLength(1,GridUnitType.Star);leftPlayer.RowDefinitions[2].Height=new GridLength(0);
            playerFrame!.ClearValue(HeightProperty);content.Margin=new Thickness(0);content.LayoutTransform=Transform.Identity;statusBar.Visibility=Visibility.Collapsed;
            WindowChrome.SetWindowChrome(this,null);ResizeMode=ResizeMode.NoResize;WindowState=WindowState.Maximized;
            DesktopWindowBounds.ApplyFullscreenBounds(this);
        }
        else
        {
            fullscreen=false;WindowState=WindowState.Normal;ResizeMode=ResizeMode.CanResize;
            WindowChrome.SetWindowChrome(this,new WindowChrome{CaptionHeight=0,ResizeBorderThickness=new Thickness(6),CornerRadius=new CornerRadius(0),GlassFrameThickness=new Thickness(0)});
            header!.Visibility=rail!.Visibility=side!.Visibility=playerTitle!.Visibility=danmakuRow!.Visibility=Visibility.Visible;
            shell.RowDefinitions[0].Height=new GridLength(72);shell.ColumnDefinitions[0].Width=new GridLength(84);playerPage.ColumnDefinitions[1].Width=new GridLength(360);
            leftPlayer!.Margin=new Thickness(0,0,22,0);leftPlayer.VerticalAlignment=VerticalAlignment.Stretch;leftPlayer.RowDefinitions[1].Height=GridLength.Auto;leftPlayer.RowDefinitions[2].Height=new GridLength(1,GridUnitType.Star);
            playerPage.ColumnDefinitions[1].Width=new GridLength(400);UpdatePlayerHeight();content.Margin=new Thickness(22,18,22,14);content.LayoutTransform=new ScaleTransform(preferences.InterfaceScale,preferences.InterfaceScale);
            Left=normalBounds.Left;Top=normalBounds.Top;Width=normalBounds.Width;Height=normalBounds.Height;WindowState=normalState;
            Topmost=normalTopmost;
        }
        SetPlayerSurface(fullscreen);leftPlayer!.InvalidateMeasure();Dispatcher.BeginInvoke(()=>{UpdatePlayerHeight();overlay?.Reposition();},DispatcherPriority.Loaded);overlay?.Wake();
    }
    private async Task SubmitSearch()
    {
        var input=search.Text.Trim();if(string.IsNullOrEmpty(input))return;
        var match=System.Text.RegularExpressions.Regex.Match(input,@"BV[0-9A-Za-z]{10}");if(match.Success){await OpenVideo(match.Value);return;}
        if(!searching||keyword!=input)searchBack.Push(CaptureDiscovery());searchOrder="totalrank";searchSource="";keyword=input;searchHistory.Remove(input);searchHistory.Insert(0,input);if(searchHistory.Count>20)searchHistory.RemoveAt(20);PersistSearchHistory();searching=true;await ShowHome();
    }
    private void PersistSearchHistory(){File.WriteAllText(Path.Combine(App.DataDirectory,"search-history.json"),JsonSerializer.Serialize(searchHistory));}
    private void LoadHistory(){try{var path=Path.Combine(App.DataDirectory,"search-history.json");if(File.Exists(path))searchHistory.AddRange(JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path))??[]);}catch(JsonException){}}
    private async Task Login()
    {
        var qr=await api.CreateQr();using var generator=new QRCodeGenerator();using var data=generator.CreateQrCode(qr.Url,QRCodeGenerator.ECCLevel.M);using var png=new PngByteQRCode(data);var bytes=png.GetGraphic(8);
        using var memory=new MemoryStream(bytes);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=memory;bitmap.EndInit();bitmap.Freeze();
        var dialog=new Window{Title="扫码登录",Owner=this,Width=390,Height=440,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brushes.White};var panel=new StackPanel{Margin=new Thickness(28)};panel.Children.Add(Text("使用哔哩哔哩 App 扫码",19,FontWeights.SemiBold));panel.Children.Add(new Image{Source=bitmap,Width=260,Height=260,Margin=new Thickness(0,14,0,8)});var label=Text("请扫码并在手机上确认登录",14);label.HorizontalAlignment=HorizontalAlignment.Center;panel.Children.Add(label);dialog.Content=panel;
        using var stop=new CancellationTokenSource();dialog.Closed+=(_,_)=>stop.Cancel();
        async Task Poll(){try{while(!stop.IsCancellationRequested){await Task.Delay(2200,stop.Token);var result=await api.PollQr(qr.Key,stop.Token);label.Text=result.Code switch{86101=>"请扫码并在手机上确认登录",86090=>"已扫码，请在手机上确认",86038=>"二维码已过期，请关闭后重新登录",0=>"登录成功",_=>result.Message};if(result.Code==0){account=await api.GetAccount(stop.Token);UpdateAccount();dialog.Close();break;}if(result.Code==86038)break;}}catch(OperationCanceledException){}catch(Exception e){label.Text=e.Message;}}
        dialog.Loaded+=(_,_)=>_ = Poll();ShowModal(dialog);
    }
    private void UpdateAccount()
    {
        StopCardPreview();EnsureLaterAccount();avatar.ToolTip=account?.LoggedIn==true?"我的空间 · "+account.Name:"扫码登录";AutomationProperties.SetName(avatar,account?.LoggedIn==true?"账号："+account.Name:"扫码登录");
        if(account?.LoggedIn!=true)return;avatar.Width=52;avatar.Height=56;avatar.Padding=new Thickness(0);var identity=new Grid{Width=46,Height=50};var image=new Image{Width=40,Height=40,VerticalAlignment=VerticalAlignment.Top};image.Clip=new EllipseGeometry(new Point(20,20),20,20);LoadImage(image,account.Avatar,72,CancellationToken.None);identity.Children.Add(image);
        if(account.Level>=0){var badge=OfficialIcons.Level(account.Level,account.Senior,12);badge.VerticalAlignment=VerticalAlignment.Bottom;badge.HorizontalAlignment=HorizontalAlignment.Center;identity.Children.Add(badge);}avatar.Content=identity;
    }
    private void LoadImage(Image image,string url,int width=360,CancellationToken? cancellation=null,Action? applied=null,double sourceAspect=0)
    {
        if(string.IsNullOrWhiteSpace(url))return;
        image.SetValue(ManagedImageProperty,true);
        CancellationTokenSource? visible=null;
        async void Apply()
        {
            if(image.Source!=null)return;
            visible?.Cancel();visible?.Dispose();visible=CancellationTokenSource.CreateLinkedTokenSource(cancellation??navigation.Token);var ct=visible.Token;
            try
            {
                var logical=image.ActualWidth>0?image.ActualWidth:double.IsFinite(image.Width)?image.Width:width;
                if(sourceAspect>0&&image.ActualHeight>0)logical=image.Stretch==Stretch.UniformToFill?Math.Max(logical,image.ActualHeight*sourceAspect):Math.Min(logical,image.ActualHeight*sourceAspect);
                var pixels=Math.Clamp((int)Math.Ceiling(logical*VisualTreeHelper.GetDpi(image).PixelsPerDip/32)*32,32,2048);
                var bitmap=await images.Get(url,pixels,ct);if(bitmap!=null)await Dispatcher.InvokeAsync(()=>{if(image.IsLoaded&&image.IsVisible&&PresentationSource.FromVisual(image)!=null){image.Source=bitmap;applied?.Invoke();}},DispatcherPriority.Background,ct);
            }
            catch(OperationCanceledException){}catch(Exception e){Debug.WriteLine(e.Message);}
        }
        void SuspendImage(){visible?.Cancel();visible?.Dispose();visible=null;if(!(bool)image.GetValue(RetainImagesOnHideProperty))image.Source=null;}
        image.Loaded+=(_,_)=>Apply();image.Unloaded+=(_,_)=>SuspendImage();image.IsVisibleChanged+=(_,_)=>{if(image.IsVisible&&image.IsLoaded)Apply();else if(!image.IsVisible)SuspendImage();};
        if(image.IsLoaded)Apply();
    }
    private Button Nav(PackIconKind icon,string label,Func<Task> action){var stack=new StackPanel();var i=Icon(icon,25);i.HorizontalAlignment=HorizontalAlignment.Center;stack.Children.Add(i);var t=Text(label,13);t.Foreground=Muted;t.HorizontalAlignment=HorizontalAlignment.Center;t.Margin=new Thickness(0,5,0,0);stack.Children.Add(t);var b=Button("");b.Content=stack;b.Height=72;b.Margin=new Thickness(8,8,8,0);ButtonAssist.SetCornerRadius(b,new CornerRadius(18));b.Foreground=Muted;AutomationProperties.SetName(b,label);b.Click+=async(_,_)=>await Run(action);return b;}
    private static TextBlock Text(string value,double size,FontWeight? weight=null)=>new(){Text=value,FontSize=size,Foreground=Ink,FontWeight=weight??FontWeights.Normal,VerticalAlignment=VerticalAlignment.Center};
    private new static PackIcon Icon(PackIconKind kind,double size)=>new(){Kind=kind,Width=size,Height=size};
    private static Button Button(string label)=>new(){Content=label,Foreground=Ink,Style=(Style)Application.Current.FindResource("PlainButton"),FontSize=14};
    private static Button IconButton(PackIconKind kind,string label,double width){var b=Button("");b.Content=Icon(kind,22);b.Padding=new Thickness(0);b.Width=width;b.ToolTip=label;AutomationProperties.SetName(b,label);return b;}
    private static Button Tab(string label,bool selected){var b=Button(label);b.FontSize=19;b.FontWeight=FontWeights.SemiBold;b.Foreground=selected?Pink:Brushes.Black;b.BorderThickness=selected?new Thickness(0,0,0,2):new Thickness(0);b.BorderBrush=Pink;b.Margin=new Thickness(0,0,18,0);return b;}
    private readonly Dictionary<string,SegmentedModeSwitch> modeSwitches=[];
    private FrameworkElement ModeSwitch(DiscoveryMode selected,Func<DiscoveryMode,Task> choose,string? scope=null)
    {
        if(scope!=null&&modeSwitches.TryGetValue(scope,out var retained))
        {
            if(retained.Parent is Panel previous)previous.Children.Remove(retained);
            retained.Select(selected,true);return retained;
        }
        var control=new SegmentedModeSwitch(selected,choose,Surface,PalePink,Muted,Pink);
        if(scope!=null)modeSwitches[scope]=control;
        return control;
    }
}


















