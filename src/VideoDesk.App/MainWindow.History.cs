using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using VideoDesk.Core;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private string historyQuery="";
    private int historyRenderVersion;
    private ViewportCardRows<WatchProgress>? historyCards;
    private async Task ShowHistory()
    {
        var ct=BeginPage();staticPage="history";SelectNav(historyNav);
        if(historyLayout!=null&&libraryAccountMid==(account?.Mid??0))
        {
            ShowCachedPage(historyLayout);if(local.History.FirstOrDefault()?.WatchedAt>historyLocalRendered)await RenderHistory(historyRows!,ct);if(DateTime.UtcNow-historyFetched<TimeSpan.FromSeconds(30))return;
            if(account?.LoggedIn==true)await ReadSection(historyRows!,async()=>{var updated=await api.GetHistory(ct:ct);ct.ThrowIfCancellationRequested();historyCursor=updated;cloudHistory.Clear();cloudHistory.AddRange(updated.Items);await RenderHistory(historyRows!,ct);historyFetched=DateTime.UtcNow;},"云端历史",ct);return;
        }
        libraryAccountMid=account?.Mid??0;cloudHistory.Clear();historyCursor=null;historyQuery="";
        var layout=new DockPanel{Margin=new Thickness(6,4,6,0)};
        var heading=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(32,0,0,22)};var clock=Icon(PackIconKind.History,28);clock.Margin=new Thickness(0,0,12,0);clock.VerticalAlignment=VerticalAlignment.Center;heading.Children.Add(clock);heading.Children.Add(Text("历史记录",28,FontWeights.SemiBold));DockPanel.SetDock(heading,Dock.Top);layout.Children.Add(heading);
        var toolbar=new Grid{Margin=new Thickness(72,0,8,24)};toolbar.ColumnDefinitions.Add(new());toolbar.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var label=Text("观看记录",15,FontWeights.Medium);label.Foreground=Pink;label.VerticalAlignment=VerticalAlignment.Center;toolbar.Children.Add(label);
        var tools=new StackPanel{Orientation=Orientation.Horizontal};var field=new TextBox{Width=240,Height=36,FontSize=13,Style=null,BorderThickness=new Thickness(0),Background=Surface,Padding=new Thickness(12,6,12,6),VerticalContentAlignment=VerticalAlignment.Center};AutomationProperties.SetName(field,"搜索历史标题或 UP 主");var searchSurface=InputSurface(field,"搜索标题 / UP 主");searchSurface.Margin=new Thickness(0,0,12,0);tools.Children.Add(searchSurface);
        var clear=Button("清空历史");clear.Height=36;clear.FontSize=13;clear.Padding=new Thickness(14,0,14,0);clear.Foreground=Muted;clear.Background=Surface;
        clear.Click+=async(_,_)=>await Run(async()=>{var panel=new StackPanel();panel.Children.Add(Text("清空观看记录？",20,FontWeights.SemiBold));var cloud=new CheckBox{Content="同时清空 B站账号历史",IsEnabled=account?.LoggedIn==true,Margin=new Thickness(0,18,0,0)};panel.Children.Add(cloud);if(!Dialog(panel,"清空"))return;if(cloud.IsChecked==true)await api.ClearCloudHistory();local.ClearProgress();historyLayout=null;await ShowHistory();});tools.Children.Add(clear);Grid.SetColumn(tools,1);toolbar.Children.Add(tools);DockPanel.SetDock(toolbar,Dock.Top);layout.Children.Add(toolbar);
        toolbar.SizeChanged+=(_,e)=>searchSurface.Width=e.NewSize.Width<650?170:240;
        var rows=new StackPanel();var scroll=new SmoothScrollViewer{Content=rows,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};layout.Children.Add(scroll);content.Children.Add(layout);historyLayout=layout;historyRows=rows;historyScroll=scroll;
        var searchDelay=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(180)};searchDelay.Tick+=async(_,_)=>{searchDelay.Stop();if(staticPage!="history"||!layout.IsVisible)return;historyQuery=field.Text.Trim();await Run(()=>RenderHistory(rows,navigation.Token));scroll.ScrollToTop();};field.TextChanged+=(_,_)=>{searchDelay.Stop();searchDelay.Start();};layout.Unloaded+=(_,_)=>searchDelay.Stop();
        bool loading=false;
        async Task LoadMore(){if(loading)return;loading=true;var token=navigation.Token;try{await ReadSection(rows,async()=>{var result=await api.GetHistory(historyCursor?.Max??0,historyCursor?.ViewedAt??0,historyCursor?.Business??"",token);token.ThrowIfCancellationRequested();historyCursor=result;cloudHistory.AddRange(result.Items);await RenderHistory(rows,token);historyFetched=DateTime.UtcNow;},"云端历史",token,LoadMore);}finally{loading=false;}}
        await RenderHistory(rows,ct);if(account?.LoggedIn==true)await LoadMore();
        scroll.ScrollChanged+=async(_,e)=>{if(e.VerticalChange>0&&scroll.ScrollableHeight-scroll.VerticalOffset<350&&historyCursor?.HasMore==true)await Run(LoadMore);};
    }
    private async Task RenderHistory(StackPanel target,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();var version=++historyRenderVersion;var snapshot=cloudHistory.Concat(local.SnapshotHistory()).ToArray();var query=historyQuery;
        var merged=await Task.Run(()=>snapshot.GroupBy(p=>p.Bvid).Select(g=>{var newest=g.OrderByDescending(p=>p.WatchedAt).First();var metadata=g.FirstOrDefault(p=>p.Cover.Length>0);return newest with{Cover=metadata?.Cover??newest.Cover,Creator=metadata?.Creator??newest.Creator,Aid=g.Max(v=>v.Aid),Mid=g.FirstOrDefault(v=>v.Mid>0)?.Mid??0,Cloud=g.Any(v=>v.Cloud)};}).OrderByDescending(p=>p.WatchedAt).Where(p=>query.Length==0||p.Title.Contains(query,StringComparison.OrdinalIgnoreCase)||p.Creator.Contains(query,StringComparison.OrdinalIgnoreCase)).ToArray(),ct);
        ct.ThrowIfCancellationRequested();if(version!=historyRenderVersion)return;
        if(historyCards?.Parent!=target)historyCards=new(()=>historyScroll,w=>Math.Clamp((int)(w/270),1,5),item=>HistoryCard(item,target,navigation.Token),item=>HistoryDay(item.WatchedAt.LocalDateTime.Date),HistoryRow,80);
        historyCards.SetItems(merged);if(historyCards.Parent!=target){target.Children.Clear();target.Children.Add(historyCards);}else RemoveSectionState(target);
        if(merged.Length==0){var empty=SectionState(target);empty.Empty(query.Length>0?"没有找到匹配的观看记录":"观看过的视频会显示在这里");empty.Margin=new Thickness(88,30,0,0);}
        historyLocalRendered=local.History.FirstOrDefault()?.WatchedAt??default;
    }
    private static string HistoryDay(DateTime date)=>date==DateTime.Today?"今天":date==DateTime.Today.AddDays(-1)?"昨天":date.ToString("M月d日");
    private FrameworkElement HistoryRow(string label,bool first,UniformGrid cards)
    {
        var section=new Grid();section.ColumnDefinitions.Add(new(){Width=new GridLength(80)});section.ColumnDefinitions.Add(new());
        if(first){var date=Text(label,14);date.Foreground=Muted;date.HorizontalAlignment=HorizontalAlignment.Right;date.VerticalAlignment=VerticalAlignment.Top;date.Margin=new Thickness(0,4,22,0);section.Children.Add(date);section.Children.Add(new Ellipse{Width=11,Height=11,Fill=CanvasBrush,Stroke=Muted,StrokeThickness=1.5,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,7,2,0)});}
        section.Children.Add(new Border{Width=1,Background=Surface,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,0,7,0)});Grid.SetColumn(cards,1);section.Children.Add(cards);return section;
    }
    private FrameworkElement HistoryCard(WatchProgress item,StackPanel target,CancellationToken ct)
    {
        var card=new StackPanel();var cover=CardCover(item.Cover,item.Duration,"",out var image,item.Position);bool finished=item.Duration>0&&item.Position>=item.Duration-10;
        var play=Icon(PackIconKind.PlayCircleOutline,44);play.Foreground=Brushes.White;play.HorizontalAlignment=HorizontalAlignment.Center;play.VerticalAlignment=VerticalAlignment.Center;play.Visibility=Visibility.Collapsed;cover.Children.Add(play);
        card.Children.Add(cover);var entry=CardButton(card,item.Title,()=>ResumeHistory(item));AutomationProperties.SetName(entry,(finished?"重新观看：":"继续观看：")+item.Title);entry.MouseEnter+=(_,_)=>play.Visibility=Visibility.Visible;entry.MouseLeave+=(_,_)=>play.Visibility=Visibility.Collapsed;entry.SizeChanged+=(_,e)=>cover.Height=Math.Max(100,e.NewSize.Width*9/16);
        var titleRow=new Grid{Height=54};titleRow.ColumnDefinitions.Add(new());titleRow.ColumnDefinitions.Add(new(){Width=new GridLength(28)});titleRow.Children.Add(CardTitle(item.Title));
        var delete=IconButton(PackIconKind.TrashCanOutline,"删除这条观看记录",28);delete.Height=28;delete.Foreground=Muted;delete.VerticalAlignment=VerticalAlignment.Top;delete.Margin=new Thickness(0,10,0,0);Grid.SetColumn(delete,1);titleRow.Children.Add(delete);delete.Click+=async(_,_)=>await Run(async()=>{if(!Confirm("删除观看记录？",item.Cloud?"这条记录将从本机和 B站账号历史中删除。":"这条记录将从本机历史中删除。","删除"))return;var token=navigation.Token;if(item.Cloud)await api.DeleteHistory(item.Aid>0?item.Aid:(await api.GetDetail(item.Bvid,token)).Video.Aid);local.RemoveProgress(item.Bvid);token.ThrowIfCancellationRequested();cloudHistory.RemoveAll(p=>p.Bvid==item.Bvid);historyFetched=DateTime.MinValue;await RenderHistory(target,token);});card.Children.Add(titleRow);
        var metadata=new Grid{Margin=new Thickness(8,6,8,10)};metadata.ColumnDefinitions.Add(new());metadata.ColumnDefinitions.Add(new(){Width=GridLength.Auto});metadata.Children.Add(CardAuthor(item.Creator,item.Mid,false,out var author));
        var time=Text(item.WatchedAt.LocalDateTime.ToString("HH:mm"),13);time.Foreground=Muted;time.Margin=new Thickness(8,0,0,0);time.ToolTip=item.WatchedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");Grid.SetColumn(time,1);metadata.Children.Add(time);card.Children.Add(metadata);
        if(item.Cover.Length==0)_=HydrateHistory(item,image,author,ct);return entry;
    }
    private async Task HydrateHistory(WatchProgress record,Image image,TextBlock author,CancellationToken ct)
    {
        try{var d=await api.GetDetail(record.Bvid,ct);ct.ThrowIfCancellationRequested();LoadImage(image,d.Video.Cover,480);author.Text=d.Video.Creator;CreatorLink(author,d.Video.Mid);if(!record.Cloud)local.SaveProgress(record with{Cover=d.Video.Cover,Creator=d.Video.Creator,Published=d.Video.Published,Aid=d.Video.Aid,Mid=d.Video.Mid});}catch(Exception e){System.Diagnostics.Debug.WriteLine(e.Message);}
    }
    private async Task ResumeHistory(WatchProgress item){local.SaveProgress(item with{Cloud=false});await OpenVideo(item.Bvid);}
}
