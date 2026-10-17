using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using VideoDesk.Core;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private sealed class FollowingState
    {
        public readonly System.Collections.ObjectModel.ObservableCollection<CreatorDynamic> Items=[];
        public string Offset="";public bool Loaded,More,Loading;public double Scroll;public CancellationToken LoadingToken;
    }
    private sealed class PopularState
    {
        public readonly List<PopularEntry> Items=[];public int Page;public bool More=true,Loading;public double Scroll;public string Description="";public CancellationToken LoadingToken;
    }
    private readonly Dictionary<bool,FollowingState> followingFeeds=[];
    private readonly Dictionary<string,PopularState> popularFeeds=[];
    private readonly Dictionary<(bool Music,DiscoveryMode Mode),IReadOnlyList<PopularPeriod>> popularPeriods=[];
    private FollowingState? followingState;
    private PopularState? popularState;
    private bool followingVideoOnly;
    private PopularCategory popularCategory;
    private int weeklyPeriod,musicPeriod,rankingRegion;
    private SmoothScrollViewer? popularScroll;
    private ListBox? followingList;
    private ScrollViewer? followingScroll;
    private StackPanel? popularBody;
    private UnderlinedTabs? followingTabs,popularTabs;
    private static readonly (string Label,int Id)[] RankingRegions=[("全部",0),("番剧",-1),("国创",-4),("纪录片",-3),("电影",-2),("电视剧",-5),("综艺",-7),("动画",1005),("游戏",1008),("鬼畜",1007),("音乐",1003),("舞蹈",1004),("影视",1001),("娱乐",1002),("知识",1010),("科技数码",1012),("美食",1020),("汽车",1013),("时尚美妆",1014),("体育运动",1018),("动物",1024)];

    private void RememberSocialScroll()
    {
        if(followingState!=null)followingState.Scroll=followingScroll?.VerticalOffset??followingState.Scroll;
        if(popularState!=null)popularState.Scroll=popularScroll?.VerticalOffset??popularState.Scroll;
        followingList=null;followingScroll=null;popularScroll=null;popularBody=null;
    }
    private async Task ShowFollowing()
    {
        var ct=BeginPage();staticPage="following";SelectNav(homeNav);var filter=followingVideoOnly;
        if(!followingFeeds.TryGetValue(filter,out var state))followingFeeds[filter]=state=new();followingState=state;state.Loading=false;
        var layout=new DockPanel();var header=DiscoveryHeader(RefreshFollowing,false);DockPanel.SetDock(header,Dock.Top);layout.Children.Add(header);
        var feed=new DockPanel();followingTabs??=new UnderlinedTabs(new[]{("全部",false),("视频动态",true)}.Select(t=>(t.Item1,t.Item2==filter,(Func<Task>)(async()=>{var direction=t.Item2.CompareTo(followingVideoOnly);followingVideoOnly=t.Item2;await Run(ShowHome);UiMotion.Enter(followingList,direction);}))),15){Margin=new Thickness(4,0,0,18)};if(followingTabs.Parent is Panel previousTabs)previousTabs.Children.Remove(followingTabs);followingTabs.Select(filter?1:0);DockPanel.SetDock(followingTabs,Dock.Top);feed.Children.Add(followingTabs);
        if(account?.LoggedIn!=true)
        {
            var prompt=new StackPanel();SectionState(prompt).Empty("登录后查看关注动态");var login=Button("扫码登录");login.HorizontalAlignment=HorizontalAlignment.Left;login.Click+=async(_,_)=>await Run(async()=>{await Login();if(account?.LoggedIn==true)await ShowHome();});prompt.Children.Add(login);feed.Children.Add(prompt);layout.Children.Add(feed);content.Children.Add(layout);return;
        }
        var footer=new StackPanel{Margin=new Thickness(7,8,7,8)};DockPanel.SetDock(footer,Dock.Bottom);feed.Children.Add(footer);
        var template=new DataTemplate{VisualTree=new FrameworkElementFactory(typeof(ContentPresenter))};template.VisualTree.SetBinding(ContentPresenter.ContentProperty,new Binding{Converter=new DynamicCardConverter(this)});
        var itemStyle=new Style(typeof(ListBoxItem));itemStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(0)));itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0)));itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));itemStyle.Setters.Add(new Setter(UIElement.FocusableProperty,false));
        var itemTemplate=new ControlTemplate(typeof(ListBoxItem)){VisualTree=new FrameworkElementFactory(typeof(ContentPresenter))};itemStyle.Setters.Add(new Setter(Control.TemplateProperty,itemTemplate));
        // Keep the ItemsPresenter inside the scroll viewer: CanContentScroll and
        // pixel scrolling let the virtualizing panel realize only nearby cards.
        var scrollHost=new FrameworkElementFactory(typeof(SmoothScrollViewer),"PART_ScrollViewer");scrollHost.SetValue(ScrollViewer.CanContentScrollProperty,true);scrollHost.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty,ScrollBarVisibility.Disabled);scrollHost.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty,ScrollBarVisibility.Auto);scrollHost.SetValue(UIElement.FocusableProperty,false);scrollHost.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        var list=new ListBox{ItemsSource=state.Items,ItemTemplate=template,ItemContainerStyle=itemStyle,Template=new ControlTemplate(typeof(ListBox)){VisualTree=scrollHost},Background=CanvasBrush,BorderThickness=new Thickness(0),Padding=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Stretch};
        ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);ScrollViewer.SetCanContentScroll(list,true);VirtualizingPanel.SetIsVirtualizing(list,true);VirtualizingPanel.SetVirtualizationMode(list,VirtualizationMode.Recycling);VirtualizingPanel.SetScrollUnit(list,ScrollUnit.Pixel);
        followingList=list;feed.Children.Add(list);layout.Children.Add(WithDynamicSidebar(feed));content.Children.Add(layout);
        list.AddHandler(ScrollViewer.ScrollChangedEvent,new ScrollChangedEventHandler(async(_,e)=>
        {
            if(e.OriginalSource is not ScrollViewer scroll||!ReferenceEquals(scroll,VisualTree.Descendants(list).OfType<ScrollViewer>().FirstOrDefault()))return;followingScroll=scroll;
            if(e.VerticalChange>0&&scroll.ScrollableHeight-scroll.VerticalOffset<600&&state.More&&!state.Loading&&!ct.IsCancellationRequested)await Run(()=>LoadFollowing(state,filter,footer,ct));
        }));
        if(!state.Loaded)await LoadFollowing(state,filter,footer,ct);
        else if(state.Items.Count==0)SectionState(footer).Empty("暂无关注动态");
        await Dispatcher.InvokeAsync(()=>{followingScroll=VisualTree.Descendants(list).OfType<ScrollViewer>().FirstOrDefault();followingScroll?.ScrollToVerticalOffset(state.Scroll);},DispatcherPriority.Loaded);
    }
    private async Task LoadFollowing(FollowingState state,bool videoOnly,Panel footer,CancellationToken ct)
    {
        if(state.Loading)return;state.Loading=true;state.LoadingToken=ct;RemoveSectionState(footer);var feedback=SectionState(footer);feedback.Loading("");
        try
        {
            var previous=state.Offset;var page=await api.GetFollowingDynamics(videoOnly,previous,ct);ct.ThrowIfCancellationRequested();
            foreach(var item in page.Items.Where(item=>state.Items.All(old=>old.Id!=item.Id)))state.Items.Add(item);
            state.Offset=page.Offset;state.More=page.HasMore&&page.Offset.Length>0&&page.Offset!=previous;state.Loaded=true;
            if(state.Items.Count==0)feedback.Empty("暂无关注动态");
        }
        catch(Exception error) when(error is not OperationCanceledException){if(!ct.IsCancellationRequested)feedback.Failed("关注动态加载失败",error.Message,()=>Run(()=>LoadFollowing(state,videoOnly,footer,ct)));}
        finally{if(state.LoadingToken==ct)state.Loading=false;if(feedback.Status==SectionStatus.Loading||ct.IsCancellationRequested)footer.Children.Remove(feedback);}
    }
    private async Task RefreshFollowing(){followingFeeds.Remove(followingVideoOnly);CloseDynamicSidebar();await ShowHome();}
    private sealed class DynamicCardConverter(MainWindow owner):IValueConverter
    {
        public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>value is CreatorDynamic item?owner.BuildDynamicCard(item):DependencyProperty.UnsetValue;
        public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
    }

    private string PopularKey=>$"{popularCategory}:{homeMode}:{(popularCategory==PopularCategory.Weekly?weeklyPeriod:popularCategory==PopularCategory.Music?musicPeriod:0)}:{(popularCategory==PopularCategory.Ranking?rankingRegion:0)}";
    private async Task ShowPopular()
    {
        var ct=BeginPage();staticPage="popular";SelectNav(homeNav);var category=popularCategory;var mode=homeMode;
        var layout=new DockPanel();var heading=DiscoveryHeader(RefreshPopular,true);DockPanel.SetDock(heading,Dock.Top);layout.Children.Add(heading);
        var labels=new[]{"综合热门","每周必看","入站必刷","排行榜","全站音乐榜"};
        popularTabs??=new UnderlinedTabs(labels.Select((label,i)=>(label,(int)category==i,(Func<Task>)(async()=>{var direction=i.CompareTo((int)popularCategory);popularCategory=(PopularCategory)i;await Run(ShowHome);UiMotion.Enter(popularBody,direction);}))),15){Margin=new Thickness(4,0,0,18)};if(popularTabs.Parent is Panel previousTabs)previousTabs.Children.Remove(popularTabs);popularTabs.Select((int)category);DockPanel.SetDock(popularTabs,Dock.Top);layout.Children.Add(popularTabs);
        var options=new StackPanel{Margin=new Thickness(7,0,7,22)};DockPanel.SetDock(options,Dock.Top);layout.Children.Add(options);
        popularBody=new();popularScroll=new SmoothScrollViewer{Content=popularBody,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};layout.Children.Add(popularScroll);content.Children.Add(layout);
        if(category is PopularCategory.Weekly or PopularCategory.Music)
        {
            var music=category==PopularCategory.Music;IReadOnlyList<PopularPeriod> periods=[];
            var read=await ReadSection(options,async()=>{if(!popularPeriods.TryGetValue((music,mode),out periods!))popularPeriods[(music,mode)]=periods=await api.GetPopularPeriods(music,mode,ct);},"期数",ct,ShowHome);if(!read)return;
            if(periods.Count==0){SectionState(options).Empty("暂无榜单");return;}
            var selected=music?musicPeriod:weeklyPeriod;if(periods.All(p=>p.Id!=selected))selected=periods[0].Id;if(music)musicPeriod=selected;else weeklyPeriod=selected;
            var chooser=new ComboBox{Style=(Style)FindResource("CompactChoice"),ItemsSource=periods,DisplayMemberPath=nameof(PopularPeriod.Label),SelectedValuePath=nameof(PopularPeriod.Id),SelectedValue=selected,Width=280,HorizontalAlignment=HorizontalAlignment.Left};chooser.SelectionChanged+=async(_,_)=>{if(chooser.SelectedItem is not PopularPeriod chosen||ct.IsCancellationRequested)return;if(music)musicPeriod=chosen.Id;else weeklyPeriod=chosen.Id;await Run(ShowHome);};options.Children.Add(chooser);
        }
        else if(category==PopularCategory.Ranking)
        {
            var filters=new WrapPanel();foreach(var (label,id) in RankingRegions){var b=Button(label);b.Height=32;b.FontSize=13;b.Margin=new Thickness(0,0,6,8);b.Background=rankingRegion==id?PalePink:Surface;b.Foreground=rankingRegion==id?Pink:Ink;b.Click+=async(_,_)=>{rankingRegion=id;await Run(ShowHome);};filters.Children.Add(b);}options.Children.Add(filters);
        }
        if(!popularFeeds.TryGetValue(PopularKey,out var state))popularFeeds[PopularKey]=state=new();popularState=state;state.Loading=false;
        while(popularFeeds.Count>12){var oldest=popularFeeds.Keys.First(k=>k!=PopularKey);popularFeeds.Remove(oldest);}
        RenderPopular(state,category);
        var scroll=popularScroll;scroll.ScrollChanged+=async(_,e)=>{if(e.VerticalChange>0&&scroll.ScrollableHeight-scroll.VerticalOffset<650&&state.More&&!state.Loading&&!ct.IsCancellationRequested)await Run(()=>LoadPopular(state,category,mode,ct));};
        if(state.Page==0)await LoadPopular(state,category,mode,ct);
        ct.ThrowIfCancellationRequested();await Dispatcher.InvokeAsync(()=>scroll.ScrollToVerticalOffset(state.Scroll),DispatcherPriority.Loaded);
    }
    private async Task LoadPopular(PopularState state,PopularCategory category,DiscoveryMode mode,CancellationToken ct)
    {
        if(state.Loading||!state.More)return;state.Loading=true;state.LoadingToken=ct;var target=popularBody!;RemoveSectionState(target);var feedback=SectionState(target);feedback.Loading("");
        try
        {
            var page=await api.GetPopularPage(category,mode,state.Page+1,category==PopularCategory.Music?musicPeriod:weeklyPeriod,rankingRegion,ct);ct.ThrowIfCancellationRequested();state.Page++;state.More=page.HasMore;state.Description=page.Description;
            foreach(var item in page.Items.Where(item=>state.Items.All(old=>item.Video.Bvid.Length>0?old.Video.Bvid!=item.Video.Bvid:old.Rank!=item.Rank)))state.Items.Add(item);
            RenderPopular(state,category);
        }
        catch(Exception error) when(error is not OperationCanceledException){if(!ct.IsCancellationRequested)feedback.Failed("热门内容加载失败",error.Message,()=>Run(()=>LoadPopular(state,category,mode,ct)));}
        finally{if(state.LoadingToken==ct)state.Loading=false;if(feedback.Status==SectionStatus.Loading||ct.IsCancellationRequested)target.Children.Remove(feedback);}
    }
    private void RenderPopular(PopularState state,PopularCategory category)
    {
        var body=popularBody!;
        if(body.Children.OfType<ViewportCardRows<PopularEntry>>().FirstOrDefault() is {} existing&&existing.Tag is PopularCategory current&&current==category){existing.SetItems(state.Items);return;}
        if(body.Children.OfType<ViewportListRows<PopularEntry>>().FirstOrDefault() is {} existingList&&existingList.Tag is PopularCategory currentList&&currentList==category){existingList.SetItems(state.Items);return;}
        body.Children.Clear();
        if(state.Description.Length>0){var text=Text(state.Description,13);text.Foreground=Muted;text.TextWrapping=TextWrapping.Wrap;text.Margin=new Thickness(7,0,7,20);body.Children.Add(text);}
        if(category is PopularCategory.Ranking or PopularCategory.Music)
        {
            var rows=new ViewportListRows<PopularEntry>(()=>popularScroll,PopularRow,item=>(item.Video.Bvid,item.Rank,item.MusicTitle),140){Tag=category};rows.SetItems(state.Items);body.Children.Add(rows);
        }
        else
        {
            var rows=new ViewportCardRows<PopularEntry>(()=>popularScroll,_=>CardColumns(),item=>PopularCard(item,category)){Tag=category,ExtraRowHeight=category is PopularCategory.Weekly or PopularCategory.Precious?64:0};rows.SetItems(state.Items);body.Children.Add(rows);
        }
        if(state.Page>0&&state.Items.Count==0)SectionState(body).Empty("暂无视频");
    }
    private FrameworkElement PopularCard(PopularEntry item,PopularCategory category)
    {
        if(category==PopularCategory.General)return VideoTile(item.Video);
        var video=item.Video;var card=new StackPanel();var cover=CardCover(video.Cover,video.Duration,$"▷ {BilibiliClient.CountLabel(video.Views)}    ▤ {BilibiliClient.CountLabel(video.Danmaku)}",out _);card.Children.Add(cover);card.Children.Add(CardTitle(video.Title));card.Children.Add(CardMetadata(video));
        var note=category==PopularCategory.Weekly?item.HotComment:item.Recommendation;
        if(note.Length>0)
        {
            var warm=new SolidColorBrush(Color.FromRgb(242,139,38));var content=new Grid();content.ColumnDefinitions.Add(new(){Width=new GridLength(24)});content.ColumnDefinitions.Add(new());
            var symbol=Icon(category==PopularCategory.Weekly?PackIconKind.FormatQuoteOpen:PackIconKind.MedalOutline,18);symbol.Foreground=warm;symbol.VerticalAlignment=VerticalAlignment.Top;symbol.Margin=new Thickness(0,2,0,0);content.Children.Add(symbol);
            var text=Text(note,12);text.Foreground=category==PopularCategory.Weekly?Muted:warm;text.TextWrapping=TextWrapping.Wrap;text.TextTrimming=TextTrimming.CharacterEllipsis;text.LineHeight=18;text.MaxHeight=36;text.ToolTip=note;Grid.SetColumn(text,1);content.Children.Add(text);
            card.Children.Add(new Border{Child=content,Background=new SolidColorBrush(Color.FromRgb(255,247,237)),CornerRadius=new CornerRadius(12),Padding=new Thickness(9,7,9,7),Margin=new Thickness(8,0,8,12),Tag=category==PopularCategory.Weekly?"weekly-hot-comment":"precious-reason"});
        }
        var button=CardButton(card,video.Title,()=>OpenVideo(video.Bvid));button.Tag="popular-featured-card";AttachCardPreview(button,cover,video);AddLaterMenu(button,video);button.SizeChanged+=(_,e)=>cover.Height=Math.Max(100,e.NewSize.Width*9/16);return button;
    }
    private FrameworkElement PopularRow(PopularEntry entry)
    {
        var video=entry.Video;var row=new Grid{Margin=new Thickness(7,0,7,0)};row.ColumnDefinitions.Add(new(){Width=new GridLength(50)});row.ColumnDefinitions.Add(new(){Width=new GridLength(214)});row.ColumnDefinitions.Add(new());
        var rank=Text(entry.Rank.ToString(),26,FontWeights.SemiBold);rank.Foreground=entry.Rank<=3?Pink:Muted;rank.HorizontalAlignment=HorizontalAlignment.Center;row.Children.Add(rank);
        var cover=CardCover(video.Cover,video.Duration,"",out _);cover.Width=192;cover.Height=108;cover.HorizontalAlignment=HorizontalAlignment.Left;Grid.SetColumn(cover,1);row.Children.Add(cover);
        var words=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,20,0)};var title=Text(entry.MusicTitle.Length>0?entry.MusicTitle:video.Title,16,FontWeights.SemiBold);title.TextWrapping=TextWrapping.Wrap;title.MaxHeight=48;words.Children.Add(title);
        var author=Text(video.Creator,13);author.Foreground=Muted;author.Margin=new Thickness(0,12,0,6);words.Children.Add(author);var stats=Text(entry.MusicTitle.Length>0?"热度 "+BilibiliClient.CountLabel(entry.Heat):"▷ "+BilibiliClient.CountLabel(video.Views)+"    ▤ "+BilibiliClient.CountLabel(video.Danmaku),12);stats.Foreground=Muted;words.Children.Add(stats);Grid.SetColumn(words,2);row.Children.Add(words);
        var button=Button("");button.Content=row;button.Height=double.NaN;button.Padding=new Thickness(8,16,8,16);button.Margin=new Thickness(0,0,0,8);button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.Background=RaisedBrush;ButtonAssist.SetCornerRadius(button,new CornerRadius(20));button.Tag="ranking-row";button.ToolTip=video.Title;
        button.Click+=async(_,_)=>await Run(()=>video.Bvid.Length>0?OpenVideo(video.Bvid):NavigateCommentLink(entry.Url));return button;
    }
    private async Task RefreshPopular(){popularFeeds.Remove(PopularKey);await ShowHome();}
}
