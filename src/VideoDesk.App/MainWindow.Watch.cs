using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using VideoDesk.Core;
using MaterialDesignThemes.Wpf;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private readonly Dictionary<long,Task<CreatorSummary>> creatorSummaries=[];
    private readonly SemaphoreSlim summarySlots=new(2);
    private VideoCollectionContext? playbackCollection;
    private int collectionPage=1;
    private TextBlock? onlineText;
    private DateTime lastBufferPoll;

    private FrameworkElement IdentityName(TextBlock name,int level=-1,bool senior=false,long mid=0,UserIdentity? identity=null)
    {
        var levelSlot=new ContentControl{Margin=new Thickness(6,0,0,0),VerticalAlignment=VerticalAlignment.Center,Visibility=Visibility.Collapsed};var vipSlot=new ContentControl{Margin=new Thickness(6,0,0,0),VerticalAlignment=VerticalAlignment.Center,Visibility=Visibility.Collapsed};var row=new IdentityNameRow(name,levelSlot,vipSlot);
        void ApplyLevel(int n,bool advanced){if(n<0||n>6)return;levelSlot.Content=OfficialIcons.Level(n,advanced,name.FontSize>=20?16:12);levelSlot.Visibility=Visibility.Visible;}
        void ApplyIdentity(UserIdentity? value){vipSlot.Content=VipBadge(value);vipSlot.Visibility=vipSlot.Content==null?Visibility.Collapsed:Visibility.Visible;if(value?.IsVip==true&&value.NicknameColor.Length>0)name.Foreground=IdentityColor(value.NicknameColor,name.Foreground);}
        ApplyLevel(level,senior);ApplyIdentity(identity);
        if(mid<=0||level>=0&&identity!=null)return row;var ct=navigation.Token;
        row.Loaded+=async(_,_)=>{try{var summary=await CreatorSummaryCached(mid,ct);if(ct.IsCancellationRequested||!row.IsLoaded)return;if(level<0)ApplyLevel(summary.Level,summary.Senior);if(identity==null)ApplyIdentity(summary.Identity);}catch(OperationCanceledException){}catch(Exception error){Debug.WriteLine(error.Message);}};return row;
    }
    private Task<CreatorSummary> CreatorSummaryCached(long mid,CancellationToken ct)
    {
        if(creatorSummaries.TryGetValue(mid,out var existing)&&!existing.IsCanceled&&!existing.IsFaulted)return existing.WaitAsync(ct);
        async Task<CreatorSummary> Fetch(){await summarySlots.WaitAsync(ct);try{return await api.GetCreatorSummary(mid,ct);}finally{summarySlots.Release();}}
        var task=Fetch();creatorSummaries[mid]=task;if(creatorSummaries.Count>256)foreach(var key in creatorSummaries.Where(p=>p.Value.IsCompleted).Select(p=>p.Key).Take(64).ToArray())creatorSummaries.Remove(key);return task;
    }
    private FrameworkElement CreatorInformation(DockPanel single,TextBlock hint,Button follow)
    {
        var ct=navigation.Token;var own=detail!.Video;
        single.Loaded+=async(_,_)=>{try{var summary=await CreatorSummaryCached(own.Mid,ct);if(!ct.IsCancellationRequested)hint.Text=$"{BilibiliClient.CountLabel(summary.Followers)} 粉丝  ·  {BilibiliClient.CountLabel(summary.Videos)} 视频";}catch(OperationCanceledException){}catch(Exception e){Debug.WriteLine(e.Message);}};
        if(detail.Contributors is not {Count:>1} team)return single;
        var panel=new StackPanel{Margin=new Thickness(0,2,0,18)};var heading=new DockPanel{Margin=new Thickness(0,0,0,14)};single.Children.Remove(follow);DockPanel.SetDock(follow,Dock.Right);heading.Children.Add(follow);heading.Children.Add(Text($"创作团队 · {team.Count} 人",16,FontWeights.SemiBold));panel.Children.Add(heading);
        var members=new UniformGrid{Columns=Math.Min(3,team.Count)};
        foreach(var person in team)
        {
            var item=new StackPanel{Margin=new Thickness(4,0,10,12)};var face=UserAvatar(person.Avatar,44,mid:person.Mid);face.HorizontalAlignment=HorizontalAlignment.Left;CreatorLink(face,person.Mid);item.Children.Add(face);
            var name=Text(person.Name,13,FontWeights.SemiBold);name.TextTrimming=TextTrimming.CharacterEllipsis;name.Margin=new Thickness(0,8,0,0);CreatorLink(name,person.Mid);item.Children.Add(IdentityName(name,person.Level,mid:person.Mid));var role=Text(person.Role,12);role.Foreground=Muted;role.Margin=new Thickness(0,5,0,0);item.Children.Add(role);members.Children.Add(item);
        }panel.Children.Add(members);return panel;
    }
    private void BuildVideoUnderlay(FrameworkElement actions)
    {
        var body=new StackPanel();onlineText=Text("正在获取观看人数…",12);onlineText.Foreground=Muted;
        var more=IconButton(PackIconKind.DotsVertical,"更多视频操作",32);more.Height=32;more.Foreground=Muted;AddLaterMenu(more,detail!.Video);
        var menu=more.ContextMenu!;menu.Style=(Style)FindResource("CompactActionMenu");menu.PlacementTarget=more;menu.Placement=PlacementMode.Custom;
        // Explicit target-relative placement avoids Windows' menu alignment offset.
        menu.CustomPopupPlacementCallback=(popup,target,_)=>
        [
            new(new Point(target.Width-popup.Width,target.Height+6),PopupPrimaryAxis.Horizontal),
            new(new Point(target.Width-popup.Width,-popup.Height-6),PopupPrimaryAxis.Horizontal)
        ];
        more.Click+=(_,e)=>{e.Handled=true;menu.IsOpen=true;};
        var actionBar=new Grid{Margin=new Thickness(0,0,0,4)};
        actionBar.RowDefinitions.Add(new(){Height=GridLength.Auto});actionBar.RowDefinitions.Add(new(){Height=GridLength.Auto});
        actionBar.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});actionBar.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        actions.HorizontalAlignment=HorizontalAlignment.Left;actionBar.Children.Add(actions);
        var tools=new DockPanel{VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right};
        more.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(more,Dock.Right);tools.Children.Add(more);
        onlineText.VerticalAlignment=VerticalAlignment.Center;onlineText.TextTrimming=TextTrimming.CharacterEllipsis;onlineText.MaxWidth=220;onlineText.Margin=new(0,0,8,0);tools.Children.Add(onlineText);
        Grid.SetColumn(tools,1);actionBar.Children.Add(tools);
        void ArrangeActions(double width)
        {
            actions.Width=Math.Min(380,Math.Max(0,width));var stacked=width<660;
            Grid.SetColumnSpan(actions,stacked?2:1);Grid.SetRow(tools,stacked?1:0);tools.Margin=stacked?new Thickness(0,6,0,0):new Thickness(16,0,0,0);
        }
        ArrangeActions(380);actionBar.SizeChanged+=(_,e)=>ArrangeActions(e.NewSize.Width);body.Children.Add(actionBar);
        if(!string.IsNullOrWhiteSpace(detail!.Description))
        {
            var fullDescription=detail.Description;var description=Text(fullDescription,14);description.TextWrapping=TextWrapping.Wrap;description.Margin=new Thickness(12,10,12,14);description.Foreground=Ink;
            var label=Text("视频简介",14);
            var expand=new Expander{Header=label,IsExpanded=false,Content=SelectableText(description,"视频简介"),HorizontalContentAlignment=HorizontalAlignment.Stretch,FontSize=14,Padding=new Thickness(4),Margin=new Thickness(0,2,0,0),Background=Surface};expand.Expanded+=(_,_)=>label.Text="收起简介";expand.Collapsed+=(_,_)=>label.Text="视频简介";body.Children.Add(expand);
        }
        videoTagsPanel=new WrapPanel{Margin=new Thickness(4,10,0,4)};body.Children.Add(videoTagsPanel);
        danmakuRow!.Height=double.NaN;danmakuRow.Margin=new Thickness(16,12,16,12);((Border)danmakuRow).Child=new SmoothScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        UpdatePlayerHeight();
    }
    private FrameworkElement? BuildPlaybackCollection()
    {
        var context=playbackCollection;if(context==null||context.Videos.Count==0)return null;var current=context.Videos.ToList().FindIndex(v=>v.Bvid==detail!.Video.Bvid);
        var panel=new StackPanel();var head=new DockPanel{Margin=new Thickness(0,0,0,8)};var count=Text($"{Math.Max(0,current)+1}/{context.Total}",12);count.Foreground=Muted;DockPanel.SetDock(count,Dock.Right);head.Children.Add(count);var title=Text(context.Title,14,FontWeights.SemiBold);title.TextTrimming=TextTrimming.CharacterEllipsis;title.ToolTip=context.Title;head.Children.Add(title);panel.Children.Add(head);
        var controls=new UniformGrid{Columns=2,Margin=new Thickness(0,0,0,8)};
        foreach(var (label,direction) in new[]{("上一集",-1),("下一集",1)}){var button=Button(label);button.Height=32;button.Margin=new Thickness(2);button.Background=CanvasBrush;button.Foreground=Pink;button.IsEnabled=current>=0&&current+direction>=0&&(current+direction<context.Videos.Count||context.Videos.Count<context.Total);button.Click+=async(_,_)=>await Run(()=>SwitchCollection(direction));controls.Children.Add(button);}panel.Children.Add(controls);
        var list=new ListBox{Height=Math.Min(240,context.Videos.Count*40),Background=Brushes.Transparent,BorderThickness=new Thickness(0),Style=null,SelectionMode=SelectionMode.Single};VirtualizingStackPanel.SetIsVirtualizing(list,true);VirtualizingStackPanel.SetVirtualizationMode(list,VirtualizationMode.Recycling);ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);ScrollViewer.SetVerticalScrollBarVisibility(list,ScrollBarVisibility.Auto);
        foreach(var v in context.Videos){var row=new DockPanel{Height=36};var duration=Text(BilibiliClient.DurationLabel(v.Duration),11);duration.Foreground=Muted;duration.Margin=new Thickness(8,0,0,0);duration.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(duration,Dock.Right);row.Children.Add(duration);var label=Text((v.Bvid==detail!.Video.Bvid?"▶  ":"")+v.Title,13);label.Foreground=v.Bvid==detail.Video.Bvid?Pink:Ink;label.TextTrimming=TextTrimming.CharacterEllipsis;label.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(label);var item=new ListBoxItem{Content=row,Tag=v.Bvid,Padding=new Thickness(8,0,8,0),HorizontalContentAlignment=HorizontalAlignment.Stretch,Background=v.Bvid==detail.Video.Bvid?PalePink:Brushes.Transparent};list.Items.Add(item);}
        list.SelectionChanged+=async(_,_)=>{if(list.SelectedItem is ListBoxItem item&&item.Tag is string bvid&&bvid!=detail?.Video.Bvid)await Run(()=>OpenVideo(bvid));};list.Loaded+=(_,_)=>{if(current>=0)list.ScrollIntoView(list.Items[current]);};panel.Children.Add(list);
        if(context.Videos.Count<context.Total){var more=Button("加载更多集");more.Foreground=Pink;more.Height=34;more.Click+=async(_,_)=>await Run(async()=>{more.IsEnabled=false;try{await LoadCollectionMore();ReplaceCollectionPanel();}finally{more.IsEnabled=true;}});panel.Children.Add(more);}
        return new Border{Tag="playback-collection",Child=panel,Padding=new Thickness(14),Background=Surface,CornerRadius=new CornerRadius(10),Margin=new Thickness(0,0,0,18)};
    }
    private void ReplaceCollectionPanel(){if(videoSidebar==null)return;var old=videoSidebar.Children.OfType<FrameworkElement>().FirstOrDefault(e=>e.Tag as string=="playback-collection");var index=old==null?1:videoSidebar.Children.IndexOf(old);if(old!=null)videoSidebar.Children.Remove(old);if(BuildPlaybackCollection() is {} next)videoSidebar.Children.Insert(Math.Min(index,videoSidebar.Children.Count),next);}
    private async Task LoadCollectionMore()
    {
        if(playbackCollection is {Id:-2,Title:"稍后再看"} queue)
        {
            var queueToken=navigation.Token;WatchLaterPage nextPage;var videos=queue.Videos.ToArray();
            do
            {
                nextPage=await api.GetWatchLater(collectionPage+1,queueToken);queueToken.ThrowIfCancellationRequested();if(playbackCollection!=queue)return;
                collectionPage++;videos=videos.Concat(nextPage.Items.Where(v=>!v.Unavailable).Select(v=>v.Video)).DistinctBy(v=>v.Bvid).ToArray();
            }while(nextPage.HasMore&&videos.Length==queue.Videos.Count);
            playbackCollection=queue with{Videos=videos,Total=nextPage.HasMore?videos.Length+1:videos.Length};return;
        }
        var context=playbackCollection!;var ct=navigation.Token;var page=await api.GetCreatorCollectionVideos(context.Mid,new(context.Id,context.Title,"",context.Total,context.Series),collectionPage+1,ct);ct.ThrowIfCancellationRequested();if(playbackCollection?.Id!=context.Id)return;collectionPage++;playbackCollection=context with{Videos=context.Videos.Concat(page.Items).DistinctBy(v=>v.Bvid).ToArray(),Total=page.Total};
    }
    private async Task SwitchCollection(int direction,bool fromBeginning=false)
    {
        if(playbackCollection==null||detail==null)return;var context=playbackCollection;var source=detail.Video.Bvid;var sourceCid=part?.Cid;var sourceHost=player;var sourceRevision=player?.LoadRevision;var current=context.Videos.ToList().FindIndex(v=>v.Bvid==source);if(current<0)return;var index=current+direction;
        if(index>=context.Videos.Count&&context.Videos.Count<context.Total)
        {
            await LoadCollectionMore();
            // A file replacement, part change or navigation while the next page is
            // loading takes precedence over the completed video's auto-next.
            if(fromBeginning&&(detail?.Video.Bvid!=source||part?.Cid!=sourceCid||!ReferenceEquals(player,sourceHost)||player?.LoadRevision!=sourceRevision))return;
            if(playbackCollection==null)return;context=playbackCollection;
        }
        if(index>=0&&index<context.Videos.Count)
        {
            var bvid=context.Videos[index].Bvid;var keepFullscreen=fromBeginning&&fullscreen;var speed=player?.Number("speed")??1;var volume=player?.Number("volume")??60;var mute=player?.Text("mute");
            await OpenVideo(bvid,fromBeginning?0:null,firstPart:fromBeginning,rememberPage:!fromBeginning);
            if(fromBeginning&&detail?.Video.Bvid==bvid&&player?.Ready==true){if(speed>0)player.Set("speed",speed.ToString(System.Globalization.CultureInfo.InvariantCulture));player.Set("volume",volume.ToString(System.Globalization.CultureInfo.InvariantCulture));if(volumeSlider!=null)volumeSlider.Value=volume;if(mute is "yes" or "no")player.Set("mute",mute);if(speedButton!=null)speedButton.Content=speed.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)+"×";if(keepFullscreen&&!fullscreen)ToggleFullscreen();}
        }
    }
    private void RefreshPartSelection()
    {
        var section=videoSidebar?.Children.OfType<Expander>().FirstOrDefault(e=>e.Tag as string=="video-parts");if(section==null||part==null||detail==null)return;
        section.Header=$"分集  {part.Page}/{detail.Parts.Count}";if(section.Content is StackPanel items)foreach(var button in items.Children.OfType<Button>())button.Foreground=button.Tag is long cid&&cid==part.Cid?Pink:Muted;
    }
    private async Task RefreshWatchMetadata(VideoPart videoPart,CancellationToken ct)
    {
        async Task Heat(){try{var map=await api.GetVideoHeatmap(detail!.Video.Bvid,detail.Video.Aid,videoPart.Cid,ct);if(!ct.IsCancellationRequested&&part?.Cid==videoPart.Cid)seek?.SetHeatmap(map);}catch(OperationCanceledException){}catch(Exception e){Debug.WriteLine("Heatmap: "+e.Message);}}
        async Task Online(){try{while(!ct.IsCancellationRequested&&part?.Cid==videoPart.Cid){var count=await api.GetOnlineViewers(detail!.Video.Bvid,videoPart.Cid,ct);if(!ct.IsCancellationRequested&&part?.Cid==videoPart.Cid&&onlineText!=null)onlineText.Text=count.Show?$"{count.Count} 人正在看":"观看人数暂不可用";await Task.Delay(TimeSpan.FromSeconds(30),ct);}}catch(OperationCanceledException){}catch(Exception e){if(!ct.IsCancellationRequested&&onlineText!=null)onlineText.Text="观看人数暂不可用";Debug.WriteLine(e.Message);}}
        await Task.WhenAll(Heat(),Online());
    }
    private async Task<IReadOnlyList<DanmakuItem>> FirstDanmaku(VideoPart videoPart,double start,CancellationToken ct)
    {
        try
        {
            if(danmakuSegments is {} session&&session.Cid==videoPart.Cid&&danmakuSessionCancellation!=null)
                return await session.GetSegmentAsync(session.SegmentAt(start),danmakuSessionCancellation.Token);
            return await api.GetDanmakuSegment(videoPart.Cid,(int)(start/360)+1,ct);
        }
        catch(OperationCanceledException){throw;}catch(Exception e){Debug.WriteLine("Initial danmaku: "+e.Message);return [];}
    }
    private async Task InstallInitialDanmaku(IReadOnlyList<DanmakuItem> messages,double start,CancellationToken ct)
    {
        danmakuMessages=messages;if(overlay!=null){overlay.Danmaku.SetMessages(messages);await overlay.Danmaku.PrimeAsync(start,ct);}ct.ThrowIfCancellationRequested();
    }
}



