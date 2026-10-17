using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using VideoDesk.Core;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private WrapPanel? videoTagsPanel;
    private Task NavigateBackToVideo(string bvid)=>OpenVideo(bvid);
    private FrameworkElement CardMetadata(VideoCard video)
    {
        var row=new Grid{Margin=new Thickness(8,6,8,10)};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.Children.Add(CardAuthor(video.Creator,video.Mid,video.Followed,out _,video.LikeBadge));var date=Text(video.PublishedAt>0?CardDate(video.PublishedAt):video.Published,13);date.Foreground=Muted;date.Margin=new Thickness(5,0,0,0);Grid.SetColumn(date,1);row.Children.Add(date);return row;
    }
    private static string CardDate(long timestamp)
    {
        var published=TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.FromUnixTimeSeconds(timestamp),"China Standard Time");var age=DateTimeOffset.UtcNow-published;if(age.TotalMinutes<1)return "刚刚";if(age.TotalHours<1)return $"{(int)age.TotalMinutes}分钟前";if(age.TotalDays<1)return $"{(int)age.TotalHours}小时前";return published.ToString("yyyy年M月d日",CultureInfo.InvariantCulture);
    }
    private async Task LoadVideoTags(string bvid,long aid,CancellationToken ct)
    {
        try{var tags=await api.GetVideoTags(bvid,aid,ct);ct.ThrowIfCancellationRequested();if(detail?.Video.Bvid!=bvid||videoTagsPanel==null)return;RenderVideoTags(tags);}catch(OperationCanceledException){}catch(Exception e){Debug.WriteLine("Tags: "+e.Message);}
    }
    private void RenderVideoTags(IReadOnlyList<VideoTag> tags)
    {
        videoTagsPanel!.Children.Clear();foreach(var tag in tags)
        {
            var label=Text(tag.Name,13);label.Foreground=tag.Kind is "topic" or "old_channel"?new SolidColorBrush(Color.FromRgb(0,174,236)):Muted;var button=new Button{Style=null,BorderThickness=new Thickness(0),Height=32,Padding=new Thickness(12,5,12,5),Cursor=System.Windows.Input.Cursors.Hand};var rounded=new FrameworkElementFactory(typeof(Border));rounded.SetValue(Border.CornerRadiusProperty,new CornerRadius(16));rounded.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});var caption=new FrameworkElementFactory(typeof(ContentPresenter));caption.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);caption.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);caption.SetBinding(FrameworkElement.MarginProperty,new System.Windows.Data.Binding("Padding"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});rounded.AppendChild(caption);button.Template=new ControlTemplate(typeof(Button)){VisualTree=rounded};button.Content=label;button.Margin=new Thickness(0,5,8,5);button.Background=Surface;button.ToolTip="搜索标签："+tag.Name;button.Tag="video-tag";button.Click+=async(_,_)=>await Run(()=>SearchLinkedTerm(tag.Name,"video_tag"));videoTagsPanel.Children.Add(button);
        }
    }
    private async Task SearchLinkedTerm(string term,string source)
    {
        var previous=CaptureDiscovery() with{ReturnVideo=detail?.Video.Bvid};searchBack.Push(previous);keyword=term;search.Text=term;searchSource=source;searchOrder="totalrank";searching=true;searchHistory.Remove(term);searchHistory.Insert(0,term);if(searchHistory.Count>20)searchHistory.RemoveAt(20);PersistSearchHistory();await ShowHome();
    }
    private static Dictionary<string,string> LinkQuery(Uri uri)=>uri.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries).Select(s=>s.Split('=',2)).GroupBy(p=>p[0]).ToDictionary(g=>g.Key,g=>g.First().Length>1?Uri.UnescapeDataString(g.First()[1].Replace('+',' ')):"");
    private async Task NavigateCommentLink(string target)
    {
        var bvid=Regex.Match(target,@"BV[0-9A-Za-z]{10}",RegexOptions.IgnoreCase);if(bvid.Success){double? start=null;if(Uri.TryCreate(target,UriKind.Absolute,out var videoUri)&&LinkQuery(videoUri).TryGetValue("t",out var time)&&CommentTimestamps.TryParseQuery(time,out var value))start=value;var id="BV"+bvid.Value[2..];if(start is {} position&&id==detail?.Video.Bvid&&part!=null&&dynamicDetail==null){await SeekCommentPosition(part,position);return;}await OpenVideo(id,start);return;}
        var avid=Regex.Match(target,@"^(?:av)(\d+)$",RegexOptions.IgnoreCase);if(avid.Success){await OpenVideo(await api.ResolveVideoAid(long.Parse(avid.Groups[1].Value),navigation.Token));return;}
        var cv=Regex.Match(target,@"^cv(\d+)$",RegexOptions.IgnoreCase);if(cv.Success)target="https://www.bilibili.com/read/cv"+cv.Groups[1].Value;
        if(target.StartsWith("//"))target="https:"+target;if(!Uri.TryCreate(target,UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https")){Notify("这个链接没有可用的网页地址");return;}
        if(uri.Host=="search.bilibili.com"&&LinkQuery(uri).TryGetValue("keyword",out var term)){await SearchLinkedTerm(term,LinkQuery(uri).GetValueOrDefault("from_source","webcommentline_search"));return;}
        if(uri.Host=="space.bilibili.com"&&long.TryParse(uri.AbsolutePath.Trim('/'),out var mid)){await OpenCreator(mid);return;}
        if(uri.Host is "t.bilibili.com" or "www.bilibili.com"&&(System.Text.RegularExpressions.Regex.Match(uri.AbsolutePath,@"^/(?:opus/)?(\d+)$") is {} dynamicId)&&dynamicId.Success){var item=await api.GetDynamicDetail(dynamicId.Groups[1].Value,navigation.Token);await OpenDynamicDetail(item,false);return;}
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});
    }
    private FrameworkElement CommentMedia(CommentItem item)
    {
        var panel=new StackPanel();if(item.Pictures is {Count:>0} pictures)
        {
            var gallery=new WrapPanel{Margin=new Thickness(0,0,0,10)};foreach(var picture in pictures.Take(9))
            {
                var width=pictures.Count==1?(picture.Width>0&&picture.Height>0?Math.Min(260,180*picture.Width/(double)picture.Height):160):88;var height=pictures.Count==1?(picture.Width>0&&picture.Height>0?Math.Min(180,width*picture.Height/(double)picture.Width):120):88;
                var image=new Image{Width=width,Height=height,Stretch=pictures.Count==1?Stretch.Uniform:Stretch.UniformToFill};LoadImage(image,picture.Url,500,sourceAspect:picture.Height>0?picture.Width/(double)picture.Height:0);var border=new Border{Child=image,Background=Surface,CornerRadius=new CornerRadius(6),ClipToBounds=true};border.SizeChanged+=(_,e)=>border.Clip=new RectangleGeometry(new Rect(e.NewSize),6,6);var button=Button("");button.Content=border;button.Width=width;button.Height=height;button.Padding=new Thickness(0);button.Margin=new Thickness(0,0,6,6);button.ToolTip="查看评论图片";button.Tag="comment-picture";button.Click+=(_,_)=>ShowCommentPictures(pictures,pictures.ToList().IndexOf(picture));gallery.Children.Add(button);
            }panel.Children.Add(gallery);
        }
        if(item.Note is {} note){var button=Button("");var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(Icon(PackIconKind.NoteTextOutline,19));var text=Text("查看完整视频笔记  ›",13);text.Margin=new Thickness(8,0,0,0);row.Children.Add(text);button.Content=row;button.Background=Surface;button.Foreground=Pink;button.HorizontalContentAlignment=HorizontalAlignment.Left;button.Padding=new Thickness(12,8,12,8);button.Margin=new Thickness(0,0,0,10);button.Tag="comment-note";button.Click+=async(_,_)=>await Run(()=>NavigateCommentLink(note.Url));panel.Children.Add(button);}
        return panel;
    }
}

