using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VideoDesk.Core;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private CancellationTokenSource? collectionOpen;
    private CancellationTokenSource? streamOpen;

    private void CancelCollectionOpen(){collectionOpen?.Cancel();collectionOpen?.Dispose();collectionOpen=null;}
    private void CancelStreamOpen(){streamOpen?.Cancel();streamOpen?.Dispose();streamOpen=null;}
    private CancellationTokenSource BeginStreamOpen(CancellationToken ct)
    {
        CancelStreamOpen();return streamOpen=CancellationTokenSource.CreateLinkedTokenSource(ct);
    }
    private void EndStreamOpen(CancellationTokenSource request)
    {
        if(ReferenceEquals(streamOpen,request)){streamOpen=null;request.Dispose();}
    }

    private void RefreshPlayerTitle()
    {
        if(playerTitle is not StackPanel panel||detail==null)return;
        panel.Children.Clear();var row=new DockPanel();var back=BackButton();back.Margin=new(0,0,8,0);back.VerticalAlignment=VerticalAlignment.Top;
        back.Click+=async(_,_)=>await Run(GoBack);DockPanel.SetDock(back,Dock.Left);row.Children.Add(back);
        var title=Text(detail.Video.Title,23,FontWeights.SemiBold);title.TextWrapping=TextWrapping.Wrap;title.MaxHeight=64;title.TextTrimming=TextTrimming.CharacterEllipsis;title.ToolTip=detail.Video.Title;
        row.Children.Add(SelectableText(title,"视频标题"));panel.Children.Add(row);
        var stats=Text($"▷ {BilibiliClient.CountLabel(detail.Video.Views)}    ▤ {BilibiliClient.CountLabel(detail.Video.Danmaku)}    {detail.Video.Published}",13);stats.Foreground=Muted;stats.Margin=new(48,7,0,0);panel.Children.Add(stats);
    }

    private async Task OpenCollectionVideo(string bvid,double? startOverride,bool firstPart,bool rememberPage,long? preferredCid)
    {
        var host=player!;var oldVideo=detail!;var oldPart=part!;var oldRevision=host.LoadRevision;
        CancelCollectionOpen();var request=collectionOpen=CancellationTokenSource.CreateLinkedTokenSource(navigation.Token);var ct=request.Token;
        var watch=Stopwatch.StartNew();
        try
        {
            // Keep the existing surface while preparing the next
            // file. Only commit metadata when its playable URL is available.
            var fetched=await api.GetDetail(bvid,ct);ct.ThrowIfCancellationRequested();
            var selected=fetched.Parts.FirstOrDefault()??throw new System.IO.InvalidDataException("这个视频没有可播放分集");
            var progress=local.GetProgress(bvid);
            if(preferredCid is {} wanted)selected=fetched.Parts.FirstOrDefault(p=>p.Cid==wanted)??selected;
            if(progress!=null&&!firstPart)selected=fetched.Parts.FirstOrDefault(p=>p.Cid==progress.Cid)??selected;
            var start=startOverride??(progress!=null&&progress.Cid==selected.Cid&&progress.Position<progress.Duration-10?progress.Position:0);
            var metadataMs=watch.Elapsed.TotalMilliseconds;
            var stream=await api.GetPlayStream(bvid,selected.Cid,preferences.DefaultQuality,ct:ct);ct.ThrowIfCancellationRequested();
            var streamReadyMs=watch.Elapsed.TotalMilliseconds;
            if(!ReferenceEquals(collectionOpen,request)||player!=host||detail?.Video.Bvid!=oldVideo.Video.Bvid||part?.Cid!=oldPart.Cid||host.LoadRevision!=oldRevision)return;
            CancelStreamOpen();
            if(rememberPage)RememberPage();SaveProgress();_=FlushCloudHistory();DismissFeedback();ReleasePageFocus();
            navigation.Cancel();navigation.Dispose();navigation=new();var token=navigation.Token;
            EndDanmakuSession();host.PrepareLoad(start);detail=fetched;part=selected;
            watchStartedAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds();playedSeconds=0;lastPlaybackTick=0;lastBufferPoll=DateTime.MinValue;
            related.Clear();videoState=new(false,0,false);followingCreator=false;accountStateReady=false;accountStateLoading=false;interactionBusy=false;accountPending="";accountRevision++;
            overlay?.ClosePanel();overlay?.Danmaku.SetMessages([]);overlay?.Danmaku.Mask?.Dispose();if(overlay!=null)overlay.Danmaku.Mask=null;maskCid=0;
            seek?.SetHeatmap(null);seek?.SetBuffered([]);seek!.Maximum=Math.Max(1,selected.Duration);seek.Value=start;
            if(timeText!=null)timeText.Text=BilibiliClient.DurationLabel(start)+" / "+BilibiliClient.DurationLabel(selected.Duration);
            PopulateQualities(stream);host.Load(stream,start);timer.Start();
            RefreshPlayerTitle();ResetVideoComments();BuildVideoSidebar();videoScroll?.ScrollToTop();ResetStoryboardPreview();
            StartDanmakuSession(selected,token);var first=FirstDanmaku(selected,start,token);
            _=LoadDanmaku(selected,token,first,firstIndex:(int)(start/360)+1);_=LoadRelatedBoth(bvid,token);_=LoadAccountActions(bvid,fetched.Video.Aid,fetched.Video.Mid,token);_=RefreshWatchMetadata(selected,token);_=LoadVideoTags(bvid,fetched.Video.Aid,token);
            if(App.Diagnostics.Enabled){var revision=host.LoadRevision;_=App.Diagnostics.AppendAsync("player-timing.log",()=>FormattableString.Invariant($"collection-reuse metadata={metadataMs:F1} ms ready-url={streamReadyMs:F1} ms revision={revision}")+Environment.NewLine);}
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){}
        finally
        {
            if(ReferenceEquals(collectionOpen,request))
            {
                collectionOpen=null;request.Dispose();
            }
        }
    }

    private void ResetVideoComments()
    {
        if(commentMenu!=null)commentMenu.IsOpen=false;if(composerPopup!=null)composerPopup.IsOpen=false;
        if(commentComposer is {} previous&&previous.Parent is DockPanel parent)
        {
            var index=parent.Children.IndexOf(previous);parent.Children.Remove(previous);commentComposer=BuildCommentComposer();DockPanel.SetDock(commentComposer,Dock.Bottom);parent.Children.Insert(index,commentComposer);
        }
        commentsLoading=false;commentSidebar?.Children.Clear();if(commentHeader!=null)commentHeader.Child=null;
        if(commentView!=null)commentView.Visibility=Visibility.Collapsed;if(commentComposer!=null)commentComposer.Visibility=Visibility.Collapsed;if(videoScroll!=null)videoScroll.Visibility=Visibility.Visible;
        if(videoTabButton!=null){videoTabButton.Background=Pink;videoTabButton.Foreground=Brushes.White;}if(videoCommentTabButton!=null){videoCommentTabButton.Background=Surface;videoCommentTabButton.Foreground=Muted;}
    }
}
