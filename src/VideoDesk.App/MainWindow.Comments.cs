using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using VideoDesk.Core;
using System.Globalization;
using System.Text;
using System.Windows.Documents;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private FrameworkElement? commentComposer;
    private TextBox? commentInput;
    private TextBlock? replyHint;
    private long replyRoot,replyParent;
    private int commentPage;
    private long commentNext;
    private bool commentsNewest,commentsLoading,commentsEnded;
    private int commentVersion;
    private TextBlock? commentCount;
    private StackPanel? commentItems;
    private ViewportListRows<CommentItem>? commentRows;
    private Border? commentHeader;
    private DockPanel? commentView;
    private readonly Dictionary<bool,CommentPage> commentFirstPages=[];
    private ComboBox? commentSort;
    private readonly CommentSession commentSession=new();
    private IReadOnlyList<CommentItem> displayedComments=[];
    private long displayedCommentTotal;
    private long CommentOid=>dynamicDetail?.CommentOid??detail?.Video.Aid??0;
    private int CommentType=>dynamicDetail?.CommentType??1;
    private long CommentOwner=>dynamicDetail?.Mid??detail?.Video.Mid??0;
    private string CommentTitle=>dynamicDetail is {} dynamic?dynamic.Name+"的动态":detail?.Video.Title??"";
    private FrameworkElement CreateCommentViewport()
    {
        commentSidebar=new();
        commentScroll=new SmoothScrollViewer{Content=commentSidebar,ShowBackToTop=false,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        return commentScroll;
    }
    private void UpdateCommentCache(long id,bool liked,long count,bool? hated=null)
    {
        CommentItem Update(CommentItem item)=>item with{Liked=item.Id==id?liked:item.Liked,Likes=item.Id==id?count:item.Likes,Hated=item.Id==id?(hated??(liked?false:item.Hated)):item.Hated,Replies=item.Replies?.Select(Update).ToArray()};
        displayedComments=displayedComments.Select(Update).ToArray();
        foreach(var key in commentFirstPages.Keys.ToArray())commentFirstPages[key]=commentFirstPages[key] with{Items=commentFirstPages[key].Items.Select(Update).ToArray()};
        if(CommentOid>0)commentSession.UpdateLike(CommentOid,id,liked,count,hated);
        RefreshReplyViewports(Update);
    }
    private FrameworkElement BuildCommentComposer()
    {
        replyRoot=replyParent=0;commentPage=0;commentNext=0;commentsEnded=false;commentVersion++;commentFirstPages.Clear();commentSort=null;displayedComments=[];displayedCommentTotal=0;commentRows=null;commentItems=null;commentReplyStates.Clear();
        var panel=new StackPanel{Margin=new Thickness(0,8,0,0),Background=CanvasBrush};panel.Children.Add(new Border{Height=1,Background=Surface,Margin=new Thickness(0,0,0,12)});replyHint=Text("",12);replyHint.Foreground=Muted;replyHint.Margin=new Thickness(40,0,0,8);replyHint.Visibility=Visibility.Collapsed;replyHint.MouseLeftButtonDown+=(_,_)=>{replyRoot=replyParent=0;replyHint.Visibility=Visibility.Collapsed;};panel.Children.Add(replyHint);
        BuildNativeCommentEditor(panel);return panel;
    }
    private async Task PublishComment(Button send)
    {
        if(!RequireLogin())return;if(string.IsNullOrWhiteSpace(commentInput!.Text)&&commentImagePaths.Count==0){Notify("请输入评论内容或添加图片");return;}send.IsEnabled=false;var oid=CommentOid;var type=CommentType;var bvid=detail?.Video.Bvid??"";var input=commentInput;var message=input.Text;var root=replyRoot;var parent=replyParent;var sender=account!;var ct=CommentCancellation;var draft=new CommentDraft(new Dictionary<string,long>(commentMentions),commentImagePaths.ToArray(),commentSync?.IsChecked==true);if(commentComposer!=null)commentComposer.IsEnabled=false;
        try
        {
            var submission=await api.SendComment(oid,message,root,parent,ct,bvid,draft,type);
            if(submission.Publication==CommentPublication.VerificationRequired){if(!ct.IsCancellationRequested)Notify(submission.Message);return;}
            var reply=submission.Reply??new CommentItem(sender.Name,message,0,submission.Id,sender.Avatar,DateTimeOffset.UtcNow.ToUnixTimeSeconds(),sender.Mid,Level:sender.Level,Senior:sender.Senior);reply=reply with{Id=submission.Id,Message=reply.Message.Length>0?reply.Message:message,Avatar=reply.Avatar.Length>0?reply.Avatar:sender.Avatar};commentSession.Add(oid,root,reply);
            if(ct.IsCancellationRequested||CommentOid!=oid||CommentType!=type)return;input.Clear();commentMentions.Clear();commentImagePaths.Clear();RenderCommentAttachments();replyRoot=replyParent=0;replyHint!.Visibility=Visibility.Collapsed;commentsNewest=root==0||commentsNewest;commentFirstPages.Clear();commentVersion++;commentsLoading=false;
            DisplayCommentSnapshot(commentSession.Merge(oid,displayedComments),displayedCommentTotal+(root==0?1:0));if(root==0)commentScroll!.ScrollToTop();Notify(submission.Message);
            _=RefreshAfterComment(oid,ct);
        }
        finally{send.IsEnabled=true;if(ReferenceEquals(input,commentInput)&&commentComposer!=null)commentComposer.IsEnabled=true;}
    }
    private async Task RefreshAfterComment(long oid,CancellationToken ct)
    {
        try{await LoadComments();}catch(OperationCanceledException){}catch(Exception e){if(!ct.IsCancellationRequested&&CommentOid==oid)Notify("发送成功，列表刷新失败，可稍后重试："+e.Message);}
    }
    private void DisplayCommentSnapshot(IReadOnlyList<CommentItem> items,long total)
    {
        displayedComments=items;displayedCommentTotal=Math.Max(total,items.Count);if(commentCount!=null)commentCount.Text="评论  "+BilibiliClient.CountLabel(displayedCommentTotal);
        RefreshReplyViewports();
        if(commentItems?.Parent!=commentSidebar||commentRows==null)
        {
            commentSidebar!.Children.Clear();commentItems=new StackPanel{Margin=new Thickness(0,0,12,0)};
            commentRows=new(()=>commentScroll,item=>RenderComment(item),item=>item.Id>0?(object)item.Id:item,170);
            commentItems.Children.Add(commentRows);commentSidebar.Children.Add(commentItems);
        }
        RemoveSectionState(commentSidebar!);RemoveSectionState(commentItems!);commentRows.SetItems(items);
        if(items.Count==0)SectionState(commentItems!).Empty(dynamicDetail==null?"还没有评论，来聊聊这个视频吧":"还没有评论，来聊聊这条动态吧");
    }
    private async Task LoadComments()
    {
        var ct=CommentCancellation;var version=++commentVersion;commentsLoading=false;commentsEnded=false;commentPage=0;commentNext=0;
        if(commentHeader!.Child==null)
        {
            var header=new Grid{Margin=new Thickness(0,2,12,6)};header.ColumnDefinitions.Add(new());header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var sort=new ComboBox{Style=(Style)FindResource("CompactChoice"),ItemsSource=new[]{"最热","最新"},SelectedIndex=commentsNewest?1:0,Width=100,Margin=new Thickness(20,0,0,0),Tag="comment-sort"};commentSort=sort;
            sort.SelectionChanged+=async(_,_)=>{if(ct.IsCancellationRequested)return;var newest=sort.SelectedIndex==1;if(commentsNewest==newest)return;commentsNewest=newest;await Run(LoadComments);};Grid.SetColumn(sort,1);header.Children.Add(sort);
            commentCount=Text("评论",19,FontWeights.SemiBold);commentCount.TextTrimming=TextTrimming.CharacterEllipsis;header.Children.Add(commentCount);commentHeader.Child=header;
        }
        if(commentSort!=null)commentSort.SelectedIndex=commentsNewest?1:0;
        commentScroll!.ScrollChanged-=OnCommentScroll;commentScroll.ScrollChanged+=OnCommentScroll;await LoadMoreComments(ct,version);
    }
    private async void OnCommentScroll(object sender,ScrollChangedEventArgs e){if(e.VerticalChange>0&&commentScroll!.ScrollableHeight-commentScroll.VerticalOffset<350&&!commentsLoading&&!commentsEnded)await Run(()=>LoadMoreComments(CommentCancellation,commentVersion));}
    private async Task LoadMoreComments(CancellationToken ct,int version)
    {
        if(commentsLoading||commentsEnded)return;commentsLoading=true;var target=commentSidebar!;RemoveSectionState(target);var feedback=SectionState(target);feedback.Loading(commentPage==0?"正在加载评论…":"正在加载更多评论…");
        try{var newest=commentsNewest;var aid=CommentOid;var result=commentPage==0&&commentFirstPages.TryGetValue(newest,out var cached)?cached:await api.GetCommentPage(aid,commentPage+1,newest,ct,next:commentNext,type:CommentType);ct.ThrowIfCancellationRequested();if(version!=commentVersion)return;commentPage++;commentNext=result.Next;if(commentPage==1)commentFirstPages[newest]=result;commentsEnded=!result.HasMore;var items=commentPage==1?result.Items:displayedComments.Concat(result.Items).DistinctBy(c=>c.Id).ToArray();DisplayCommentSnapshot(commentSession.Merge(aid,items),result.Total);if(commentPage==1)commentScroll!.ScrollToTop();}
        catch(Exception error) when(error is not OperationCanceledException)
        {
            if(!ct.IsCancellationRequested&&version==commentVersion){commentsEnded=true;feedback.Failed(displayedComments.Count>0?"评论加载失败":"评论加载失败",error.Message,async()=>{ct.ThrowIfCancellationRequested();if(version!=commentVersion)return;commentsEnded=false;await Run(()=>LoadMoreComments(ct,version));});}
        }
        finally{if(version==commentVersion)commentsLoading=false;if(feedback.Status==SectionStatus.Loading||ct.IsCancellationRequested||version!=commentVersion)target.Children.Remove(feedback);}
    }
    private FrameworkElement RenderComment(CommentItem item,bool nested=false,long root=0)
    {
        var outer=new StackPanel{Margin=new Thickness(0,0,0,nested?14:20)};var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new GridLength(nested?34:46)});row.ColumnDefinitions.Add(new());var face=UserAvatar(item.Avatar,nested?26:36,item.Identity);face.HorizontalAlignment=HorizontalAlignment.Left;face.VerticalAlignment=VerticalAlignment.Top;CreatorLink(face,item.Mid);row.Children.Add(face);
        var text=new StackPanel();var top=new Grid();top.ColumnDefinitions.Add(new());top.ColumnDefinitions.Add(new(){Width=GridLength.Auto});var name=Text(item.Name,nested?12:13,FontWeights.Medium);name.TextTrimming=TextTrimming.CharacterEllipsis;name.MaxWidth=nested?180:210;name.Foreground=Muted;top.Children.Add(IdentityName(name,item.Level,item.Senior,identity:item.Identity));if(item.Mid==CommentOwner){var up=Text("UP",10,FontWeights.SemiBold);up.Foreground=Brushes.White;var badge=new Border{Child=up,CornerRadius=new CornerRadius(3),Background=Pink,Padding=new Thickness(3,0,3,0),Margin=new Thickness(6,0,0,0),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(badge,1);top.Children.Add(badge);}text.Children.Add(top);
        CreatorLink(name,item.Mid);var message=RichComment(item,nested?14:15,timeLinks:dynamicDetail==null&&CommentType==1);message.TextWrapping=TextWrapping.Wrap;message.LineHeight=nested?22:24;message.Margin=new Thickness(0,7,4,8);message.Foreground=Ink;if(item.Pinned){var pin=new Run("置顶  "){Foreground=Pink,FontSize=12};if(message.Inlines.FirstInline is {} first)message.Inlines.InsertBefore(first,pin);else message.Inlines.Add(pin);}text.Children.Add(SelectableRichText(message,"评论"));
        text.Children.Add(CommentMedia(item));
        var actions=new StackPanel{Orientation=Orientation.Horizontal};bool liked=item.Liked,hated=item.Hated;Button? hate=null;long count=item.Likes;var like=Button("");like.Padding=new Thickness(5,2,5,2);like.MinWidth=48;like.Height=28;like.Margin=new Thickness(0,0,4,0);
        void UpdateLike(){var value=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};var icon=Icon(liked?PackIconKind.ThumbUp:PackIconKind.ThumbUpOutline,16);icon.VerticalAlignment=VerticalAlignment.Center;value.Children.Add(icon);var n=Text(BilibiliClient.CountLabel(count),12);n.Foreground=liked?Pink:Muted;n.Margin=new Thickness(6,0,0,0);value.Children.Add(n);like.Content=value;like.Foreground=liked?Pink:Muted;}UpdateLike();like.ToolTip="点赞评论";
        like.Click+=async(_,_)=>await Run(async()=>{if(!RequireLogin())return;like.IsEnabled=false;try{var aid=CommentOid;var ct=CommentCancellation;await api.LikeComment(aid,item.Id,!liked,ct,CommentType);if(ct.IsCancellationRequested)return;count=Math.Max(0,count+(liked?-1:1));liked=!liked;if(liked)hated=false;UpdateCommentCache(item.Id,liked,count,hated);UpdateLike();UpdateHate();}finally{like.IsEnabled=true;}});actions.Children.Add(like);
        hate=IconButton(PackIconKind.ThumbDownOutline,"不喜欢这条评论",26);hate.Height=28;hate.Margin=new Thickness(0,0,2,0);
        void UpdateHate(){hate!.Content=Icon(hated?PackIconKind.ThumbDown:PackIconKind.ThumbDownOutline,16);hate.Foreground=hated?Pink:Muted;hate.ToolTip=hated?"取消不喜欢":"不喜欢这条评论";}UpdateHate();
        hate.Click+=async(_,_)=>await Run(async()=>{if(!RequireLogin())return;hate.IsEnabled=false;try{var ct=CommentCancellation;await api.HateComment(CommentOid,item.Id,!hated,ct,CommentType);if(ct.IsCancellationRequested)return;hated=!hated;if(hated&&liked){liked=false;count=Math.Max(0,count-1);}UpdateCommentCache(item.Id,liked,count,hated);UpdateLike();UpdateHate();}finally{hate.IsEnabled=true;}});actions.Children.Add(hate);
        var reply=Button("回复");reply.FontSize=12;reply.Foreground=Muted;reply.Height=28;reply.Padding=new Thickness(6,2,6,2);reply.Click+=(_,_)=>BeginCommentReply(item,root);
        var actionRow=new DockPanel();var moreActions=IconButton(PackIconKind.DotsVertical,"更多评论操作",26);moreActions.Height=28;moreActions.Foreground=Muted;moreActions.Click+=(_,_)=>ShowCommentMenu(item,root,moreActions);DockPanel.SetDock(moreActions,Dock.Right);actionRow.Children.Add(moreActions);DockPanel.SetDock(actions,Dock.Right);actionRow.Children.Add(actions);var meta=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};var time=Text(RelativeDate(item.CreatedAt),11);time.Foreground=Muted;time.VerticalAlignment=VerticalAlignment.Center;time.Margin=new Thickness(0,0,6,0);meta.Children.Add(time);meta.Children.Add(reply);actionRow.Children.Add(meta);text.Children.Add(actionRow);outer.MouseRightButtonUp+=(_,e)=>{e.Handled=true;ShowCommentMenu(item,root,moreActions);};
        if(item.UpLiked){var upLiked=Text("UP主觉得很赞",11);upLiked.Foreground=Muted;text.Children.Add(new Border{Child=upLiked,Background=Surface,Padding=new Thickness(6,3,6,3),Margin=new Thickness(0,6,0,0),HorizontalAlignment=HorizontalAlignment.Left});}
        if(!nested&&(item.Replies?.Count>0||item.ReplyCount>0))
        {
            text.Children.Add(BuildReplyPreview(item));
        }
        Grid.SetColumn(text,1);row.Children.Add(text);outer.Children.Add(row);if(!nested)outer.Children.Add(new Border{Height=1,Background=Surface,Margin=new Thickness(46,18,0,0)});return outer;
    }
    private FrameworkElement BuildReplyPreview(CommentItem item)
    {
        return RetainedReplyPreview(item);
    }
    private static string RelativeDate(long timestamp)
    {
        if(timestamp<=0)return "";var time=DateTimeOffset.FromUnixTimeSeconds(timestamp);var delta=DateTimeOffset.UtcNow-time;return delta.TotalMinutes<1?"刚刚":delta.TotalHours<1?$"{(int)delta.TotalMinutes}分钟前":delta.TotalDays<1?$"{(int)delta.TotalHours}小时前":delta.TotalDays<7?$"{(int)delta.TotalDays}天前":time.LocalDateTime.ToString("M-d");
    }
    private TextBlock RichComment(CommentItem item,double size,bool timeLinks=false)
    {
        var result=Text("",size);var tokens=item.Emotes??new Dictionary<string,CommentEmote>();var text=item.Message;var pending=new StringBuilder();
        void Flush(){if(pending.Length>0){result.Inlines.Add(new Run(pending.ToString()));pending.Clear();}}
        void Picture(string label,string url,double width)
        {
            Flush();var fallback=Text(label,size);fallback.FontFamily=new FontFamily("Segoe UI Emoji, Microsoft YaHei UI");var image=new Image{Width=width,Height=width,Stretch=Stretch.Uniform,Opacity=0};var box=new Grid{Width=width,Height=width,Margin=new Thickness(2,0,2,0),ToolTip=label};box.Children.Add(fallback);box.Children.Add(image);
            LoadImage(image,url,72,CommentCancellation,()=>{image.Opacity=1;fallback.Visibility=Visibility.Collapsed;});result.Inlines.Add(new InlineUIContainer(box){BaselineAlignment=BaselineAlignment.Center});
        }
        var links=(item.Links??new Dictionary<string,CommentLink>()).Where(p=>p.Value.Url.Length>0).OrderByDescending(p=>p.Key.Length).ToArray();var seen=new HashSet<string>();var automaticMatches=System.Text.RegularExpressions.Regex.Matches(text,@"BV[0-9A-Za-z]{10}|(?:av|cv)\d+|https?://[^\s<>]+",System.Text.RegularExpressions.RegexOptions.IgnoreCase).ToDictionary(m=>m.Index,m=>m.Value);
        var times=timeLinks?CommentTimestamps.Find(text).Where(t=>CommentTimePart(t)!=null).ToDictionary(t=>t.Index):[];
        void Link(string label,string target){Flush();var link=new Hyperlink(new Run(label)){Foreground=new SolidColorBrush(Color.FromRgb(0,161,214)),TextDecorations=null,ToolTip=target};link.Click+=async(_,e)=>{e.Handled=true;await Run(()=>NavigateCommentLink(target));};result.Inlines.Add(link);}
        for(int i=0;i<text.Length;)
        {
            var match=links.FirstOrDefault(p=>text.AsSpan(i).StartsWith(p.Key,StringComparison.Ordinal)&&(!p.Value.MatchOnce||!seen.Contains(p.Key)));if(match.Key!=null){Link(match.Value.Title,match.Value.Url);seen.Add(match.Key);i+=match.Key.Length;continue;}
            if(automaticMatches.TryGetValue(i,out var automatic)){var token=automatic.TrimEnd('。','，','）',')');Link(token,token);i+=token.Length;continue;}
            if(times.TryGetValue(i,out var timestamp))
            {
                Flush();var label=text.Substring(i,timestamp.Length);var bvid=detail!.Video.Bvid;var link=new Hyperlink(new Run(label)){Foreground=new SolidColorBrush(Color.FromRgb(0,161,214)),TextDecorations=null,ToolTip=label,Tag="comment-time:"+timestamp.Seconds};link.Click+=async(_,e)=>{e.Handled=true;await Run(()=>SeekCommentTime(bvid,timestamp));};result.Inlines.Add(link);i+=timestamp.Length;continue;
            }
            if(text[i]=='['){var end=text.IndexOf(']',i);if(end>=0&&end-i<=80&&tokens.TryGetValue(text[i..(end+1)],out var emote)){var label=text[i..(end+1)];Picture(label,emote.Url,emote.Size>=2?size*2.4:size+7);i=end+1;continue;}}
            var element=StringInfo.GetNextTextElement(text,i);var points=element.EnumerateRunes().Select(r=>r.Value).ToArray();bool emoji=points.Any(c=>c>=0x1F000&&c<=0x1FAFF||c==0x20E3||c==0xFE0F)&&points.Any(c=>c!=0xFE0F);
            if(emoji){var code=string.Join("-",points.Where(c=>c!=0xFE0F||element.Contains('\u200d')).Select(c=>c.ToString("x")));Picture(element,"https://cdn.jsdelivr.net/gh/jdecked/twemoji@17.0.3/assets/72x72/"+code+".png",size+6);}else pending.Append(element);i+=element.Length;
        }
        Flush();return result;
    }
}













