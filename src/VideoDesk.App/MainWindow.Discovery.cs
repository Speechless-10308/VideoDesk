using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using VideoDesk.Core;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private readonly Button favoritesNav;
    private readonly Dictionary<string,FeedState> feeds=new();
    private FeedState? currentFeed;
    private FeedState? cachedFeed;
    private StackPanel? feedPanel;
    private ScrollViewer? feedScroll;
    private bool following;
    private string searchOrder="totalrank",searchSource="";
    private IReadOnlyList<SearchTopic> hotTopics=[];
    private CancellationTokenSource suggestions=new();
    private UnderlinedTabs? homeTabs,searchTabs;
    private TextBlock? searchCount;
    private DateTime lastFeedVirtualization;
    private sealed record DiscoverySnapshot(bool Searching,string Keyword,string Order,DiscoveryMode Mode,bool Popular,bool Following,string Source="",string? ReturnVideo=null,bool VideoOnly=false,PopularCategory Category=PopularCategory.General,int Weekly=0,int Music=0,int Region=0);
    private readonly Stack<DiscoverySnapshot> searchBack=[];
    private DiscoverySnapshot CaptureDiscovery()=>new(searching,keyword,searchOrder,searchMode,popular,following,searchSource,VideoOnly:followingVideoOnly,Category:popularCategory,Weekly:weeklyPeriod,Music:musicPeriod,Region:rankingRegion);
    private Task RestoreDiscovery(DiscoverySnapshot state){searching=state.Searching;keyword=state.Keyword;searchOrder=state.Order;searchSource=state.Source;searchMode=state.Mode;popular=state.Popular;following=state.Following;followingVideoOnly=state.VideoOnly;popularCategory=state.Category;weeklyPeriod=state.Weekly;musicPeriod=state.Music;rankingRegion=state.Region;search.Text=state.Searching?state.Keyword:"";return state.ReturnVideo!=null?NavigateBackToVideo(state.ReturnVideo):ShowHome();}
    private Task BackSearch()=>searchBack.Count>0?RestoreDiscovery(searchBack.Pop()):ShowHome();
    private sealed class FeedState
    {
        public readonly List<FeedBatch> Batches=[];
        public int RequestPage,Columns;public double Scroll;public bool Loading,Ended,TailFillQueued;public string Offset="";public CancellationToken LoadingToken;public long Total;public DateTime LastUsed=DateTime.UtcNow;
        public DockPanel? Layout;public StackPanel? Panel;public ScrollViewer? Viewer;public CancellationToken ViewToken;public bool ViewReady;public double ViewWidth;
    }
    private sealed class FeedBatch(IReadOnlyList<VideoCard> videos,int page,string label="")
    {
        public IReadOnlyList<VideoCard> Videos=videos;public int Page=page;public string Label=label;public Border? Element;public double MeasuredHeight;
    }
    private void SelectNav(Button selected)
    {
        foreach(var b in new[]{homeNav,historyNav,favoritesNav,settingsNav}){b.Background=b==selected?PalePink:Brushes.Transparent;b.Foreground=b==selected?Pink:Muted;if(b.Content is StackPanel row)foreach(var text in row.Children.OfType<TextBlock>())text.Foreground=b.Foreground;}
    }
    private void RememberFeedScroll(){if(currentFeed!=null&&feedScroll!=null)currentFeed.Scroll=feedScroll.VerticalOffset;}
    private void ReleaseFeedVisuals()
    {
        if(currentFeed?.Layout is {} layout)layout.Visibility=Visibility.Collapsed;
        feedPanel=null;feedScroll=null;TrimFeedCache("");
    }
    private void DiscardFeedView(FeedState state)
    {
        if(state.Layout!=null)content.Children.Remove(state.Layout);
        foreach(var batch in state.Batches){if(batch.Element is not {} element)continue;foreach(var rows in VisualTree.Descendants(element).OfType<ViewportCardRows<VideoCard>>())rows.ReleaseRetainedRows();element.Child=null;batch.Element=null;}
        state.Panel?.Children.Clear();state.Layout=null;state.Panel=null;state.Viewer=null;state.ViewReady=false;
    }
    private void TrimFeedCache(string activeKey)
    {
        var inactive=feeds.Where(pair=>pair.Key!=activeKey).OrderByDescending(pair=>pair.Value.LastUsed).ToArray();
        int videos=0;
        for(int i=0;i<inactive.Length;i++)
        {
            videos+=inactive[i].Value.Batches.Sum(batch=>batch.Videos.Count);
            if(i>=7||videos>2000)feeds.Remove(inactive[i].Key);
        }
    }
    private async Task ShowHome()
    {
        if(!searching&&following){await ShowFollowing();return;}
        if(!searching&&popular){await ShowPopular();return;}
        var ct=BeginPage();SelectNav(homeNav);
        var key=searching?$"search:{searchMode}:{searchOrder}:{searchSource}:{keyword}":$"home:{homeMode}:{popular}:{following}";
        if(!feeds.TryGetValue(key,out var state)){state=new();feeds[key]=state;}
        state.LastUsed=DateTime.UtcNow;TrimFeedCache(key);
        if(state.LoadingToken.IsCancellationRequested)state.Loading=false;currentFeed=state;state.ViewToken=ct;
        if(ReferenceEquals(cachedFeed,state)&&state.ViewReady&&state.Layout!=null&&state.Panel!=null&&state.Viewer!=null)
        {
            feedPanel=state.Panel;feedScroll=state.Viewer;if(Math.Abs(state.ViewWidth-content.ActualWidth)>1)ResizeFeedCards(state);if(!searching&&state.Layout.Children.OfType<Grid>().FirstOrDefault(g=>g.Tag as string=="discovery-header") is {} header)AttachHomeTabs(header);ShowCachedPage(state.Layout);
            if(state.Batches.Count==0){state.Ended=false;await LoadFeedPage(state,ct);}
            QueueFeedTailFill(state,state.Viewer,ct);return;
        }
        if(cachedFeed!=null)DiscardFeedView(cachedFeed);cachedFeed=state;
        var layout=new DockPanel();
        layout.SetValue(RetainImagesOnHideProperty,true);state.Layout=layout;
        if(!searching)
        {
            var header=DiscoveryHeader(RefreshFeed,true);DockPanel.SetDock(header,Dock.Top);layout.Children.Add(header);
        }
        else
        {
            var top=new Grid{Height=100,Margin=new Thickness(4,0,0,8)};top.ColumnDefinitions.Add(new());top.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            top.RowDefinitions.Add(new(){Height=new GridLength(48)});top.RowDefinitions.Add(new(){Height=new GridLength(46)});
            var headingRow=new Grid{Margin=new Thickness(0,0,24,0)};headingRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});headingRow.ColumnDefinitions.Add(new());headingRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});var back=BackButton("返回上一个搜索页面 · Alt+←");back.Margin=new Thickness(8,6,10,0);back.IsEnabled=searchBack.Count>0;back.Click+=async(_,_)=>await Run(BackSearch);headingRow.Children.Add(back);var heading=Text(keyword,23,FontWeights.SemiBold);heading.TextTrimming=TextTrimming.CharacterEllipsis;heading.ToolTip=keyword;Grid.SetColumn(heading,1);headingRow.Children.Add(heading);searchCount=Text(state.Total>0?$"约 {BilibiliClient.CountLabel(state.Total)} 个视频":"正在搜索…",13);searchCount.Foreground=Muted;searchCount.Margin=new Thickness(18,0,0,0);Grid.SetColumn(searchCount,2);headingRow.Children.Add(searchCount);top.Children.Add(headingRow);
            searchTabs??=new UnderlinedTabs(new[]{("综合","totalrank"),("最新","pubdate"),("播放最多","click")}.Select(t=>(t.Item1,searchOrder==t.Item2,(Func<Task>)(async()=>{searchOrder=t.Item2;await Run(ShowHome);}))),15);searchTabs.Select(searchOrder=="click"?2:searchOrder=="pubdate"?1:0);
            if(searchTabs.Parent is Panel previous)previous.Children.Remove(searchTabs);Grid.SetRow(searchTabs,1);top.Children.Add(searchTabs);
            var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};actions.Children.Add(ModeSwitch(searchMode,async mode=>{var direction=mode.CompareTo(searchMode);searchMode=mode;await Run(ShowHome);if(searching&&searchMode==mode)UiMotion.Enter(feedPanel,direction);},"search"));
            var refresh=IconButton(PackIconKind.Refresh,"刷新",44);refresh.Margin=new Thickness(16,0,0,0);refresh.Click+=async(_,_)=>await Run(RefreshFeed);actions.Children.Add(refresh);Grid.SetColumn(actions,1);top.Children.Add(actions);DockPanel.SetDock(top,Dock.Top);layout.Children.Add(top);
        }
        feedPanel=new StackPanel();feedScroll=new SmoothScrollViewer{Content=feedPanel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};layout.Children.Add(feedScroll);content.Children.Add(layout);
        var panel=feedPanel;var scroll=feedScroll;state.Panel=panel;state.Viewer=scroll;state.ViewWidth=content.ActualWidth;ReflowFeedRows(state);
        double cursor=0;var slice=Stopwatch.StartNew();foreach(var batch in state.Batches.ToArray()){ct.ThrowIfCancellationRequested();if(!state.Batches.Contains(batch))continue;var height=FeedBatchHeight(batch);var near=cursor+height>state.Scroll-600&&cursor<state.Scroll+Math.Max(720,content.ActualHeight)+600;panel.Children.Add(near?RenderBatch(batch):BatchPlaceholder(batch,true));cursor+=height;if(slice.Elapsed.TotalMilliseconds>=3){await Dispatcher.Yield(DispatcherPriority.Background);slice.Restart();}}
        ct.ThrowIfCancellationRequested();state.ViewReady=true;
        var settle=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(180)};
        settle.Tick+=(_,_)=>{if(scroll is SmoothScrollViewer smooth&&smooth.IsWheelAnimating)return;settle.Stop();VirtualizeFeed(state,scroll,panel);};scroll.Unloaded+=(_,_)=>settle.Stop();
        scroll.ScrollChanged+=async(_,e)=>{if(currentFeed!=state||!scroll.IsVisible)return;if(e.VerticalChange!=0||e.ExtentHeightChange!=0||e.ViewportHeightChange!=0){settle.Stop();settle.Start();}if(DateTime.UtcNow-lastFeedVirtualization>TimeSpan.FromMilliseconds(120)){lastFeedVirtualization=DateTime.UtcNow;VirtualizeFeed(state,scroll,panel);}if(e.VerticalChange>0&&scroll.ScrollableHeight-scroll.VerticalOffset<700&&!state.Loading&&!state.Ended)await Run(()=>LoadFeedPage(state,state.ViewToken));};
        scroll.IsVisibleChanged+=(_,_)=>{if(!scroll.IsVisible)settle.Stop();};
        if(state.Batches.Count==0){state.Ended=false;await LoadFeedPage(state,ct);}
        else
        {
            var restoreOffset=state.Scroll;
            await Dispatcher.InvokeAsync(()=>{ct.ThrowIfCancellationRequested();scroll.ScrollToVerticalOffset(restoreOffset);},DispatcherPriority.Loaded,ct);
            // ScrollToVerticalOffset takes effect in the next layout pass. Fill
            // that viewport immediately instead of waiting for the settle timer.
            await Dispatcher.InvokeAsync(()=>{ct.ThrowIfCancellationRequested();VirtualizeFeed(state,scroll,panel);},DispatcherPriority.Loaded,ct);
        }
        QueueFeedTailFill(state,scroll,ct);
    }
    private async Task<IReadOnlyList<VideoCard>> FetchFeed(FeedState state,int pn,CancellationToken ct)
    {
        if(following&&!searching){if(account?.LoggedIn!=true)throw new InvalidOperationException("登录后可以查看关注 UP 主的视频");var result=await api.GetFollowing(state.Offset,ct);ct.ThrowIfCancellationRequested();state.Offset=result.Offset;state.Ended=!result.HasMore;return result.Items;}
        if(searching){var result=await api.SearchResults(keyword,searchMode,pn,searchOrder,ct,searchSource);ct.ThrowIfCancellationRequested();state.Total=result.Total;state.Ended=!result.HasMore;if(searchCount!=null)searchCount.Text=$"约 {BilibiliClient.CountLabel(result.Total)} 个视频";return result.Items;}
        return await api.GetHome(homeMode,pn,popular,ct);
    }
    private async Task LoadFeedPage(FeedState state,CancellationToken ct)
    {
        if(state.Loading||state.Ended)return;ct.ThrowIfCancellationRequested();state.Loading=true;state.LoadingToken=ct;var panel=feedPanel!;var scroll=feedScroll!;bool appended=false;RemoveSectionState(panel);var indicator=SectionState(panel);indicator.Loading(state.Batches.Count==0?"正在加载视频…":"正在加载更多视频…");
        try
        {
            int pn=0;IReadOnlyList<VideoCard> list=[];VideoCard[] unique=[];var seen=state.Batches.SelectMany(b=>b.Videos).Select(v=>v.Bvid).ToHashSet();
            for(int attempt=0;attempt<3;attempt++){pn=state.RequestPage+1;list=await FetchFeed(state,pn,ct);ct.ThrowIfCancellationRequested();state.RequestPage=pn;unique=list.Where(v=>!seen.Contains(v.Bvid)).ToArray();if(unique.Length>0||list.Count==0||state.Ended)break;}
            if(list.Count==0){state.Ended=true;indicator.Empty(state.Batches.Count==0?"暂无视频":"暂无更多视频");return;}
            if(unique.Length==0){indicator.Empty("本批暂无新视频");var more=Button("继续加载推荐");more.Click+=async(_,_)=>{panel.Children.Remove(indicator);await Run(()=>LoadFeedPage(state,ct));};indicator.AddAction(more);return;}
            var batch=new FeedBatch(unique,pn);state.Batches.Add(batch);ReflowFeedRows(state);if(state.Batches.Contains(batch))panel.Children.Add(RenderBatch(batch));appended=true;
        }
        catch(Exception e) when(e is not OperationCanceledException)
        {
            if(ct.IsCancellationRequested)return;
            indicator.Failed(state.Batches.Count==0?"视频加载失败":"更多视频加载失败",e.Message,async()=>{var retryToken=state.ViewToken;retryToken.ThrowIfCancellationRequested();state.Ended=false;panel.Children.Remove(indicator);await Run(()=>LoadFeedPage(state,retryToken));});
            if(searching){var term=keyword;var official=Button("打开官方搜索页");official.Click+=(_,_)=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://search.bilibili.com/all?keyword="+Uri.EscapeDataString(term)){UseShellExecute=true});indicator.AddAction(official);if(searchCount!=null)searchCount.Text="搜索暂时不可用";}
            state.Ended=true;
        }
        finally{if(state.LoadingToken==ct)state.Loading=false;if(indicator.Status==SectionStatus.Loading||ct.IsCancellationRequested)panel.Children.Remove(indicator);if(appended)QueueFeedTailFill(state,scroll,ct);}
    }
    private Border RenderBatch(FeedBatch batch)
    {
        StackPanel stack;
        if(batch.Element?.Child is StackPanel retained)
        {
            if(batch.Element.Parent is Panel parent)parent.Children.Remove(batch.Element);
            stack=retained;
            if(stack.Children[0] is Grid divider)stack.Children.Remove(divider);
            foreach(var cards in stack.Children.OfType<UniformGrid>())cards.Columns=CardColumns();
        }
        else
        {
            stack=new();var cards=new ViewportCardRows<VideoCard>(()=>feedScroll,_=>CardColumns(),VideoTile){RetainOnSuspend=true};cards.SetItems(batch.Videos);stack.Children.Add(cards);var element=FeedBatchElement(batch);element.Height=double.NaN;element.Child=stack;
        }
        if(batch.Label.Length>0)
        {
            var line=new Grid{Margin=new Thickness(7,8,7,24)};line.Children.Add(new Border{Height=1,Background=new SolidColorBrush(Color.FromRgb(225,229,236)),VerticalAlignment=VerticalAlignment.Center});var tag=Text(batch.Label,12,FontWeights.Medium);tag.Foreground=Muted;tag.Background=CanvasBrush;tag.Padding=new Thickness(16,0,16,0);tag.HorizontalAlignment=HorizontalAlignment.Center;line.Children.Add(tag);stack.Children.Insert(0,line);
        }
        batch.Element!.Height=double.NaN;return batch.Element;
    }
    private double FeedViewportWidth=>feedScroll?.ViewportWidth>0&&Math.Abs(feedScroll.ActualWidth-content.ActualWidth)<1?feedScroll.ViewportWidth:Math.Max(1,content.ActualWidth-8);
    private double FeedBatchHeight(FeedBatch batch)=>batch.MeasuredHeight>0?batch.MeasuredHeight:Math.Ceiling(Math.Ceiling(batch.Videos.Count/(double)CardColumns())*(Math.Max(100,(FeedViewportWidth/CardColumns()-14)*9/16)+110)+(batch.Label.Length>0?47:0));
    private static Border FeedBatchElement(FeedBatch batch)
    {
        if(batch.Element==null){var element=new Border();batch.Element=element;element.SizeChanged+=(_,e)=>{if(ReferenceEquals(batch.Element,element)&&element.Child!=null&&e.NewSize.Height>0)batch.MeasuredHeight=e.NewSize.Height;};}return batch.Element;
    }
    private Border BatchPlaceholder(FeedBatch batch,bool detach=false){var element=FeedBatchElement(batch);if(detach&&element.Parent is Panel old)old.Children.Remove(element);element.Child=null;batch.MeasuredHeight=FeedBatchHeight(batch);element.Height=Math.Max(1,batch.MeasuredHeight);return element;}
    private void VirtualizeFeed(FeedState state,ScrollViewer scroll,Panel panel)
    {
        if(currentFeed!=state||!panel.IsLoaded)return;
        foreach(var batch in state.Batches)
        {
            var element=batch.Element;if(element?.Parent!=panel)continue;var top=element.TransformToAncestor(panel).Transform(new Point()).Y;
            var near=top+element.ActualHeight>scroll.VerticalOffset-1400&&top<scroll.VerticalOffset+scroll.ViewportHeight+1400;
            if(near&&element.Child==null)RenderBatch(batch);else if(!near&&element.Child!=null&&scroll is not SmoothScrollViewer{IsWheelAnimating:true})BatchPlaceholder(batch);
        }
    }
    private int CardColumns()=>content.ActualWidth>=1280?4:content.ActualWidth>=920?3:2;
    private void ResizeCards(object? sender,SizeChangedEventArgs e)
    {
        if(feedPanel==null||currentFeed==null||!e.WidthChanged)return;ResizeFeedCards(currentFeed);
    }
    private void ResizeFeedCards(FeedState state)
    {
        if(Math.Abs(state.ViewWidth-content.ActualWidth)<1)return;
        state.ViewWidth=content.ActualWidth;if(state.Columns!=CardColumns())ReflowFeedRows(state);foreach(var batch in state.Batches){batch.MeasuredHeight=0;if(batch.Element?.Child is StackPanel stack){foreach(var grid in stack.Children.OfType<UniformGrid>())grid.Columns=CardColumns();batch.Element.Height=double.NaN;}else BatchPlaceholder(batch);}if(feedScroll!=null)QueueFeedTailFill(state,feedScroll,navigation.Token);
    }
    private async Task RefreshFeed()
    {
        var state=currentFeed;if(state==null||state.Loading)return;var ct=navigation.Token;if(state.Batches.Count==0){state.Ended=false;await LoadFeedPage(state,ct);return;}state.Loading=true;state.LoadingToken=ct;var panel=feedPanel!;RemoveSectionState(panel);var feedback=SectionState(panel);panel.Children.Remove(feedback);panel.Children.Insert(0,feedback);feedback.Loading("");var previousOffset=state.Offset;
        try
        {
            const int index=0;feedScroll!.ScrollToTop();state.Scroll=0;
            var pn=searching||popular||following?1:state.RequestPage+1;if(following&&!searching)state.Offset="";var list=await FetchFeed(state,pn,ct);ct.ThrowIfCancellationRequested();
            if(list.Count==0){return;}
            var batch=new FeedBatch(list,1,$"新的{(searching?"搜索结果":"推荐")} · {DateTime.Now:HH:mm}");var prepared=RenderBatch(batch);var viewport=VisualTree.Descendants(prepared).OfType<ViewportCardRows<VideoCard>>().Single();await viewport.PrepareFirstViewportAsync(Math.Max(1,feedScroll.ViewportWidth),Math.Max(1,feedScroll.ViewportHeight),ct);ct.ThrowIfCancellationRequested();
            if(!searching)state.RequestPage=pn;var old=state.Batches[index];old.Label=$"刷新之前 · {DateTime.Now:HH:mm}";state.Batches.Insert(index,batch);
            old.MeasuredHeight=0;old.Element!.Height=double.NaN;feedPanel!.Children.Remove(old.Element);feedPanel.Children.Insert(index,RenderBatch(old));feedPanel.Children.Insert(index,prepared);state.Ended=false;
            feedScroll.ScrollToTop();
        }
        catch(Exception error) when(error is not OperationCanceledException)
        {
            if(!ct.IsCancellationRequested){state.Offset=previousOffset;feedback.Failed("刷新失败",error.Message,async()=>{state.ViewToken.ThrowIfCancellationRequested();await Run(RefreshFeed);});}
        }
        finally{if(state.LoadingToken==ct)state.Loading=false;if(feedback.Status==SectionStatus.Loading||ct.IsCancellationRequested)panel.Children.Remove(feedback);}
    }
    private async void ShowSearchPopup()
    {
        BuildSearchPopup([]);searchPopup.IsOpen=true;
        if(hotTopics.Count==0)try{hotTopics=await api.GetHotSearch();if(searchPopup.IsOpen&&search.Text.Length==0)BuildSearchPopup([]);}catch(Exception e){System.Diagnostics.Debug.WriteLine(e.Message);}
        if(search.Text.Length>0)ScheduleSuggestions();
    }
    private async void ScheduleSuggestions()
    {
        suggestions.Cancel();suggestions.Dispose();suggestions=new();var ct=suggestions.Token;var term=search.Text.Trim();
        try{if(term.Length==0){BuildSearchPopup([]);return;}await Task.Delay(250,ct);var values=await api.GetSearchSuggestions(term,ct);ct.ThrowIfCancellationRequested();if(searchPopup.IsOpen&&search.Text.Trim()==term)BuildSearchPopup(values);}
        catch(OperationCanceledException){}catch(Exception e){System.Diagnostics.Debug.WriteLine(e.Message);}
    }
    private void BuildSearchPopup(IReadOnlyList<string> suggestionsList)
    {
        var stack=new StackPanel{Width=464,Margin=new Thickness(22,18,22,20)};
        Button Choose(string text){var b=Button(text);b.HorizontalContentAlignment=HorizontalAlignment.Left;b.Click+=async(_,_)=>{search.Text=text;searchPopup.IsOpen=false;await Run(SubmitSearch);};return b;}
        if(suggestionsList.Count>0){foreach(var value in suggestionsList){var b=Choose(value);b.Height=42;stack.Children.Add(b);}}
        else
        {
            var heading=new DockPanel();var clear=Button("清空");clear.Foreground=Muted;clear.FontSize=12;clear.Click+=(_,_)=>{searchHistory.Clear();PersistSearchHistory();BuildSearchPopup([]);};DockPanel.SetDock(clear,Dock.Right);heading.Children.Add(clear);heading.Children.Add(Text("搜索历史",16,FontWeights.SemiBold));stack.Children.Add(heading);
            var wrap=new WrapPanel{Margin=new Thickness(0,10,0,14)};foreach(var item in searchHistory.Take(8)){var chip=new DockPanel{Margin=new Thickness(4,2,5,2)};var remove=IconButton(PackIconKind.Close,"删除搜索历史："+item,25);remove.Height=32;remove.Foreground=Muted;remove.Click+=(_,_)=>{searchHistory.Remove(item);PersistSearchHistory();BuildSearchPopup([]);};DockPanel.SetDock(remove,Dock.Right);chip.Children.Add(remove);var b=Choose(item);b.MaxWidth=170;b.Height=32;b.FontSize=13;var title=Text(item,13);title.TextTrimming=TextTrimming.CharacterEllipsis;b.Content=title;chip.Children.Add(b);wrap.Children.Add(new Border{Child=chip,Background=Surface,CornerRadius=new CornerRadius(8),Margin=new Thickness(0,0,10,10)});}if(searchHistory.Count==0){var empty=Text("暂无搜索历史",13);empty.Foreground=Muted;wrap.Children.Add(empty);}stack.Children.Add(wrap);stack.Children.Add(Text("热搜",16,FontWeights.SemiBold));
            var hot=new Grid{Margin=new Thickness(0,10,0,0)};hot.ColumnDefinitions.Add(new());hot.ColumnDefinitions.Add(new());for(int row=0;row<5;row++)hot.RowDefinitions.Add(new(){Height=new GridLength(40)});
            for(int i=0;i<hotTopics.Count;i++){var rank=i+1;var item=hotTopics[i];var b=Choose(item.Keyword);var row=new DockPanel();var n=Text(rank.ToString(),14,FontWeights.SemiBold);n.Foreground=rank<=3?Pink:Muted;n.Width=25;row.Children.Add(n);var label=Text(item.Keyword,13);label.TextTrimming=TextTrimming.CharacterEllipsis;row.Children.Add(label);b.Content=row;b.Padding=new Thickness(0,4,8,4);Grid.SetColumn(b,i<5?0:1);Grid.SetRow(b,i%5);hot.Children.Add(b);}stack.Children.Add(hot);
        }
        searchPopup.Child=new Border{Child=stack,Background=CanvasBrush,CornerRadius=new CornerRadius(20),BorderBrush=OutlineBrush,BorderThickness=new Thickness(1),Effect=new DropShadowEffect{BlurRadius=18,ShadowDepth=3,Opacity=.12}};
    }
}







