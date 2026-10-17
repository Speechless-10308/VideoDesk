using System.Windows.Threading;
using VideoDesk.Core;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private (MpvHost Host,long Revision)? queuedCompletion;

    private void QueuePlaybackCompletion(MpvHost host,long revision)
    {
        if(!ReferenceEquals(player,host)||detail==null||part==null||host.LoadRevision!=revision||queuedCompletion==(host,revision))return;
        var bvid=detail.Video.Bvid;var cid=part.Cid;var ct=navigation.Token;queuedCompletion=(host,revision);
        // Finish pumping native events before replacing or disposing the host.
        _=Dispatcher.BeginInvoke(new Action(async()=>
        {
            try
            {
                if(ct.IsCancellationRequested||!ReferenceEquals(player,host)||host.LoadRevision!=revision||detail?.Video.Bvid!=bvid||part?.Cid!=cid)return;
                await Run(()=>AdvanceCompletedPlayback(ct));
            }
            finally{if(queuedCompletion==(host,revision))queuedCompletion=null;}
        }),DispatcherPriority.Background);
    }
    private async Task AdvanceCompletedPlayback(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();if(detail==null||part==null)return;
        var index=detail.Parts.ToList().FindIndex(p=>p.Cid==part.Cid);
        if(index>=0&&index+1<detail.Parts.Count){var next=detail.Parts[index+1];await ChangePart(next,0);return;}
        if(playbackCollection!=null)await SwitchCollection(1,fromBeginning:true);
    }
}
