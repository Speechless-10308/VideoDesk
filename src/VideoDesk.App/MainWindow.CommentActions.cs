using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;
using VideoDesk.Core;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private readonly Dictionary<long,bool> commentBlacklist=[];
    private Popup? commentMenu;
    private bool OwnComment(CommentItem item)=>account?.LoggedIn==true&&item.Mid==account.Mid;
    private bool OwnCommentVideo=>account?.LoggedIn==true&&CommentOwner==account.Mid;
    private void BeginCommentReply(CommentItem item,long root)
    {
        if(!RequireLogin())return;replyRoot=root>0?root:item.Id;replyParent=item.Id;
        replyHint!.Text="回复 "+item.Name+"  ·  点击此处取消";replyHint.Visibility=Visibility.Visible;commentInput!.Focus();
    }
    private string CommentUrl(CommentItem item,long root)=>(dynamicDetail is {} dynamic?"https://t.bilibili.com/"+dynamic.Id:"https://www.bilibili.com/video/"+detail!.Video.Bvid+"/")+"?comment_root_id="+(root>0?root:item.Id)+"&comment_id="+item.Id;
    private FrameworkElement CommentMenuContent(CommentItem item,long root,bool mine,bool owner,bool senior,Action close)
    {
        var panel=new StackPanel{Margin=new Thickness(6)};
        void Gap()=>panel.Children.Add(new Border{Height=1,Background=Surface,Margin=new Thickness(12,5,12,5)});
        Button Entry(string label,PackIconKind kind,Func<Task> action,bool danger=false,string subtitle="")
        {
            var button=Button("");button.Height=subtitle.Length>0?62:42;button.Padding=new Thickness(12,6,12,6);button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.Background=Brushes.Transparent;button.Foreground=danger?new SolidColorBrush(Color.FromRgb(227,69,90)):Ink;
            var row=new DockPanel();var icon=Icon(kind,19);icon.VerticalAlignment=VerticalAlignment.Center;icon.Margin=new Thickness(0,0,12,0);DockPanel.SetDock(icon,Dock.Left);row.Children.Add(icon);
            var labels=new StackPanel{VerticalAlignment=VerticalAlignment.Center};var title=Text(label,14);title.Foreground=button.Foreground;labels.Children.Add(title);
            if(subtitle.Length>0){var hint=Text(subtitle,11);hint.Foreground=Muted;hint.Margin=new Thickness(0,4,0,0);labels.Children.Add(hint);}row.Children.Add(labels);button.Content=row;
            button.Click+=async(_,_)=>{if(App.Arguments.Contains("--preview-comment-actions"))return;close();await Run(action);};panel.Children.Add(button);return button;
        }
        if(owner&&root==0)Entry(item.Pinned?"取消置顶":"置顶",PackIconKind.PinOutline,()=>SetCommentPin(item));
        Entry("回复",PackIconKind.CommentOutline,()=>{BeginCommentReply(item,root);return Task.CompletedTask;});
        Entry("分享",PackIconKind.ShareOutline,()=>{ShareComment(item,root);return Task.CompletedTask;});
        Entry("复制",PackIconKind.ContentCopy,()=>{Clipboard.SetText(item.Message);Notify("评论内容已复制");return Task.CompletedTask;});
        if(mine||owner){Gap();Entry("删除",PackIconKind.DeleteOutline,()=>DeleteSelectedComment(item),true);}
        if(!mine)
        {
            Gap();Entry(senior?"硬核会员举报":"举报",PackIconKind.FlagOutline,()=>ReportSelectedComment(item));
            var blocked=commentBlacklist.GetValueOrDefault(item.Mid);var block=Entry(blocked?"移出黑名单":"加入黑名单",PackIconKind.AccountOffOutline,()=>ChangeCommentBlacklist(item));
            if(!App.Arguments.Contains("--preview-comment-actions")&&account?.LoggedIn==true&&!commentBlacklist.ContainsKey(item.Mid))_ = LoadBlacklistMenuState(item,block);
        }
        if(owner){Gap();Entry("设置评论屏蔽词",PackIconKind.FilterOutline,OpenCommentFilters,subtitle:"我的稿件不展示特定评论");}
        return new Border{Child=panel,Width=252,Background=CanvasBrush,CornerRadius=new CornerRadius(12),BorderBrush=Surface,BorderThickness=new Thickness(1),Margin=new Thickness(8),Effect=new DropShadowEffect{BlurRadius=14,ShadowDepth=2,Opacity=.14}};
    }
    private async Task LoadBlacklistMenuState(CommentItem item,Button button)
    {
        button.IsEnabled=false;try{var blocked=await api.IsBlacklisted(item.Mid,CommentCancellation);commentBlacklist[item.Mid]=blocked;if(button.Content is DockPanel row&&row.Children.OfType<StackPanel>().FirstOrDefault() is {} labels&&labels.Children[0] is TextBlock title)title.Text=blocked?"移出黑名单":"加入黑名单";}catch(OperationCanceledException){}catch(Exception e){button.ToolTip="暂未读取到黑名单状态："+e.Message;}finally{button.IsEnabled=true;}
    }
    private void ShowCommentMenu(CommentItem item,long root,FrameworkElement target)
    {
        commentMenu?.SetCurrentValue(Popup.IsOpenProperty,false);var popup=new Popup{PlacementTarget=target,Placement=PlacementMode.Custom,AllowsTransparency=true,StaysOpen=false,
            CustomPopupPlacementCallback=(menu,button,_)=>[
                new(new Point(button.Width-menu.Width+8,button.Height-4),PopupPrimaryAxis.Vertical),
                new(new Point(button.Width-menu.Width+8,-menu.Height+4),PopupPrimaryAxis.Vertical)]};
        popup.Child=CommentMenuContent(item,root,OwnComment(item),OwnCommentVideo,account?.Senior==true,()=>popup.IsOpen=false);commentMenu=popup;popup.IsOpen=true;
        ScrollChangedEventHandler moved=(_,e)=>{if(e.VerticalChange!=0||e.HorizontalChange!=0)popup.IsOpen=false;};
        commentScroll?.AddHandler(ScrollViewer.ScrollChangedEvent,moved);
        EventHandler movedWindow=(_,_)=>popup.IsOpen=false;LocationChanged+=movedWindow;
        // A StaysOpen=false popup captures the mouse. Without this, wheel input
        // outside the menu can remain trapped in its separate popup window.
        PreProcessInputEventHandler wheel=(_,args)=>
        {
            if(!popup.IsOpen||args.StagingItem.Input is not MouseWheelEventArgs input)return;
            popup.IsOpen=false;input.Handled=true;
            commentScroll?.RaiseEvent(new MouseWheelEventArgs(input.MouseDevice,input.Timestamp,input.Delta){RoutedEvent=Mouse.PreviewMouseWheelEvent});
        };InputManager.Current.PreProcessInput+=wheel;
        popup.Closed+=(_,_)=>{commentScroll?.RemoveHandler(ScrollViewer.ScrollChangedEvent,moved);LocationChanged-=movedWindow;InputManager.Current.PreProcessInput-=wheel;};
    }
    private void ShareComment(CommentItem item,long root)
    {
        var link=CommentUrl(item,root);var panel=new StackPanel();panel.Children.Add(Text("分享评论",20,FontWeights.SemiBold));var title=Text(CommentTitle,14,FontWeights.SemiBold);title.TextWrapping=TextWrapping.Wrap;title.Margin=new Thickness(0,14,0,10);panel.Children.Add(title);
        var text=Text(item.Name+"："+item.Message,14);text.TextWrapping=TextWrapping.Wrap;panel.Children.Add(new Border{Child=new ScrollViewer{Content=text,MaxHeight=220,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},Background=Surface,CornerRadius=new CornerRadius(8),Padding=new Thickness(14),Margin=new Thickness(0,0,0,12)});
        var address=new TextBox{Text=link,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,FontSize=12,Padding=new Thickness(8),BorderThickness=new Thickness(0),Background=Surface};panel.Children.Add(address);
        if(Dialog(panel,"复制评论链接")){Clipboard.SetText(link);Notify("评论链接已复制，可跳转到这条评论");}
    }
    private IReadOnlyList<CommentItem> TransformComments(IReadOnlyList<CommentItem> items,Func<CommentItem,CommentItem> update)
        =>items.Select(c=>update(c with{Replies=c.Replies is null?null:TransformComments(c.Replies,update)})).ToArray();
    private void ChangeDisplayedComments(Func<CommentItem,CommentItem> update)
    {
        displayedComments=TransformComments(displayedComments,update);foreach(var key in commentFirstPages.Keys.ToArray())commentFirstPages[key]=commentFirstPages[key] with{Items=TransformComments(commentFirstPages[key].Items,update)};
        RefreshReplyViewports(update);
    }
    private void RemoveVisibleComment(long aid,long id)
    {
        bool root=displayedComments.Any(c=>c.Id==id);commentSession.Remove(aid,id);commentFirstPages.Clear();displayedComments=commentSession.Merge(aid,displayedComments);
        DisplayCommentSnapshot(displayedComments,Math.Max(0,displayedCommentTotal-(root?1:0)));
    }
    private async Task DeleteSelectedComment(CommentItem item)
    {
        if(!RequireLogin())return;if(!OwnComment(item)&&!OwnCommentVideo){Notify("只能删除自己的评论或管理自己视频下的评论");return;}
        var oid=CommentOid;var type=CommentType;var ct=CommentCancellation;var preview=item.Message.Length>100?item.Message[..100]+"…":item.Message;
        if(!Confirm("删除评论",(item.ReplyCount>0?"这条评论和其下的回复将被删除。":"这条评论将被删除。")+"\n\n"+preview,"删除"))return;
        await api.DeleteComment(oid,item.Id,ct,type);commentSession.Remove(oid,item.Id);if(ct.IsCancellationRequested)return;
        RemoveVisibleComment(oid,item.Id);Notify("评论已删除");
    }
    private async Task SetCommentPin(CommentItem item)
    {
        if(!RequireLogin()||!OwnCommentVideo)return;var oid=CommentOid;var type=CommentType;var ct=CommentCancellation;var pin=!item.Pinned;
        if(pin&&displayedComments.Any(c=>c.Pinned&&c.Id!=item.Id)&&!Confirm("替换置顶评论","置顶这条评论会替换当前的置顶评论。","置顶"))return;
        await api.PinComment(oid,item.Id,pin,ct,type);if(ct.IsCancellationRequested)return;commentSession.SetPinned(oid,pin?item.Id:0);
        ChangeDisplayedComments(c=>c with{Pinned=c.Id==item.Id?pin:pin?false:c.Pinned});commentFirstPages.Clear();if(pin)displayedComments=displayedComments.OrderByDescending(c=>c.Id==item.Id).ToArray();DisplayCommentSnapshot(displayedComments,displayedCommentTotal);Notify(pin?"评论已置顶":"已取消置顶");
    }
    private void HideCommentsFrom(long aid,long mid)
    {
        void Remove(IEnumerable<CommentItem> items){foreach(var c in items){if(c.Mid==mid)commentSession.Remove(aid,c.Id);if(c.Replies!=null)Remove(c.Replies);}}Remove(displayedComments);foreach(var state in commentReplyStates.Values)Remove(state.Items);var total=displayedCommentTotal-displayedComments.Count(c=>c.Mid==mid);commentFirstPages.Clear();DisplayCommentSnapshot(commentSession.Merge(aid,displayedComments),Math.Max(0,total));
    }
    private async Task ChangeCommentBlacklist(CommentItem item)
    {
        if(!RequireLogin()||OwnComment(item))return;var oid=CommentOid;var type=CommentType;var ct=CommentCancellation;var blocked=commentBlacklist.TryGetValue(item.Mid,out var known)?known:await api.IsBlacklisted(item.Mid,ct);
        if(!Confirm(blocked?"移出黑名单":"加入黑名单",blocked?"将“"+item.Name+"”移出黑名单？":"将“"+item.Name+"”加入黑名单后，会解除关注及相关合集订阅，并限制对方与你互动。",blocked?"移出":"加入"))return;
        await api.SetBlacklist(item.Mid,!blocked,ct);commentBlacklist[item.Mid]=!blocked;if(ct.IsCancellationRequested)return;if(!blocked)HideCommentsFrom(oid,item.Mid);Notify(blocked?"已移出黑名单":"已加入黑名单");
    }
    private async Task ReportSelectedComment(CommentItem item)
    {
        if(!RequireLogin()||OwnComment(item))return;var oid=CommentOid;var type=CommentType;var ct=CommentCancellation;var metadata=await api.GetCommentReportMetadata(oid,ct,type);ct.ThrowIfCancellationRequested();
        var panel=new StackPanel();panel.Children.Add(Text(account!.Senior?"硬核会员举报":"举报评论",20,FontWeights.SemiBold));var preview=Text(item.Name+"："+(item.Message.Length>90?item.Message[..90]+"…":item.Message),13);preview.Foreground=Muted;preview.TextWrapping=TextWrapping.Wrap;preview.Margin=new Thickness(0,12,0,16);panel.Children.Add(preview);
        panel.Children.Add(Text("举报原因",14));var reasons=new ComboBox{ItemsSource=metadata.Reasons,DisplayMemberPath="Name",SelectedIndex=0,Height=40,Margin=new Thickness(0,8,0,12)};panel.Children.Add(reasons);
        var content=new TextBox{MaxLength=200,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=90,Padding=new Thickness(10)};HintAssist.SetHint(content,"补充说明（其他或要求说明的选项需填写 2–200 字）");panel.Children.Add(content);
        var blacklist=new CheckBox{Content="同时加入黑名单",Margin=new Thickness(0,14,0,8)};panel.Children.Add(blacklist);var delete=new CheckBox{Content="同时删除该评论",Visibility=metadata.CanDelete&&OwnCommentVideo?Visibility.Visible:Visibility.Collapsed};panel.Children.Add(delete);
        var validation=Text("",12);validation.Foreground=new SolidColorBrush(Color.FromRgb(227,69,90));validation.TextWrapping=TextWrapping.Wrap;validation.Margin=new Thickness(0,8,0,0);panel.Children.Add(validation);
        if(!Dialog(panel,"提交举报",canAccept:()=>{var selected=(CommentReportReason)reasons.SelectedItem;if((selected.DetailRequired||selected.Id==0)&&content.Text.Trim().Length<2){validation.Text="该举报原因需要填写 2–200 字补充说明";return false;}return true;}))return;var reason=(CommentReportReason)reasons.SelectedItem;var explanation=content.Text.Trim();
        await api.ReportComment(oid,item.Id,reason.Id,explanation,blacklist.IsChecked==true,delete.IsChecked==true,commentsNewest,ct,type);if(ct.IsCancellationRequested)return;
        if(blacklist.IsChecked==true){commentBlacklist[item.Mid]=true;HideCommentsFrom(oid,item.Mid);}else RemoveVisibleComment(oid,item.Id);Notify("举报已提交");
    }
    private async Task OpenCommentFilters()
    {
        if(!RequireLogin()||!OwnCommentVideo)return;var link=await api.GetCommentFilterLink(CommentOid,CommentCancellation,CommentType);await NavigateCommentLink(link);
    }
}

