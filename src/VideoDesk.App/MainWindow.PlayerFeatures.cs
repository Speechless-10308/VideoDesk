using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VideoDesk.Core;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private IReadOnlyList<VideoChapter> videoChapters=[];
    private Button? chapterButton;
    private TextBlock? chapterCaption;
    private CancellationTokenSource? playerFeaturesSession;

    private void ResetPlayerFeatures()
    {
        playerFeaturesSession?.Cancel();playerFeaturesSession?.Dispose();playerFeaturesSession=null;
        videoChapters=[];if(chapterButton!=null)chapterButton.Visibility=Visibility.Collapsed;
        (seek as PlaybackTimeline)?.SetChapters([]);overlay?.Interactions.SetItems([],null);overlay?.ClosePanel();
        if(overlay==null||detail==null||part==null)return;
        playerFeaturesSession=CancellationTokenSource.CreateLinkedTokenSource(navigation.Token);
        _=LoadPlayerFeatures(detail.Video.Bvid,detail.Video.Aid,part.Cid,playerFeaturesSession.Token);
    }
    private async Task LoadPlayerFeatures(string bvid,long aid,long cid,CancellationToken ct)
    {
        async Task Chapters()
        {
            try
            {
                var chapters=await api.GetVideoChapters(bvid,cid,ct);ct.ThrowIfCancellationRequested();
                if(part?.Cid!=cid||detail?.Video.Bvid!=bvid)return;
                videoChapters=chapters;(seek as PlaybackTimeline)?.SetChapters(chapters);storyboardPreview?.SetChapters(chapters);
                if(chapterButton!=null)chapterButton.Visibility=chapters.Count==0?Visibility.Collapsed:Visibility.Visible;
                UpdatePlayerFeatures(player?.Number("time-pos")??0);
            }
            catch(OperationCanceledException){}catch(Exception error) when(error is System.Net.Http.HttpRequestException or BilibiliException or System.IO.IOException or TimeoutException){System.Diagnostics.Debug.WriteLine(error.Message);}
        }
        async Task Interactions()
        {
            try
            {
                var items=await api.GetVideoInteractions(aid,cid,ct);ct.ThrowIfCancellationRequested();
                if(part?.Cid!=cid||detail?.Video.Bvid!=bvid||overlay==null)return;
                overlay.Interactions.SetItems(items,async(item,selection,seconds,token)=>
                {
                    if(!RequireLogin())throw new InvalidOperationException("请先登录");
                    await api.SubmitVideoInteraction(aid,cid,item,selection,seconds,token);
                    try
                    {
                        var fresh=(await api.GetVideoInteractions(aid,cid,token)).FirstOrDefault(i=>i.CommandId==item.CommandId);
                        if(fresh!=null)return fresh with{Selection=selection};
                    }
                    catch(Exception error) when(error is System.Net.Http.HttpRequestException or BilibiliException or System.IO.IOException or TimeoutException){}
                    return item with{Selection=selection};
                },ct);
                UpdatePlayerFeatures(player?.Number("time-pos")??0);
            }
            catch(OperationCanceledException){}catch(Exception error) when(error is System.Net.Http.HttpRequestException or BilibiliException or System.IO.IOException or TimeoutException){System.Diagnostics.Debug.WriteLine(error.Message);}
        }
        await Task.WhenAll(Chapters(),Interactions());
    }
    private void UpdatePlayerFeatures(double seconds)
    {
        if(chapterCaption!=null)
        {
            var title=VideoPlayerFeatures.ChapterAt(videoChapters,seconds)?.Title;
            var caption=title??"章节";if(chapterCaption.Text!=caption)chapterCaption.Text=caption;
        }
        var aspect=player?.Ready==true&&player.Number("video-params/h")>0?player.Number("video-params/w")/player.Number("video-params/h"):16d/9;
        overlay?.Interactions.Update(seconds,aspect,danmakuEnabled);
    }
    private void ShowVideoChapters()
    {
        if(videoChapters.Count==0||chapterButton==null)return;
        var list=new StackPanel();var current=VideoPlayerFeatures.ChapterAt(videoChapters,player?.Number("time-pos")??0);
        foreach(var chapter in videoChapters)
        {
            var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new GridLength(44)});row.ColumnDefinitions.Add(new());
            var time=Text(BilibiliClient.DurationLabel(chapter.Start),12);time.Foreground=Brushes.LightGray;time.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(time);
            var title=Text(chapter.Title,13);title.Foreground=Brushes.White;title.TextTrimming=TextTrimming.CharacterEllipsis;title.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(title,1);row.Children.Add(title);
            var button=Button("");button.Content=row;button.Height=36;button.Padding=new(8,0,8,0);button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.ToolTip=chapter.Title;
            button.Background=chapter==current?new SolidColorBrush(Color.FromArgb(70,255,255,255)):Brushes.Transparent;
            button.Click+=(_,_)=>{overlay?.ClosePanel();player?.Command("seek",chapter.Start.ToString(CultureInfo.InvariantCulture),"absolute+exact");PollPlayer(false);overlay?.Wake();};list.Children.Add(button);
        }
        var scroll=new ScrollViewer{Width=206,Content=list,MaxHeight=300,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        overlay?.ShowPanel(scroll,chapterButton,new SolidColorBrush(Color.FromArgb(240,24,24,28)),new SolidColorBrush(Color.FromArgb(55,255,255,255)));
    }
}
