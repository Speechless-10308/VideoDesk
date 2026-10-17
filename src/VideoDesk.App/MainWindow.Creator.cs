using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using VideoDesk.Core;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private sealed class CreatorState(CreatorProfile profile,CreatorVideoPage videos)
    {
        public CreatorProfile Profile=profile;public readonly List<VideoCard> Videos=videos.Items.ToList();public long Total=videos.Total;public bool More=videos.HasMore,Loading,ReadFailed;public int Page=1,Tab;public string Query="",Offset="";
        public CreatorDynamicPage? Dynamics;public readonly List<CreatorCollection> Collections=[];public bool CollectionsLoaded,CollectionsMore;public int CollectionPage;public bool VideosFromCollection;
        public CreatorCollection? Collection;
        public readonly Dictionary<object,(double Width,double Height)> DynamicHeights=[];
    }
    private CreatorState? creatorState;
    private SmoothScrollViewer? creatorScroll;
    private StackPanel? creatorBody;
    private ViewportCardRows<VideoCard>? creatorVideoRows;
    private ViewportCardRows<CreatorCollection>? creatorCollectionRows;
    private ViewportListRows<CreatorDynamic>? creatorDynamicRows;
    private string creatorVideoContext="";
    private UnderlinedTabs? creatorTabs;
    private int creatorVersion;
    private void CreatorLink(FrameworkElement element,long mid)
    {
        if(mid<=0)return;element.Cursor=Cursors.Hand;element.ToolTip=element is TextBlock label?label.Text+" · 查看个人空间":"查看个人空间";element.PreviewMouseLeftButtonDown+=async(_,e)=>{e.Handled=true;await Run(()=>OpenCreator(mid));};
    }
    private async Task OpenCreator(long mid,CreatorState? saved=null,double savedOffset=0)
    {
        var ct=BeginPage();SelectNav(avatar);creatorVersion++;creatorState=null;
        var layout=new StackPanel();creatorScroll=new SmoothScrollViewer{Content=layout,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};content.Children.Add(WithDynamicSidebar(creatorScroll));
        var back=BackButton();back.HorizontalAlignment=HorizontalAlignment.Left;back.Margin=new Thickness(0,0,0,12);back.Click+=async(_,_)=>await Run(GoBack);layout.Children.Add(back);
        var placeholder=SectionState(layout);placeholder.Loading("正在加载个人空间…");
        try
        {
            if(saved==null){var profileTask=api.GetCreatorProfile(mid,ct);var videosTask=api.GetCreatorVideos(mid,ct:ct);await Task.WhenAll(profileTask,videosTask);ct.ThrowIfCancellationRequested();creatorState=new(profileTask.Result,videosTask.Result);}else{creatorState=saved;creatorState.Loading=false;creatorState.ReadFailed=false;}
            layout.Children.Remove(placeholder);var profile=creatorState.Profile;
            var bannerImage=new Image{Stretch=Stretch.UniformToFill};LoadImage(bannerImage,profile.Banner,1600);var banner=new ParallaxBanner(bannerImage,creatorScroll,Surface);var bannerHost=new Grid{Height=180};bannerHost.Children.Add(banner);layout.Children.Remove(back);back.VerticalAlignment=VerticalAlignment.Top;back.Margin=new Thickness(12);bannerHost.Children.Add(back);layout.Children.Add(bannerHost);
            var row=new Grid{Margin=new Thickness(12,22,12,24)};row.ColumnDefinitions.Add(new(){Width=new GridLength(100)});row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.Children.Add(UserAvatar(profile.Avatar,80,profile.Identity));
            var identity=new StackPanel{VerticalAlignment=VerticalAlignment.Center};identity.Children.Add(IdentityName(Text(profile.Name,25,FontWeights.SemiBold),profile.Level,profile.Senior,identity:profile.Identity));var sign=Text(profile.Signature,14);sign.Foreground=Muted;sign.TextWrapping=TextWrapping.Wrap;sign.Margin=new Thickness(0,8,20,0);identity.Children.Add(SelectableText(sign,"UP 主简介"));if(profile.Identity is {OfficialType:0 or 1} verified){var verification=Text("认证："+verified.OfficialTitle,12);verification.Foreground=Muted;verification.Margin=new Thickness(0,7,0,0);verification.ToolTip=verified.OfficialDescription;identity.Children.Add(verification);}Grid.SetColumn(identity,1);row.Children.Add(identity);
            var stats=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};foreach(var (label,count) in new[]{("粉丝",profile.Followers),("关注",profile.Following),("获赞",profile.Likes)}){var stat=new StackPanel{Width=92};var n=Text(BilibiliClient.CountLabel(count),20,FontWeights.SemiBold);n.HorizontalAlignment=HorizontalAlignment.Center;stat.Children.Add(n);var t=Text(label,13);t.Foreground=Muted;t.HorizontalAlignment=HorizontalAlignment.Center;t.Margin=new Thickness(0,5,0,0);stat.Children.Add(t);stats.Children.Add(stat);if(label!="获赞")stats.Children.Add(new Border{Width=1,Height=32,Background=Surface,Margin=new Thickness(7,0,7,0)});}Grid.SetColumn(stats,2);row.Children.Add(stats);
            if(account?.Mid!=mid){var follow=Button(profile.Followed?"已关注":"＋ 关注");follow.Width=112;follow.Background=profile.Followed?Surface:Pink;follow.Foreground=profile.Followed?Muted:Brushes.White;follow.Margin=new Thickness(20,0,0,0);follow.Click+=async(_,_)=>await Run(async()=>{if(!RequireLogin())return;follow.IsEnabled=false;try{var desired=!creatorState!.Profile.Followed;await api.SetFollow(mid,desired);ct.ThrowIfCancellationRequested();creatorState.Profile=creatorState.Profile with{Followed=desired};follow.Content=desired?"已关注":"＋ 关注";follow.Background=desired?Surface:Pink;follow.Foreground=desired?Muted:Brushes.White;}finally{follow.IsEnabled=true;}});Grid.SetColumn(follow,3);row.Children.Add(follow);}layout.Children.Add(row);
            var navigationRow=new DockPanel{Margin=new Thickness(5,0,6,16)};var field=new TextBox{Width=280,Height=40,Style=null,Background=Surface,BorderThickness=new Thickness(0),Padding=new Thickness(12,5,12,5),VerticalContentAlignment=VerticalAlignment.Center,FontSize=14};HintAssist.SetHint(field,"搜索TA的视频");field.KeyDown+=async(_,e)=>{if(e.Key!=Key.Enter)return;e.Handled=true;await Run(()=>SearchCreator(field.Text.Trim()));};var creatorSearch=InputSurface(field,"搜索TA的视频");DockPanel.SetDock(creatorSearch,Dock.Right);navigationRow.Children.Add(creatorSearch);
            creatorTabs=new(new[]{"投稿","动态","合集"}.Select((label,i)=>(label,i==0,(Func<Task>)(()=>Run(()=>ShowCreatorTab(i))))),17);navigationRow.Children.Add(creatorTabs);layout.Children.Add(navigationRow);creatorBody=new();layout.Children.Add(creatorBody);RenderCreatorVideos();
            var activeState=creatorState;var activeScroll=creatorScroll;creatorScroll.ScrollChanged+=async(_,e)=>{if(!ct.IsCancellationRequested&&e.VerticalChange>0&&activeScroll.ScrollableHeight-activeScroll.VerticalOffset<550&&creatorState==activeState&&!creatorState.Loading)await Run(LoadCreatorMore);};
            if(saved!=null){creatorTabs.Select(saved.Tab);if(saved.Collection!=null)RenderCreatorVideos();else await ShowCreatorTab(saved.Tab);await Dispatcher.InvokeAsync(()=>activeScroll.ScrollToVerticalOffset(savedOffset),System.Windows.Threading.DispatcherPriority.Loaded);}
            layout.SizeChanged+=(_,_)=>{if(creatorBody?.Children.OfType<UniformGrid>().FirstOrDefault() is {} grid)grid.Columns=CardColumns();};
        }
        catch(Exception error) when(error is not OperationCanceledException){if(!ct.IsCancellationRequested)placeholder.Failed("个人空间加载失败",error.Message,async()=>{ct.ThrowIfCancellationRequested();await Run(()=>OpenCreator(mid));});}
    }
    private async Task SearchCreator(string query)
    {
        var state=creatorState!;var ct=navigation.Token;var version=++creatorVersion;state.Loading=true;state.ReadFailed=false;creatorTabs!.Select(0);
        try{var result=await api.GetCreatorVideos(state.Profile.Mid,keyword:query,ct:ct);ct.ThrowIfCancellationRequested();if(version!=creatorVersion)return;state.Query=query;state.Collection=null;state.VideosFromCollection=false;state.Videos.Clear();state.Videos.AddRange(result.Items);state.Total=result.Total;state.More=result.HasMore;state.Page=1;state.Tab=0;RenderCreatorVideos();}
        finally{if(version==creatorVersion)state.Loading=false;}
    }
    private void RenderCreatorVideos()
    {
        var context=$"{creatorState!.Collection?.Id}:{creatorState.Query}";
        if(creatorVideoRows is {} existingRows&&existingRows.Parent==creatorBody&&creatorVideoContext==context){RemoveSectionState(creatorBody!);existingRows.SetItems(creatorState.Videos);CreatorEmpty(creatorState.Videos.Count,"暂无投稿");return;}
        creatorVideoContext=context;
        var state=creatorState!;creatorBody!.Children.Clear();var title=Text(state.Collection?.Title??(state.Query.Length>0?$"搜索“{state.Query}” · {state.Total} 个视频":$"全部投稿 · {state.Total} 个视频"),14);title.Foreground=Muted;title.TextTrimming=TextTrimming.CharacterEllipsis;title.Margin=new Thickness(7,0,7,20);creatorBody.Children.Add(title);
        if(state.Collection!=null){var back=Button("‹ 返回合集");back.HorizontalAlignment=HorizontalAlignment.Left;back.Margin=new Thickness(0,0,0,12);back.Click+=async(_,_)=>{state.Collection=null;await Run(()=>ShowCreatorTab(2));};creatorBody.Children.Insert(0,back);}
        creatorVideoRows=new(()=>creatorScroll,_=>CardColumns(),VideoTile);creatorVideoRows.SetItems(state.Videos);creatorBody.Children.Add(creatorVideoRows);CreatorEmpty(state.Videos.Count,"暂无投稿");
    }
    private void CreatorEmpty(int count,string text){if(count==0)SectionState(creatorBody!).Empty(text);}
    private async Task ShowCreatorTab(int tab)
    {
        if(tab!=1&&DynamicDrawerOpen)CloseDynamicSidebar();
        var state=creatorState!;var ct=navigation.Token;var version=++creatorVersion;var direction=tab.CompareTo(state.Tab);state.Tab=tab;state.Loading=true;state.ReadFailed=false;var body=creatorBody!;var feedback=SectionState(body);feedback.Loading("正在加载"+new[]{"投稿","动态","合集"}[tab]+"…");
        try{
        if(tab==0){state.Collection=null;if(state.Query.Length>0||state.Videos.Count==0||state.VideosFromCollection){var result=await api.GetCreatorVideos(state.Profile.Mid,keyword:state.Query,ct:ct);ct.ThrowIfCancellationRequested();if(version!=creatorVersion)return;state.Videos.Clear();state.Videos.AddRange(result.Items);state.Total=result.Total;state.More=result.HasMore;state.Page=1;state.VideosFromCollection=false;}RenderCreatorVideos();return;}
        if(tab==1)
        {
            state.Dynamics??=await api.GetCreatorDynamics(state.Profile.Mid,ct:ct);ct.ThrowIfCancellationRequested();if(version!=creatorVersion)return;state.Offset=state.Dynamics.Offset;creatorBody!.Children.Clear();creatorDynamicRows=new(()=>creatorScroll,CreatorDynamicCard,item=>item.Id,420){Measurements=state.DynamicHeights};creatorDynamicRows.SetItems(state.Dynamics.Items);creatorBody.Children.Add(creatorDynamicRows);CreatorEmpty(state.Dynamics.Items.Count,"暂无动态");
        }
        else
        {
            if(!state.CollectionsLoaded){var result=await api.GetCreatorCollections(state.Profile.Mid,ct:ct);ct.ThrowIfCancellationRequested();if(version!=creatorVersion)return;state.Collections.AddRange(result.Items);state.CollectionsMore=result.HasMore;state.CollectionPage=1;state.CollectionsLoaded=true;}RenderCreatorCollections();
        }
        }catch(Exception error) when(error is not OperationCanceledException){if(!ct.IsCancellationRequested&&version==creatorVersion)feedback.Failed("空间内容加载失败",error.Message,async()=>{ct.ThrowIfCancellationRequested();await Run(()=>ShowCreatorTab(tab));});}
        finally{if(version==creatorVersion)state.Loading=false;if(feedback.Status==SectionStatus.Loading||ct.IsCancellationRequested||version!=creatorVersion)body.Children.Remove(feedback);if(!ct.IsCancellationRequested&&version==creatorVersion&&feedback.Status!=SectionStatus.Failed)UiMotion.Enter(body,direction);}
    }
    private FrameworkElement CreatorDynamicCard(CreatorDynamic item)=>BuildDynamicCard(item);
    private void RenderCreatorCollections()
    {
        var state=creatorState!;
        if(creatorCollectionRows is {} existingRows&&existingRows.Parent==creatorBody){RemoveSectionState(creatorBody!);existingRows.SetItems(state.Collections);CreatorEmpty(state.Collections.Count,"暂无合集");return;}
        creatorBody!.Children.Clear();creatorCollectionRows=new(()=>creatorScroll,_=>CardColumns(),collection=>
        {
            var panel=new StackPanel();var cover=new Image{Height=155,Stretch=Stretch.UniformToFill};LoadImage(cover,collection.Cover);panel.Children.Add(new Border{Child=cover,Background=Surface,CornerRadius=new CornerRadius(8),ClipToBounds=true});var title=Text(collection.Title,16,FontWeights.Medium);title.TextTrimming=TextTrimming.CharacterEllipsis;title.Margin=new Thickness(10,14,10,8);panel.Children.Add(title);var count=Text($"{collection.Count} 个视频",13);count.Foreground=Muted;count.Margin=new Thickness(10,0,10,14);panel.Children.Add(count);var button=Button("");button.Height=double.NaN;button.Padding=new Thickness(0);button.Margin=new Thickness(7,0,7,22);button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.VerticalAlignment=VerticalAlignment.Top;button.Content=panel;button.Click+=async(_,_)=>await Run(async()=>{var ct=navigation.Token;var version=++creatorVersion;var result=await api.GetCreatorCollectionVideos(state.Profile.Mid,collection,ct:ct);ct.ThrowIfCancellationRequested();if(version!=creatorVersion)return;state.Collection=collection;state.VideosFromCollection=true;state.Videos.Clear();state.Videos.AddRange(result.Items);state.Total=result.Total;state.More=result.HasMore;state.Page=1;RenderCreatorVideos();});return button;
        }){RowHeightProvider=(_,_)=>250};creatorCollectionRows.SetItems(state.Collections);creatorBody.Children.Add(creatorCollectionRows);CreatorEmpty(state.Collections.Count,"暂无合集");
    }
    private async Task LoadCreatorMore()
    {
        var state=creatorState;if(state==null||state.Loading||state.ReadFailed)return;var ct=navigation.Token;var version=creatorVersion;state.Loading=true;var body=creatorBody!;var feedback=SectionState(body);feedback.Loading("正在加载更多空间内容…");
        try
        {
            if(state.Tab==0||state.Collection!=null){if(!state.More)return;var page=state.Collection!=null?await api.GetCreatorCollectionVideos(state.Profile.Mid,state.Collection,state.Page+1,ct):await api.GetCreatorVideos(state.Profile.Mid,state.Page+1,state.Query,ct:ct);ct.ThrowIfCancellationRequested();if(version!=creatorVersion)return;state.Page++;state.More=page.HasMore;var known=state.Videos.Select(v=>v.Bvid).ToHashSet();state.Videos.AddRange(page.Items.Where(v=>known.Add(v.Bvid)));creatorVideoRows!.SetItems(state.Videos);}
            else if(state.Tab==1&&state.Dynamics?.HasMore==true){var page=await api.GetCreatorDynamics(state.Profile.Mid,state.Offset,ct);ct.ThrowIfCancellationRequested();if(version!=creatorVersion)return;state.Offset=page.Offset;state.Dynamics=page with{Items=state.Dynamics.Items.Concat(page.Items).DistinctBy(d=>d.Id).ToArray()};creatorDynamicRows!.SetItems(state.Dynamics.Items);}
            else if(state.Tab==2&&state.CollectionsMore){var page=await api.GetCreatorCollections(state.Profile.Mid,state.CollectionPage+1,ct);ct.ThrowIfCancellationRequested();if(version!=creatorVersion)return;state.CollectionPage++;state.CollectionsMore=page.HasMore;state.Collections.AddRange(page.Items.Where(c=>state.Collections.All(old=>old.Id!=c.Id||old.Series!=c.Series)));RenderCreatorCollections();}
        }
        catch(Exception error) when(error is not OperationCanceledException){if(!ct.IsCancellationRequested&&version==creatorVersion){state.ReadFailed=true;feedback.Failed("更多空间内容加载失败",error.Message,async()=>{ct.ThrowIfCancellationRequested();state.ReadFailed=false;await Run(LoadCreatorMore);});}}
        finally{state.Loading=false;if(feedback.Status==SectionStatus.Loading||ct.IsCancellationRequested||version!=creatorVersion)body.Children.Remove(feedback);}
    }
}








