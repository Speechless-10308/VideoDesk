using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using MaterialDesignThemes.Wpf;
using VideoDesk.Core;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private FrameworkElement BuildDynamicCard(CreatorDynamic item,bool forwarded=false)
    {
        var panel=new StackPanel();var author=new Grid();author.ColumnDefinitions.Add(new(){Width=new GridLength(forwarded?32:54)});author.ColumnDefinitions.Add(new());
        var face=UserAvatar(item.Avatar.Length>0?item.Avatar:creatorState?.Profile.Avatar??"",forwarded?24:42,item.Identity);face.HorizontalAlignment=HorizontalAlignment.Left;face.VerticalAlignment=VerticalAlignment.Top;CreatorLink(face,item.Mid);author.Children.Add(face);
        var name=item.Name.Length>0?item.Name:forwarded?"原动态":creatorState?.Profile.Name??"";var identity=new StackPanel();var label=Text(name+(forwarded&&item.Action.Length>0?"  "+item.Action:""),forwarded?13:16,forwarded?FontWeights.Normal:FontWeights.SemiBold);CreatorLink(label,item.Mid);identity.Children.Add(IdentityName(label,mid:item.Mid,identity:item.Identity));
        if(!forwarded){var date=Text(item.Date+(item.Action.Length>0?"  ·  "+item.Action:""),12);date.Foreground=Muted;date.Margin=new Thickness(0,4,0,0);identity.Children.Add(date);}Grid.SetColumn(identity,1);author.Children.Add(identity);panel.Children.Add(author);
        var body=new StackPanel{Margin=new Thickness(forwarded?0:54,10,0,0)};
        if(item.Deleted){var missing=Text("原动态已失效",14);missing.Foreground=Muted;body.Children.Add(missing);}
        if(item.Topic.Length>0){var topic=Button("# "+item.Topic);topic.Height=28;topic.Foreground=new SolidColorBrush(Color.FromRgb(0,161,214));topic.Padding=new Thickness(0);topic.HorizontalAlignment=HorizontalAlignment.Left;topic.Click+=async(_,_)=>await Run(()=>NavigateCommentLink(item.TopicUrl.Length>0?item.TopicUrl:"https://search.bilibili.com/all?keyword="+Uri.EscapeDataString(item.Topic)));body.Children.Add(topic);}
        if(item.Text.Length>0){var text=RichComment(new(name,item.Text,0,Emotes:item.Emotes,Links:item.Links),14);text.TextWrapping=TextWrapping.Wrap;text.LineHeight=23;text.Margin=new Thickness(0,0,0,16);body.Children.Add(SelectableRichText(text,"动态正文"));}
        if(item.Original!=null)body.Children.Add(BuildDynamicCard(item.Original,true));
        if(item.Video is {} video)body.Children.Add(DynamicVideoCard(video,item.VideoDescription));
        if(item.Pictures.Count>0)body.Children.Add(DynamicPictures(item));
        if(item.Attachment is {} attachment)
        {
            var card=new DockPanel();if(attachment.Cover.Length>0){var image=new Image{Width=150,Height=95,Stretch=Stretch.UniformToFill,Margin=new Thickness(0,0,14,0)};LoadImage(image,attachment.Cover,360);DockPanel.SetDock(image,Dock.Left);card.Children.Add(image);}var words=new StackPanel();words.Children.Add(Text(attachment.Title,14,FontWeights.SemiBold));var desc=Text(attachment.Description,13);desc.Foreground=Muted;desc.TextWrapping=TextWrapping.Wrap;desc.MaxHeight=44;words.Children.Add(desc);card.Children.Add(words);var button=Button("");button.Content=card;button.Height=double.NaN;button.Padding=new Thickness(12);button.Background=CanvasBrush;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.IsEnabled=attachment.Url.Length>0;button.Click+=async(_,_)=>await Run(()=>NavigateCommentLink(attachment.Url));body.Children.Add(button);
        }
        panel.Children.Add(body);
        if(!forwarded)
        {
            var actions=new System.Windows.Controls.Primitives.UniformGrid{Columns=3,Margin=new Thickness(44,12,0,0)};
            Button Action(PackIconKind kind,string title,long count){var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(Icon(kind,17));var text=Text(count>0?BilibiliClient.CountLabel(count):title,13);text.Margin=new Thickness(6,0,0,0);row.Children.Add(text);var button=Button("");button.Content=row;button.Height=34;button.Foreground=Muted;button.ToolTip=title;actions.Children.Add(button);return button;}
            var repost=Action(PackIconKind.ShareOutline,"转发",item.Reposts);repost.IsEnabled=!item.ForwardForbidden&&!item.Deleted;repost.Click+=async(_,_)=>await Run(()=>RepostCreatorDynamic(item));
            var comments=Action(PackIconKind.CommentOutline,"评论",item.Comments);comments.Tag="dynamic-comments:"+item.Id;comments.IsEnabled=!item.CommentForbidden&&item.CommentOid>0;comments.Click+=async(_,_)=>await Run(()=>ShowDynamicComments(item));
            bool liked=item.Liked;long likes=item.Likes;var like=Action(liked?PackIconKind.ThumbUp:PackIconKind.ThumbUpOutline,"点赞",likes);like.Foreground=liked?Pink:Muted;like.IsEnabled=!item.LikeForbidden;like.Click+=async(_,_)=>await Run(async()=>{if(!RequireLogin())return;like.IsEnabled=false;try{await api.LikeDynamic(item.Id,!liked,navigation.Token);liked=!liked;likes=Math.Max(0,likes+(liked?1:-1));if(like.Content is StackPanel row){((PackIcon)row.Children[0]).Kind=liked?PackIconKind.ThumbUp:PackIconKind.ThumbUpOutline;((TextBlock)row.Children[1]).Text=likes>0?BilibiliClient.CountLabel(likes):"点赞";}like.Foreground=liked?Pink:Muted;}finally{like.IsEnabled=true;}});panel.Children.Add(actions);
        }
        var dynamicCard=new Border{Child=panel,Background=forwarded?Surface:CanvasBrush,BorderBrush=Brushes.Transparent,BorderThickness=new Thickness(0),CornerRadius=new CornerRadius(16),Padding=new Thickness(forwarded?14:20),Margin=new Thickness(forwarded?0:7,0,forwarded?0:7,forwarded?12:14),MaxWidth=1000,HorizontalAlignment=HorizontalAlignment.Stretch};
        dynamicCard.Tag="dynamic-card:"+item.Id;
        return dynamicCard;
    }
    private FrameworkElement DynamicVideoCard(VideoCard video,string description)
    {
        var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new GridLength(204)});row.ColumnDefinitions.Add(new());var image=new Image{Height=115,Stretch=Stretch.UniformToFill};LoadImage(image,video.Cover,500);var cover=new Grid();cover.Children.Add(image);var duration=Text(BilibiliClient.DurationLabel(video.Duration),12);duration.Foreground=Brushes.White;cover.Children.Add(new Border{Child=duration,Background=new SolidColorBrush(Color.FromArgb(175,0,0,0)),Padding=new Thickness(5,2,5,2),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(4)});row.Children.Add(cover);
        var info=new DockPanel{Margin=new Thickness(14,8,12,8)};var stats=Text("▷ "+BilibiliClient.CountLabel(video.Views)+"    ▤ "+BilibiliClient.CountLabel(video.Danmaku),12);stats.Foreground=Muted;DockPanel.SetDock(stats,Dock.Bottom);info.Children.Add(stats);var words=new StackPanel();var title=Text(video.Title,14,FontWeights.Medium);title.TextWrapping=TextWrapping.Wrap;title.MaxHeight=42;title.TextTrimming=TextTrimming.CharacterEllipsis;words.Children.Add(title);var desc=Text(description,12);desc.Foreground=Muted;desc.TextWrapping=TextWrapping.Wrap;desc.TextTrimming=TextTrimming.CharacterEllipsis;desc.MaxHeight=36;desc.Margin=new Thickness(0,5,0,0);words.Children.Add(desc);info.Children.Add(words);Grid.SetColumn(info,1);row.Children.Add(info);
        var button=Button("");button.Content=row;button.Height=double.NaN;button.Padding=new Thickness(0);button.Background=CanvasBrush;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.Margin=new Thickness(0,0,0,4);button.ToolTip=video.Title;button.Click+=async(_,_)=>await Run(()=>OpenVideo(video.Bvid));AddLaterMenu(button,video);return button;
    }
    private async Task RepostCreatorDynamic(CreatorDynamic item)
    {
        if(!RequireLogin())return;var text=new TextBox{AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=90,MaxLength=2000,Padding=new Thickness(10),Margin=new Thickness(0,10,0,0)};HintAssist.SetHint(text,"说说你的想法");var panel=new StackPanel();panel.Children.Add(Text("转发到我的动态",20,FontWeights.SemiBold));var preview=Text(item.Name+"："+item.Text,13);preview.TextWrapping=TextWrapping.Wrap;preview.MaxHeight=65;preview.Foreground=Muted;preview.Margin=new Thickness(0,10,0,0);panel.Children.Add(preview);panel.Children.Add(text);if(!Dialog(panel,"转发"))return;await api.RepostDynamic(item.Id,account!.Mid,text.Text.Length>0?text.Text:"转发动态",navigation.Token);Notify("动态已转发");
    }
    private Task ShowDynamicComments(CreatorDynamic item)
    {
        if(dynamicHost!=null)return OpenDynamicSidebar(item);
        if(dynamicDetail?.Id==item.Id){commentInput?.Focus();return Task.CompletedTask;}
        return OpenDynamicDetail(item);
    }
    private async Task OpenDynamicDetail(CreatorDynamic item,bool refresh=true,bool newest=false)
    {
        if(item.Deleted||string.IsNullOrWhiteSpace(item.Id)){Notify("原动态已失效");return;}
        var ct=BeginPage();dynamicDetail=item;commentsNewest=newest;SelectNav(homeNav);
        var layout=new DockPanel();var heading=new DockPanel{Margin=new Thickness(0,0,0,18)};
        var back=BackButton();back.Margin=new Thickness(0,0,8,0);back.Click+=async(_,_)=>await Run(GoBack);DockPanel.SetDock(back,Dock.Left);heading.Children.Add(back);heading.Children.Add(Text("动态详情",23,FontWeights.SemiBold));DockPanel.SetDock(heading,Dock.Top);layout.Children.Add(heading);
        var columns=new Grid();columns.ColumnDefinitions.Add(new());columns.ColumnDefinitions.Add(new(){Width=new GridLength(400)});var cardHost=new StackPanel{Margin=new Thickness(0,0,22,0)};dynamicScroll=new SmoothScrollViewer{Content=cardHost,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};columns.Children.Add(dynamicScroll);cardHost.Children.Add(BuildDynamicCard(item));
        var comments=new DockPanel();commentComposer=BuildCommentComposer();DockPanel.SetDock(commentComposer,Dock.Bottom);comments.Children.Add(commentComposer);
        commentHeader=new Border{Margin=new Thickness(0,0,8,18)};DockPanel.SetDock(commentHeader,Dock.Top);comments.Children.Add(commentHeader);commentSidebar=new();commentScroll=new SmoothScrollViewer{Content=commentSidebar,ShowBackToTop=false,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};comments.Children.Add(commentScroll);Grid.SetColumn(comments,1);columns.Children.Add(comments);layout.Children.Add(columns);content.Children.Add(layout);
        if(refresh){try{var current=await api.GetDynamicDetail(item.Id,ct);ct.ThrowIfCancellationRequested();dynamicDetail=current;cardHost.Children.Clear();cardHost.Children.Add(BuildDynamicCard(current));}catch(Exception error) when(error is not OperationCanceledException){Notify("动态详情刷新失败："+error.Message);}}
        if(dynamicDetail.CommentForbidden||CommentOid<=0){commentSidebar.Children.Add(Text("这条动态暂不支持评论",14));commentComposer.Visibility=Visibility.Collapsed;return;}
        await LoadComments();
    }
}


