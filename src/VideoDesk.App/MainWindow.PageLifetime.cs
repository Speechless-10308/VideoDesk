using System.Windows;
using System.Windows.Input;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private void OnPlayerStateChanged(MpvHost host,string name)
    {
        if(host!=player)return;
        var paused=host.Text("pause")=="yes"||host.Text("paused-for-cache")=="yes";
        if(name is "pause" or "paused-for-cache")
        {
            if(paused){timer.Stop();lastPlaybackTick=0;SaveProgress();}else timer.Start();
            PollPlayer(false);
        }
        else if(paused&&(name=="time-pos"||name=="demuxer-cache-duration"&&DateTime.UtcNow-lastBufferPoll>TimeSpan.FromMilliseconds(600)))PollPlayer(false);
    }
    private void ReleasePageReferences()
    {
        timer.Stop();lastPlaybackTick=0;dragging=false;reloadPlayback=false;
        // A retiring HwndHost can briefly outlive WPF navigation. Detach it so
        // its native lifetime cannot keep the rest of the page's visual tree.
        if(playerFrame!=null)playerFrame.Child=null;
        leftPlayer=null;playerFrame=null;playerTitle=null;danmakuRow=null;side=null;
        seek=null;timeText=null;pause=null;quality=null;qualityButton=null;speedButton=null;volumeSlider=null;chapterButton=null;chapterCaption=null;
        videoTabButton=null;videoCommentTabButton=null;videoScroll=null;onlineText=null;playbackCollection=null;
        followAction=null;likeAction=null;coinAction=null;favoriteAction=null;videoTagsPanel=null;
        if(commentMenu!=null){commentMenu.IsOpen=false;commentMenu.PlacementTarget=null;commentMenu.Child=null;commentMenu=null;}
        if(composerPopup!=null){composerPopup.IsOpen=false;composerPopup.PlacementTarget=null;composerPopup.Child=null;composerPopup=null;}
        commentView=null;commentItems=null;commentRows=null;commentCount=null;commentHeader=null;commentSort=null;
        commentInput=null;commentComposer=null;replyHint=null;commentScroll=null;commentSidebar=null;
        commentAttachments=null;commentSync=null;commentMentions.Clear();commentImagePaths.Clear();
        displayedComments=[];displayedCommentTotal=0;commentFirstPages.Clear();commentReplyStates.Clear();related.Clear();
        creatorVideoRows=null;creatorCollectionRows=null;creatorDynamicRows=null;creatorVideoContext="";
        openingVideoBack=null;openingVideoFeedback=null;
    }
    private void ReleasePageFocus()
    {
        // Hyperlink/Run/Paragraph belong to the content tree, not the visual
        // tree. Follow their content parents until reaching a visual owner.
        for(var node=FocusManager.GetFocusedElement(this) as DependencyObject;node!=null;)
        {
            if(ReferenceEquals(node,content)){FocusManager.SetFocusedElement(this,null);return;}
            node=node switch
            {
                ContentElement element=>ContentOperations.GetParent(element)??(element as FrameworkContentElement)?.Parent,
                System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D=>System.Windows.Media.VisualTreeHelper.GetParent(node),
                _=>LogicalTreeHelper.GetParent(node)
            };
        }
    }
    private async Task CloseApplicationAsync()
    {
        if(closingStarted)return;closingStarted=true;
        watchLaterWindow?.Close();StopCardPreview();storyboardPreview?.Dispose();storyboardPreview=null;playerFeaturesSession?.Cancel();playerFeaturesSession?.Dispose();playerFeaturesSession=null;SaveProgress();timer.Stop();navigation.Cancel();EndDanmakuSession();overlay?.Dispose();overlay=null;player?.Dispose();player=null;
        try{await laterHistoryFlush;await local.FlushProgressAsync();await FlushCloudHistory();await MpvHost.DrainShutdownsAsync();}
        catch(Exception error){closingStarted=false;Notify(error.Message);return;}
        api.Dispose();images.Dispose();await captures.DisposeAsync();closingApproved=true;_ = Dispatcher.BeginInvoke(Close);
    }
}
