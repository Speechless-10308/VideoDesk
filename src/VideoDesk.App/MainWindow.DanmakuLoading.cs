using System.Diagnostics;
using VideoDesk.Core;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private DanmakuSegmentCache? danmakuSegments;
    private CancellationTokenSource? danmakuSessionCancellation,danmakuWindowCancellation;
    private bool danmakuSessionReady;
    private int requestedDanmakuSegment;
    private DateTime danmakuRetryAfter;
    private readonly List<DanmakuItem> sentDanmaku=[];
    private void RememberSentDanmaku(DanmakuItem item)
    {
        sentDanmaku.Add(item);if(sentDanmaku.Count>32)sentDanmaku.RemoveAt(0);
        overlay?.Danmaku.ShowSent(item);
    }

    private void StartDanmakuSession(VideoPart videoPart,CancellationToken ct)
    {
        EndDanmakuSession();
        danmakuSessionCancellation=CancellationTokenSource.CreateLinkedTokenSource(ct);
        danmakuSegments=new(videoPart.Cid,videoPart.Duration,(index,token)=>api.GetDanmakuSegment(videoPart.Cid,index,token));
    }
    private void EndDanmakuSession()
    {
        danmakuWindowCancellation?.Cancel();danmakuWindowCancellation?.Dispose();danmakuWindowCancellation=null;
        danmakuSessionCancellation?.Cancel();danmakuSessionCancellation?.Dispose();danmakuSessionCancellation=null;
        danmakuSegments=null;danmakuMessages=[];danmakuSessionReady=false;requestedDanmakuSegment=0;danmakuRetryAfter=DateTime.MinValue;
        sentDanmaku.Clear();
    }
    private void RequestDanmakuWindow(double position)
    {
        if(!danmakuSessionReady||danmakuSegments is not {} session||danmakuSessionCancellation==null||DateTime.UtcNow<danmakuRetryAfter)return;
        var segment=session.SegmentAt(position);if(segment==requestedDanmakuSegment)return;
        requestedDanmakuSegment=segment;
        danmakuWindowCancellation?.Cancel();danmakuWindowCancellation?.Dispose();
        var request=danmakuWindowCancellation=CancellationTokenSource.CreateLinkedTokenSource(danmakuSessionCancellation.Token);
        _=UpdateDanmakuWindow(session,position,request);
    }
    private void RefreshDanmakuWindow()
    {
        if(danmakuSegments is not {} session)return;
        var position=player?.Number("time-pos")??0;
        danmakuWindowCancellation?.Cancel();session.Invalidate(session.SegmentAt(position));
        requestedDanmakuSegment=0;danmakuRetryAfter=DateTime.MinValue;RequestDanmakuWindow(position);
    }
    private async Task UpdateDanmakuWindow(DanmakuSegmentCache session,double position,CancellationTokenSource request)
    {
        var ct=request.Token;
        bool Current()=>!ct.IsCancellationRequested&&ReferenceEquals(session,danmakuSegments)&&ReferenceEquals(request,danmakuWindowCancellation)&&part?.Cid==session.Cid;
        void Install(IReadOnlyList<DanmakuItem> items)
        {
            if(!Current())return;
            danmakuMessages=sentDanmaku.Count==0?items:items.Concat(sentDanmaku).DistinctBy(m=>m.Id).OrderBy(m=>m.Time).ToArray();
            overlay?.Danmaku.SetWindowMessages(danmakuMessages);
        }
        try
        {
            // Show the sought segment as soon as it arrives; prefetch never
            // delays the first usable messages at the new playback position.
            Install(await session.GetSegmentAsync(session.SegmentAt(position),ct));
            if(Current())Install(await session.LoadWindowAsync(position,ct));
        }
        catch(OperationCanceledException){if(Current()){requestedDanmakuSegment=0;danmakuRetryAfter=DateTime.UtcNow.AddMilliseconds(250);}}
        catch(Exception error)
        {
            if(!Current())return;requestedDanmakuSegment=0;danmakuRetryAfter=DateTime.UtcNow.AddSeconds(10);
            Debug.WriteLine("Danmaku window: "+error.Message);Notify("弹幕分段加载失败，稍后重试："+error.Message);
        }
    }
}
