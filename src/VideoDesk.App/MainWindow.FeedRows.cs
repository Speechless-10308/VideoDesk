using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private void ReflowFeedRows(FeedState state)
    {
        var columns=CardColumns();state.Columns=columns;var changed=new HashSet<FeedBatch>();
        for(int i=0;i+1<state.Batches.Count;i++)
        {
            var batch=state.Batches[i];var missing=(columns-batch.Videos.Count%columns)%columns;
            while(missing>0&&i+1<state.Batches.Count)
            {
                var next=state.Batches[i+1];if(next.Label.Length>0)break;
                var take=Math.Min(missing,next.Videos.Count);batch.Videos=batch.Videos.Concat(next.Videos.Take(take)).ToArray();next.Videos=next.Videos.Skip(take).ToArray();changed.Add(batch);changed.Add(next);missing-=take;
                if(next.Videos.Count>0)break;
                state.Batches.RemoveAt(i+1);if(next.Element?.Parent is Panel parent)parent.Children.Remove(next.Element);if(next.Element!=null)next.Element.Child=null;
            }
        }
        foreach(var batch in changed)
        {
            var element=batch.Element;var rendered=element?.Child!=null;batch.MeasuredHeight=0;if(element==null)continue;element.Child=null;element.Height=double.NaN;
            if(!state.Batches.Contains(batch))continue;
            if(currentFeed==state&&element.Parent==feedPanel&&rendered)RenderBatch(batch);else BatchPlaceholder(batch);
        }
    }
    private void QueueFeedTailFill(FeedState state,ScrollViewer scroll,CancellationToken ct)
    {
        if(state.TailFillQueued)return;state.TailFillQueued=true;
        _ = Dispatcher.BeginInvoke(new Action(async()=>
        {
            state.TailFillQueued=false;
            if(ct.IsCancellationRequested||currentFeed!=state||feedScroll!=scroll||!scroll.IsLoaded||state.Loading||state.Ended)return;
            var tail=state.Batches.LastOrDefault();if(tail==null||tail.Videos.Count%CardColumns()==0||scroll.ScrollableHeight-scroll.VerticalOffset>=700)return;
            await Run(()=>LoadFeedPage(state,ct));
        }),DispatcherPriority.Loaded);
    }
}
