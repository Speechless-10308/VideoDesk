using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using VideoDesk.Core;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private Button? laterFab;
    private WatchLaterWindow? watchLaterWindow;
    private Task laterHistoryFlush=Task.CompletedTask;
    private readonly List<WatchLaterItem> laterItems=[];
    private readonly HashSet<long> laterKnown=[];
    private readonly HashSet<long> laterBusy=[];
    private long laterAccount;
    private int laterPage;
    private bool laterMore;
    private event Action? LaterChanged;
    private void BuildLaterFab()
    {
        laterFab=IconButton(PackIconKind.ClockOutline,"稍后再看",52);laterFab.Height=52;laterFab.Background=Pink;laterFab.Foreground=Brushes.White;
        MaterialDesignThemes.Wpf.ButtonAssist.SetCornerRadius(laterFab,new CornerRadius(16));
        laterFab.Margin=new(0,0,0,174);laterFab.HorizontalAlignment=HorizontalAlignment.Center;laterFab.VerticalAlignment=VerticalAlignment.Bottom;
        laterFab.Click+=async(_,_)=>await Run(ShowWatchLater);Grid.SetRow(laterFab,1);Grid.SetColumn(laterFab,0);Panel.SetZIndex(laterFab,70);shell.Children.Add(laterFab);
    }
    private Task ShowWatchLater()
    {
        if(!RequireLogin())return Task.CompletedTask;EnsureLaterAccount();StopCardPreview();
        if(watchLaterWindow is {} opened){if(opened.WindowState==WindowState.Minimized)opened.WindowState=WindowState.Normal;opened.Activate();return Task.CompletedTask;}
        var mid=laterAccount;
        var viewer=new WatchLaterWindow(this,api,images,local,preferences,Ink,Muted,Pink,Surface,CanvasBrush,PalePink,
            ReadLaterPage,video=>SetLater(video,false),async item=>{watchLaterWindow?.Pause();Activate();await Run(()=>OpenLaterVideo(item));},
            ()=>{StopCardPreview();player?.Set("pause","yes");},()=>account?.Mid==mid);
        watchLaterWindow=viewer;viewer.Closed+=(_,_)=>{laterHistoryFlush=viewer.HistorySync;if(ReferenceEquals(watchLaterWindow,viewer))watchLaterWindow=null;};
        var work=SystemParameters.WorkArea;viewer.Left=Math.Clamp(Left+ActualWidth-viewer.Width-24,work.Left,Math.Max(work.Left,work.Right-viewer.Width));viewer.Top=Math.Clamp(Top+92,work.Top,Math.Max(work.Top,work.Bottom-viewer.Height));viewer.Show();return viewer.Initialize();
    }
    private async Task<WatchLaterPage> ReadLaterPage(int page,CancellationToken ct)
    {
        var mid=laterAccount;var result=await api.GetWatchLater(page,ct);ct.ThrowIfCancellationRequested();if(mid!=laterAccount)throw new OperationCanceledException();
        if(page==1){laterItems.Clear();laterKnown.Clear();}laterPage=page;laterMore=result.HasMore;
        foreach(var item in result.Items){if(laterItems.All(v=>v.Video.Aid!=item.Video.Aid))laterItems.Add(item);laterKnown.Add(item.Video.Aid);}LaterChanged?.Invoke();return result;
    }
    private void EnsureLaterAccount()
    {
        var mid=account?.LoggedIn==true?account.Mid:0;if(laterAccount==mid)return;
        laterAccount=mid;laterItems.Clear();laterKnown.Clear();laterBusy.Clear();laterPage=0;laterMore=false;watchLaterWindow?.Close();watchLaterWindow=null;LaterChanged?.Invoke();
    }
    private async Task SetLater(VideoCard video,bool add)
    {
        if(!RequireLogin())return;EnsureLaterAccount();if(!laterBusy.Add(video.Aid))return;var mid=laterAccount;LaterChanged?.Invoke();
        try
        {
            if(add)await api.AddWatchLater(video.Bvid,video.Aid);else await api.RemoveWatchLater(video.Aid);
            if(account?.Mid!=mid)return;
            if(add)laterKnown.Add(video.Aid);else {laterKnown.Remove(video.Aid);laterItems.RemoveAll(v=>v.Video.Aid==video.Aid);}
            // Offset pagination shifts after a deletion or insertion. Re-read an
            // overlapping page and deduplicate, rather than skipping an item.
            laterPage=Math.Max(0,laterPage-1);laterMore=true;
            watchLaterWindow?.ApplyMutation(video,add);
            Notify(add?"已添加到稍后再看":"已从稍后再看移除");
        }
        finally{if(laterAccount==mid){laterBusy.Remove(video.Aid);LaterChanged?.Invoke();}}
    }
    private Button LaterButton(VideoCard video)
    {
        var button=IconButton(PackIconKind.ClockPlusOutline,"添加到稍后再看",32);button.Height=32;button.Padding=new(6);button.Foreground=Brushes.White;button.Background=new SolidColorBrush(Color.FromArgb(185,25,25,30));
        var row=new StackPanel{Orientation=Orientation.Horizontal};var icon=Icon(PackIconKind.ClockPlusOutline,20);row.Children.Add(icon);var label=Text("添加到稍后再看",12);label.Foreground=Brushes.White;label.VerticalAlignment=VerticalAlignment.Center;label.Margin=new(5,0,2,0);label.Visibility=Visibility.Collapsed;row.Children.Add(label);button.Content=row;button.Width=double.NaN;
        void Update(){var added=laterKnown.Contains(video.Aid);icon.Kind=added?PackIconKind.ClockCheckOutline:PackIconKind.ClockPlusOutline;label.Text=added?"从稍后再看移除":"添加到稍后再看";button.ToolTip=null;AutomationProperties.SetName(button,label.Text);button.IsEnabled=!laterBusy.Contains(video.Aid);}
        Update();button.Loaded+=(_,_)=>{LaterChanged+=Update;Update();};button.Unloaded+=(_,_)=>LaterChanged-=Update;
        button.MouseEnter+=(_,_)=>label.Visibility=Visibility.Visible;button.MouseLeave+=(_,_)=>{if(!button.IsKeyboardFocusWithin)label.Visibility=Visibility.Collapsed;};button.GotKeyboardFocus+=(_,_)=>label.Visibility=Visibility.Visible;button.LostKeyboardFocus+=(_,_)=>{if(!button.IsMouseOver)label.Visibility=Visibility.Collapsed;};
        button.Click+=async(_,e)=>{e.Handled=true;await Run(()=>SetLater(video,!laterKnown.Contains(video.Aid)));};return button;
    }
    private void AddLaterMenu(FrameworkElement tile,VideoCard video)
    {
        var menu=tile.ContextMenu??=new ContextMenu();var icon=Icon(PackIconKind.ClockPlusOutline,18);var item=new MenuItem{Header="添加到稍后再看",Icon=icon};
        menu.Opened+=(_,_)=>{var added=laterKnown.Contains(video.Aid);item.Header=added?"从稍后再看移除":"添加到稍后再看";icon.Kind=added?PackIconKind.ClockCheckOutline:PackIconKind.ClockPlusOutline;item.IsEnabled=!laterBusy.Contains(video.Aid);};
        item.Click+=async(_,e)=>{e.Handled=true;await Run(()=>SetLater(video,!laterKnown.Contains(video.Aid)));};menu.Items.Add(item);
    }
    private async Task OpenLaterVideo(WatchLaterItem item)
    {
        if(item.Unavailable)return;var videos=laterItems.Where(v=>!v.Unavailable).Select(v=>v.Video).ToArray();var page=laterPage;var more=laterMore;var mid=laterAccount;
        var progress=local.GetProgress(item.Video.Bvid);await OpenVideo(item.Video.Bvid,progress==null?Math.Max(0,item.Progress):null,preferredCid:progress==null?item.Cid:null);
        if(detail?.Video.Bvid!=item.Video.Bvid)return;
        playbackCollection=new(-2,mid,"稍后再看",videos,more?videos.Length+1:videos.Length);collectionPage=page;BuildVideoSidebar();
    }
}
