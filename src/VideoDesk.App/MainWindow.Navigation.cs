using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VideoDesk.Core;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private readonly List<Func<Task>> pageBack=[];
    private bool restoringPage;
    private CreatorDynamic? dynamicDetail;
    private SmoothScrollViewer? dynamicScroll;
    private string staticPage="";
    private sealed record ComposerSnapshot(string Text,long Root,long Parent,string Hint,IReadOnlyDictionary<string,long> Mentions,IReadOnlyList<string> Images,bool Sync);
    private ComposerSnapshot CaptureComposer()=>new(commentInput?.Text??"",replyRoot,replyParent,replyHint?.Text??"",new Dictionary<string,long>(commentMentions),commentImagePaths.ToArray(),commentSync?.IsChecked==true);
    private void RestoreComposer(ComposerSnapshot saved)
    {
        if(commentInput==null)return;commentInput.Text=saved.Text;replyRoot=saved.Root;replyParent=saved.Parent;
        if(replyHint!=null){replyHint.Text=saved.Hint;replyHint.Visibility=saved.Root>0?Visibility.Visible:Visibility.Collapsed;}
        commentMentions.Clear();foreach(var pair in saved.Mentions)commentMentions[pair.Key]=pair.Value;commentImagePaths.Clear();commentImagePaths.AddRange(saved.Images);RenderCommentAttachments();if(commentSync!=null)commentSync.IsChecked=saved.Sync;
    }
    private void RememberPage()
    {
        if(restoringPage)return;Func<Task>? restore=null;
        var drawerItem=DynamicDrawerOpen?dynamicDetail:null;SaveDynamicDraft();
        if(dynamicHost==null&&dynamicDetail is {} dynamic)
        {
            var offset=dynamicScroll?.VerticalOffset??0;var comments=commentScroll?.VerticalOffset??0;var draft=CaptureComposer();var newest=commentsNewest;
            restore=async()=>{await OpenDynamicDetail(dynamic,false,newest);RestoreComposer(draft);await Dispatcher.InvokeAsync(()=>{dynamicScroll?.ScrollToVerticalOffset(offset);commentScroll?.ScrollToVerticalOffset(comments);},DispatcherPriority.Loaded);};
        }
        else if(creatorState is {} creator)
        {
            var offset=creatorScroll?.VerticalOffset??0;restore=()=>OpenCreator(creator.Profile.Mid,creator,offset);
        }
        else if(detail is {} video)
        {
            var position=player?.Number("time-pos")??0;var paused=player?.Text("pause")=="yes";var comments=commentView?.Visibility==Visibility.Visible;var offset=commentScroll?.VerticalOffset??0;var newest=commentsNewest;var draft=CaptureComposer();var collection=playbackCollection;var savedCollectionPage=collectionPage;
            restore=async()=>{await OpenVideo(video.Video.Bvid,position);playbackCollection=collection;collectionPage=savedCollectionPage;BuildVideoSidebar();if(paused)player?.Set("pause","yes");if(comments){commentsNewest=newest;await SelectVideoComments(true);RestoreComposer(draft);await Dispatcher.InvokeAsync(()=>commentScroll?.ScrollToVerticalOffset(offset),DispatcherPriority.Loaded);}};
        }
        else if(currentFeed!=null||staticPage is "following" or "popular"){var discovery=CaptureDiscovery();restore=()=>RestoreDiscovery(discovery);}
        else if(staticPage=="history")restore=ShowHistory;
        else if(staticPage=="favorites")restore=ShowFavorites;
        else if(staticPage=="settings")restore=ShowSettings;
        if(restore==null)return;
        if(drawerItem!=null){var underlying=restore;restore=async()=>{await underlying();if(dynamicHost!=null)await OpenDynamicSidebar(drawerItem);};}
        pageBack.Add(restore);if(pageBack.Count>24)pageBack.RemoveAt(0);
    }
    private async Task GoBack()
    {
        if(DynamicDrawerOpen){CloseDynamicSidebar();return;}
        if(pageBack.Count==0){await ShowHome();return;}var restore=pageBack[^1];pageBack.RemoveAt(pageBack.Count-1);restoringPage=true;
        try{await restore();}finally{restoringPage=false;}
    }
}

