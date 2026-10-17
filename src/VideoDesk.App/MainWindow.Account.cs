using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using VideoDesk.Core;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private readonly List<WatchProgress> cloudHistory=[];
    private HistoryPage? historyCursor;
    private long selectedFolder;
    private Button? followAction,likeAction,coinAction,favoriteAction;
    private StackPanel? relatedCardsPanel;
    private bool accountStateReady,accountStateLoading;
    private long accountRevision,accountLoadRevision;
    private string accountPending="";
    private DockPanel? historyLayout;
    private StackPanel? historyRows;
    private SmoothScrollViewer? historyScroll;
    private DateTime historyFetched;
    private DateTimeOffset historyLocalRendered;
    private IReadOnlyList<FavoriteFolder> favoriteFolders=[];
    private DateTime foldersFetched;
    private Grid? favoritesLayout;
    private DockPanel? favoriteContent;
    private readonly Dictionary<long,Button> folderButtons=[];
    private readonly Dictionary<long,FavoritePage> favoriteFirstPages=[];
    private CancellationTokenSource folderNavigation=new();
    private long libraryAccountMid;
    private long favoritesAccountMid;
    private void BuildVideoSidebar()
    {
        if(videoSidebar==null||detail==null)return;videoSidebar.Children.Clear();var video=detail.Video;
        var up=new DockPanel{Margin=new Thickness(0,2,0,18)};var face=UserAvatar(detail.Avatar,52,mid:video.Mid);face.Margin=new Thickness(0,0,12,0);DockPanel.SetDock(face,Dock.Left);CreatorLink(face,video.Mid);up.Children.Add(face);
        var follow=Button("");followAction=follow;follow.Width=90;follow.VerticalAlignment=VerticalAlignment.Center;
        follow.Click+=async(_,_)=>await Run(async()=>
        {
            if(!RequireLogin())return;
            if(!accountStateReady){await LoadAccountActions(video.Bvid,video.Aid,video.Mid,navigation.Token);return;}
            await AccountAction("关注",async ct=>{var desired=!followingCreator;await SubmitAccountAction("关注",()=>api.SetFollow(video.Mid,desired),()=>followingCreator=desired,ct);});
        });DockPanel.SetDock(follow,Dock.Right);up.Children.Add(follow);
        var info=new StackPanel{VerticalAlignment=VerticalAlignment.Center};var name=Text(video.Creator,17,FontWeights.SemiBold);name.TextTrimming=TextTrimming.CharacterEllipsis;CreatorLink(name,video.Mid);info.Children.Add(IdentityName(name,mid:video.Mid));var hint=Text("UP 主",12);hint.Foreground=Muted;hint.Margin=new Thickness(0,5,0,0);info.Children.Add(hint);up.Children.Add(info);videoSidebar.Children.Add(CreatorInformation(up,hint,follow));var later=LaterButton(video);later.HorizontalAlignment=HorizontalAlignment.Right;later.Margin=new Thickness(0,0,0,12);videoSidebar.Children.Add(later);
        var actions=new UniformGrid{Columns=4,Margin=new Thickness(0,0,0,18)};
        var like=Interaction(PackIconKind.ThumbUpOutline,detail.Likes,"点赞");likeAction=like;
        like.Click+=async(_,_)=>await Run(()=>AccountAction("点赞",async ct=>
        {
            var desired=!videoState.Liked;
            await SubmitAccountAction("点赞",()=>api.SetLike(video.Bvid,desired),()=>{detail=detail! with{Likes=Math.Max(0,detail.Likes+(desired?1:-1))};videoState=videoState with{Liked=desired};},ct);
        }));actions.Children.Add(like);
        var coin=Interaction(PackIconKind.CircleMultipleOutline,detail.Coins,"投币");coinAction=coin;coin.Click+=async(_,_)=>await Run(()=>AccountAction("投币",CoinDialog));actions.Children.Add(coin);
        var fav=Interaction(PackIconKind.StarOutline,detail.Favorites,"收藏");favoriteAction=fav;fav.Click+=async(_,_)=>await Run(()=>AccountAction("收藏",FavoriteDialog));actions.Children.Add(fav);
        var share=Interaction(PackIconKind.ShareOutline,0,"分享");share.ToolTip="复制视频链接";share.Click+=(_,_)=>{Clipboard.SetText("https://www.bilibili.com/video/"+video.Bvid);Notify("视频链接已复制");};actions.Children.Add(share);actions.Margin=new Thickness(0);BuildVideoUnderlay(actions);
        if(BuildPlaybackCollection() is {} collection)videoSidebar.Children.Add(collection);
        if(detail.Parts.Count>1){var expander=new Expander{Tag="video-parts",Header=$"分集  {part!.Page}/{detail.Parts.Count}",FontSize=14,Margin=new Thickness(0,0,0,16)};var parts=new StackPanel();foreach(var p in detail.Parts){var b=Button(p.Page+". "+p.Title);b.Tag=p.Cid;b.HorizontalContentAlignment=HorizontalAlignment.Left;b.Foreground=p.Cid==part.Cid?Pink:Muted;b.Click+=async(_,_)=>{if(part?.Cid!=p.Cid)await Run(()=>ChangePart(p));};parts.Children.Add(b);}expander.Content=parts;videoSidebar.Children.Add(expander);}
        var heading=new DockPanel{Margin=new Thickness(0,7,0,15)};var mode=ModeSwitch(relatedMode,async m=>{relatedMode=m;await Run(RefreshRelated);});DockPanel.SetDock(mode,Dock.Right);heading.Children.Add(mode);heading.Children.Add(Text("相关推荐",16,FontWeights.SemiBold));videoSidebar.Children.Add(heading);
        relatedCardsPanel=new StackPanel();videoSidebar.Children.Add(relatedCardsPanel);RenderRelatedCards();RefreshAccountControls();
    }
    private void RenderRelatedCards()
    {
        if(relatedCardsPanel==null)return;relatedCardsPanel.Children.Clear();
        if(related.TryGetValue(relatedMode,out var list)){foreach(var card in list)relatedCardsPanel.Children.Add(RelatedTile(card));if(list.Count==0)SectionState(relatedCardsPanel).Empty("暂无相关推荐");}
        else SectionState(relatedCardsPanel).Loading("正在加载相关推荐…");
    }
    private void RefreshAccountControls()
    {
        if(detail==null)return;
        var enabled=!interactionBusy&&(account?.LoggedIn!=true||accountStateReady);
        if(followAction!=null)
        {
            followAction.Content=accountPending=="关注"?"提交中…":!accountStateReady&&account?.LoggedIn==true?(accountStateLoading?"加载中…":"重试状态"):followingCreator?"已关注":"＋ 关注";
            followAction.Background=followingCreator||!accountStateReady?Surface:Pink;followAction.Foreground=followingCreator||!accountStateReady?Muted:Brushes.White;
            followAction.IsEnabled=!interactionBusy&&!accountStateLoading;
        }
        void Update(Button? button,PackIconKind icon,long count,string label,bool selected)
        {
            if(button?.Content is not StackPanel stack)return;
            ((PackIcon)stack.Children[0]).Kind=icon;
            ((TextBlock)stack.Children[1]).Text=accountPending==label?"提交中…":count>0?BilibiliClient.CountLabel(count):label;
            button.Foreground=selected?Pink:Muted;((TextBlock)stack.Children[1]).Foreground=button.Foreground;button.IsEnabled=enabled;
            button.ToolTip=!accountStateReady&&account?.LoggedIn==true?"正在获取账号状态，可点击关注按钮重试":label;
            System.Windows.Automation.AutomationProperties.SetItemStatus(button,accountPending==label?"提交中":selected?"已"+label:"未"+label);
        }
        Update(likeAction,videoState.Liked?PackIconKind.ThumbUp:PackIconKind.ThumbUpOutline,detail.Likes,"点赞",videoState.Liked);
        Update(coinAction,PackIconKind.CircleMultipleOutline,detail.Coins,"投币",videoState.Coins>0);
        Update(favoriteAction,videoState.Favorited?PackIconKind.Star:PackIconKind.StarOutline,detail.Favorites,"收藏",videoState.Favorited);
    }
    private async Task AccountAction(string label,Func<CancellationToken,Task> operation)
    {
        if(!RequireLogin()||!accountStateReady||interactionBusy)return;
        var ct=navigation.Token;interactionBusy=true;accountRevision++;accountPending=label;RefreshAccountControls();
        try{await operation(ct);}
        catch(TimeoutException){if(!ct.IsCancellationRequested)accountStateReady=false;throw;}
        finally{if(!ct.IsCancellationRequested){interactionBusy=false;accountPending="";RefreshAccountControls();}}
    }
    private async Task SubmitAccountAction(string label,Func<Task> send,Action accepted,CancellationToken ct)
    {
        var watch=App.Diagnostics.Enabled?System.Diagnostics.Stopwatch.StartNew():null;await send();var acceptedMs=watch?.Elapsed.TotalMilliseconds??0;
        ct.ThrowIfCancellationRequested();accepted();accountPending="";RefreshAccountControls();
        if(watch!=null)
        {
            var updateMs=watch.Elapsed.TotalMilliseconds-acceptedMs;
            await App.Diagnostics.AppendAsync("action-timings.jsonl",()=>System.Text.Json.JsonSerializer.Serialize(new{at=DateTimeOffset.UtcNow,label,bvid=detail!.Video.Bvid,acceptedMs,updateMs,liked=videoState.Liked,coins=videoState.Coins,favorited=videoState.Favorited})+Environment.NewLine);
        }
    }
    private Button Interaction(PackIconKind icon,long count,string label,bool selected=false)
    {
        var stack=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};var glyph=Icon(icon,25);glyph.HorizontalAlignment=HorizontalAlignment.Center;stack.Children.Add(glyph);var value=Text(count>0?BilibiliClient.CountLabel(count):label,13);value.Foreground=selected?Pink:Muted;value.HorizontalAlignment=HorizontalAlignment.Center;value.Margin=new Thickness(0,7,0,0);stack.Children.Add(value);var b=Button("");b.Height=66;b.Padding=new Thickness(6);b.Content=stack;b.Foreground=selected?Pink:Muted;b.ToolTip=label;System.Windows.Automation.AutomationProperties.SetName(b,label);return b;
    }
    private async Task CoinDialog(CancellationToken ct)
    {
        var video=detail!;var limit=video.Copyright==1?2:1;var remaining=limit-videoState.Coins;if(remaining<=0){Notify("这个视频已达到投币上限");return;}
        double? balance=null;try{balance=await api.GetCoinBalance(ct);}catch(Exception error) when(error is not OperationCanceledException){System.Diagnostics.Debug.WriteLine(error.Message);}ct.ThrowIfCancellationRequested();var available=balance.HasValue?Math.Min(remaining,(int)Math.Floor(balance.Value)):remaining;if(available<1){Notify("硬币余额不足 1 枚");return;}var count=available;
        var panel=new StackPanel();panel.Children.Add(Text("为 UP 主投币",22,FontWeights.SemiBold));var hint=Text("支持喜欢的作品",13);hint.Foreground=Muted;hint.Margin=new Thickness(0,6,0,18);panel.Children.Add(hint);
        var preview=new DockPanel();var cover=new Image{Width=82,Height=46,Stretch=Stretch.UniformToFill,Margin=new Thickness(0,0,12,0)};LoadImage(cover,video.Video.Cover,200);DockPanel.SetDock(cover,Dock.Left);preview.Children.Add(cover);var title=Text(video.Video.Title,14,FontWeights.Medium);title.TextWrapping=TextWrapping.Wrap;title.TextTrimming=TextTrimming.CharacterEllipsis;title.MaxHeight=44;preview.Children.Add(title);panel.Children.Add(preview);
        var coins=new System.Windows.Controls.Primitives.UniformGrid{Columns=2,Margin=new Thickness(0,22,0,14)};var options=new Dictionary<int,Button>();
        void Select(int selected){count=selected;foreach(var (amount,button) in options){button.Background=amount==count?PalePink:Surface;button.Foreground=amount==count?Pink:Muted;button.BorderBrush=amount==count?Pink:Brushes.Transparent;}}
        for(int amount=1;amount<=2;amount++){var value=amount;var content=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center};var icon=Icon(amount==1?PackIconKind.CircleOutline:PackIconKind.CircleMultipleOutline,34);icon.HorizontalAlignment=HorizontalAlignment.Center;content.Children.Add(icon);var label=Text(amount+" 枚硬币",17,FontWeights.SemiBold);label.Margin=new Thickness(0,8,0,0);content.Children.Add(label);var button=Button("");button.Content=content;button.Height=108;button.Margin=new Thickness(amount==1?0:6,0,amount==1?6:0,0);button.BorderThickness=new Thickness(1);button.IsEnabled=amount<=available;button.ToolTip=amount<=available?"选择投 "+amount+" 枚":amount>remaining?(video.Copyright==1?"本视频已投过硬币，最多还能投 "+remaining+" 枚":"转载视频最多可投 1 枚"):"硬币余额不足 2 枚";button.Click+=(_,_)=>Select(value);options[amount]=button;coins.Children.Add(button);}Select(count);panel.Children.Add(coins);
        var status=Text((balance.HasValue?"余额 "+balance.Value.ToString("0.##")+" 枚":"余额暂未读取")+"    ·    本视频已投 "+videoState.Coins+" / "+limit+" 枚",12);status.Foreground=Muted;panel.Children.Add(status);var alsoLike=new CheckBox{Content="同时点赞",IsChecked=!videoState.Liked,IsEnabled=!videoState.Liked,Margin=new Thickness(0,16,0,12)};panel.Children.Add(alsoLike);var notice=Text("硬币投出后无法撤回"+(video.Copyright==1?"":" · 转载视频限投 1 枚"),12);notice.Foreground=Muted;panel.Children.Add(notice);
        if(!Dialog(panel,"确认投币",title:"投币"))return;var liked=alsoLike.IsChecked==true;
        await SubmitAccountAction("投币",()=>api.AddCoins(video.Video.Bvid,count,liked),()=>
        {
            detail=detail! with{Coins=detail.Coins+count,Likes=detail.Likes+(liked&&!videoState.Liked?1:0)};
            videoState=videoState with{Coins=videoState.Coins+count,Liked=videoState.Liked||liked};
        },ct);Notify("投币成功");
    }
    private async Task FavoriteDialog(CancellationToken ct)
    {
        var video=detail!;var folders=await api.GetFavoriteFolders(account!.Mid,video.Video.Aid,ct);ct.ThrowIfCancellationRequested();var panel=new StackPanel();panel.Children.Add(Text("添加到收藏夹",21,FontWeights.SemiBold));var boxes=new List<(FavoriteFolder Folder,CheckBox Box)>();
        foreach(var folder in folders){var box=new CheckBox{Content=$"{folder.Title}  ·  {folder.Count}",IsChecked=folder.Contains,Margin=new Thickness(0,14,0,0)};boxes.Add((folder,box));panel.Children.Add(box);}
        if(folders.Count==0){Notify("请先在收藏页新建一个收藏夹");return;}
        if(!Dialog(new SmoothScrollViewer{Content=panel,MaxHeight=420},"保存"))return;
        var add=boxes.Where(v=>v.Box.IsChecked==true&&!v.Folder.Contains).Select(v=>v.Folder.Id).ToArray();var remove=boxes.Where(v=>v.Box.IsChecked!=true&&v.Folder.Contains).Select(v=>v.Folder.Id).ToArray();
        if(add.Length==0&&remove.Length==0)return;
        var selected=boxes.Any(v=>v.Box.IsChecked==true);
        await SubmitAccountAction("收藏",()=>api.ChangeFavorites(video.Video.Aid,add,remove),()=>
        {
            detail=detail! with{Favorites=Math.Max(0,detail.Favorites+(selected&&!videoState.Favorited?1:!selected&&videoState.Favorited?-1:0))};
            videoState=videoState with{Favorited=selected};
        },ct);Notify("收藏已更新");
    }
    private Image AvatarImage(string url,double size)
    {
        var face=new Image{Width=size,Height=size,Stretch=Stretch.UniformToFill};face.Clip=new EllipseGeometry(new Point(size/2,size/2),size/2,size/2);LoadImage(face,url,(int)(size*2));return face;
    }
    private bool Dialog(FrameworkElement body,string accept,string? title=null,Func<bool>? canAccept=null)
    {
        var dialog=new Window{Title=title??"VideoDesk",Owner=this,Width=420,SizeToContent=SizeToContent.Height,MaxHeight=620,ResizeMode=ResizeMode.NoResize,WindowStyle=WindowStyle.None,AllowsTransparency=true,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brushes.Transparent,ShowInTaskbar=false};
        var content=new StackPanel{Margin=new Thickness(28)};content.Children.Add(body);var row=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,24,0,0)};var cancel=Button("取消");cancel.Margin=new Thickness(0,0,10,0);cancel.IsCancel=true;cancel.Click+=(_,_)=>dialog.DialogResult=false;row.Children.Add(cancel);var confirm=Button(accept);confirm.Background=Pink;confirm.Foreground=Brushes.White;confirm.MinWidth=88;confirm.Click+=(_,_)=>{if(canAccept?.Invoke()!=false)dialog.DialogResult=true;};row.Children.Add(confirm);content.Children.Add(row);dialog.Content=new Border{Child=content,Background=CanvasBrush,BorderBrush=new SolidColorBrush(Color.FromRgb(224,228,237)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(16)};return ShowModal(dialog)==true;
    }
    private bool? ShowModal(Window dialog)
    {
        var playerOverlay=overlay;playerOverlay?.Suspend();
        try{return dialog.ShowDialog();}finally{playerOverlay?.Resume();}
    }
    private bool Confirm(string heading,string description,string action)
    {
        var panel=new StackPanel();panel.Children.Add(Text(heading,20,FontWeights.SemiBold));var hint=Text(description,14);hint.TextWrapping=TextWrapping.Wrap;hint.Foreground=Muted;hint.Margin=new Thickness(0,14,0,0);panel.Children.Add(hint);return Dialog(panel,action);
    }
    private async Task ShowFavorites()
    {
        var ct=BeginPage();staticPage="favorites";SelectNav(favoritesNav);if(!RequireLogin()){var prompt=new StackPanel{VerticalAlignment=VerticalAlignment.Top};content.Children.Add(prompt);var state=SectionState(prompt);state.Empty("登录后可以查看 B站收藏夹");var login=Button("扫码登录");login.Click+=async(_,_)=>await Run(async()=>{await Login();if(account?.LoggedIn==true)await ShowFavorites();});state.AddAction(login);return;}
        if(favoritesLayout!=null&&favoritesAccountMid==account!.Mid&&DateTime.UtcNow-foldersFetched<TimeSpan.FromMinutes(1))
        {ShowCachedPage(favoritesLayout);return;}
        favoritesAccountMid=account!.Mid;
        if(favoritesLayout!=null&&favoritesAccountMid==account.Mid)ShowCachedPage(favoritesLayout);var pending=new StackPanel{VerticalAlignment=VerticalAlignment.Top};content.Children.Add(pending);IReadOnlyList<FavoriteFolder> folders=[];
        if(!await ReadSection(pending,async()=>{folders=await api.GetFavoriteFolders(account.Mid,ct:ct);},"收藏夹",ct,ShowFavorites))return;
        content.Children.Remove(pending);if(favoritesLayout!=null)content.Children.Remove(favoritesLayout);ct.ThrowIfCancellationRequested();favoriteFolders=folders;foldersFetched=DateTime.UtcNow;
        if(!folders.Any(f=>f.Id==selectedFolder))selectedFolder=folders.FirstOrDefault()?.Id??0;
        var layout=new Grid();layout.ColumnDefinitions.Add(new(){Width=new GridLength(245)});layout.ColumnDefinitions.Add(new());favoritesLayout=layout;folderButtons.Clear();
        var folderList=new StackPanel{Margin=new Thickness(0,2,18,0)};
        foreach(var folder in folders)
        {
            var b=Button("");var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new GridLength(35)});row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            row.Children.Add(Icon(PackIconKind.FolderOutline,23));var label=Text(folder.Title,15,FontWeights.Medium);label.TextTrimming=TextTrimming.CharacterEllipsis;Grid.SetColumn(label,1);row.Children.Add(label);var count=Text(folder.Count.ToString(),12);count.Foreground=Muted;count.Margin=new Thickness(8,0,0,0);Grid.SetColumn(count,2);row.Children.Add(count);
            b.Content=row;b.Height=54;b.HorizontalContentAlignment=HorizontalAlignment.Stretch;b.Margin=new Thickness(0,0,0,5);b.ToolTip=folder.Title;folderButtons[folder.Id]=b;
            b.Click+=async(_,_)=>{if(selectedFolder==folder.Id)return;selectedFolder=folder.Id;UpdateFolderSelection();await Run(()=>ShowFavoriteFolder(folder));};folderList.Children.Add(b);
        }
        UpdateFolderSelection();
        var create=Button("＋ 新建收藏夹");create.HorizontalContentAlignment=HorizontalAlignment.Left;create.Foreground=Muted;
        create.Click+=async(_,_)=>await Run(async()=>{var panel=new StackPanel();panel.Children.Add(Text("新建收藏夹",20,FontWeights.SemiBold));var title=new TextBox{Margin=new Thickness(0,20,0,0),MaxLength=40};panel.Children.Add(title);var publicBox=new CheckBox{Content="公开收藏夹",Margin=new Thickness(0,16,0,0)};panel.Children.Add(publicBox);if(!Dialog(panel,"创建")||string.IsNullOrWhiteSpace(title.Text))return;await api.CreateFavoriteFolder(title.Text.Trim(),publicBox.IsChecked==true);InvalidateFavorites();await ShowFavorites();});
        var folderPane=new DockPanel{Margin=new Thickness(0,0,14,0)};DockPanel.SetDock(create,Dock.Bottom);folderPane.Children.Add(create);folderPane.Children.Add(new SmoothScrollViewer{Content=folderList,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});layout.Children.Add(folderPane);
        favoriteContent=new DockPanel{Margin=new Thickness(18,0,0,0)};Grid.SetColumn(favoriteContent,1);layout.Children.Add(favoriteContent);content.Children.Add(layout);
        var selected=folders.FirstOrDefault(f=>f.Id==selectedFolder);if(selected==null){favoriteContent.Children.Add(Text("新建收藏夹，开始收藏喜欢的视频",16));return;}await ShowFavoriteFolder(selected);
    }
    private void UpdateFolderSelection()
    {
        foreach(var (id,button) in folderButtons){button.Background=id==selectedFolder?PalePink:Brushes.Transparent;button.Foreground=id==selectedFolder?Pink:Muted;if(button.Content is Grid row&&row.Children[0] is PackIcon icon)icon.Kind=id==selectedFolder?PackIconKind.Folder:PackIconKind.FolderOutline;}
    }
    private void InvalidateFavorites(){foldersFetched=DateTime.MinValue;favoriteFirstPages.Clear();}
    private async Task ShowFavoriteFolder(FavoriteFolder selected)
    {
        folderNavigation.Cancel();folderNavigation.Dispose();folderNavigation=CancellationTokenSource.CreateLinkedTokenSource(navigation.Token);var ct=folderNavigation.Token;
        if(!favoriteFirstPages.TryGetValue(selected.Id,out var first))
        {
            var panel=favoriteContent!;RemoveSectionState(panel);var feedback=SectionState(panel);DockPanel.SetDock(feedback,Dock.Top);panel.Children.Remove(feedback);panel.Children.Insert(0,feedback);feedback.Loading("正在加载“"+selected.Title+"”…");
            try{first=await api.GetFavoriteVideos(selected.Id,1,ct);ct.ThrowIfCancellationRequested();if(favoriteFirstPages.Count>=8)favoriteFirstPages.Remove(favoriteFirstPages.Keys.First());favoriteFirstPages[selected.Id]=first;}
            catch(Exception error) when(error is not OperationCanceledException){if(!ct.IsCancellationRequested)feedback.Failed("收藏视频加载失败",error.Message,async()=>{ct.ThrowIfCancellationRequested();await Run(()=>ShowFavoriteFolder(selected));});return;}
            finally{if(feedback.Status==SectionStatus.Loading||ct.IsCancellationRequested)panel.Children.Remove(feedback);}
        }
        ct.ThrowIfCancellationRequested();var right=new DockPanel();var header=new DockPanel{Margin=new Thickness(0,4,0,22)};var manage=Button("管理");manage.Foreground=Muted;
        manage.Click+=(_,_)=>{var menu=new ContextMenu();var rename=new MenuItem{Header="重命名"};rename.Click+=async(_,_)=>await Run(async()=>{var title=new TextBox{Text=selected.Title,MaxLength=40,Margin=new Thickness(0,8,0,0)};if(Dialog(title,"保存","重命名收藏夹")&&!string.IsNullOrWhiteSpace(title.Text)){await api.RenameFavoriteFolder(selected.Id,title.Text.Trim(),selected.Public);InvalidateFavorites();await ShowFavorites();}});menu.Items.Add(rename);var delete=new MenuItem{Header="删除收藏夹"};delete.Click+=async(_,_)=>await Run(async()=>{if(Confirm("删除收藏夹？","收藏夹和其中的收藏记录将从 B站账号中删除。","删除")){await api.DeleteFavoriteFolder(selected.Id);selectedFolder=0;InvalidateFavorites();await ShowFavorites();}});menu.Items.Add(delete);manage.ContextMenu=menu;menu.IsOpen=true;};DockPanel.SetDock(manage,Dock.Right);header.Children.Add(manage);
        var titles=new StackPanel();var titleText=Text(selected.Title,25,FontWeights.SemiBold);titleText.TextTrimming=TextTrimming.CharacterEllipsis;titles.Children.Add(titleText);var countText=Text(first.Total+" 个视频",14);countText.Foreground=Muted;countText.Margin=new Thickness(0,9,0,0);titles.Children.Add(countText);header.Children.Add(titles);DockPanel.SetDock(header,Dock.Top);right.Children.Add(header);
        var scroll=new SmoothScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        var cards=new ViewportCardRows<VideoCard>(()=>scroll,w=>w>=850?3:2,video=>{var tile=VideoTile(video);tile.ContextMenu=new ContextMenu();var remove=new MenuItem{Header="从此收藏夹移除"};remove.Click+=async(_,_)=>await Run(async()=>{if(Confirm("移除收藏？",$"从“{selected.Title}”移除这个视频。","移除")){await api.ChangeFavorites(video.Aid,[],[selected.Id]);InvalidateFavorites();await ShowFavorites();}});tile.ContextMenu.Items.Add(remove);return tile;});scroll.Content=cards;right.Children.Add(scroll);int pn=1;bool busy=false,ended=!first.HasMore;
        void Add(IReadOnlyList<VideoCard> items)=>cards.Append(items);
        Add(first.Items);if(first.Items.Count==0){var empty=new StackPanel();DockPanel.SetDock(empty,Dock.Top);right.Children.Insert(1,empty);SectionState(empty).Empty("这个收藏夹还没有视频");}
        async Task LoadMore(){if(busy||ended)return;busy=true;try{if(!await ReadSection(right,async()=>{var videos=await api.GetFavoriteVideos(selected.Id,pn+1,ct);ct.ThrowIfCancellationRequested();if(selectedFolder!=selected.Id)return;pn++;ended=!videos.HasMore;Add(videos.Items);},"更多收藏视频",ct,async()=>{ended=false;await LoadMore();}))ended=true;}finally{busy=false;}}
        scroll.ScrollChanged+=async(_,e)=>{if(e.VerticalChange>0&&scroll.ScrollableHeight-scroll.VerticalOffset<500)await Run(LoadMore);};
        favoriteContent!.Children.Clear();favoriteContent.Children.Add(right);
    }

}








