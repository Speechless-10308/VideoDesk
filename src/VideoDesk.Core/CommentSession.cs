namespace VideoDesk.Core;

// Like the official add event, confirmed server replies enter the current view immediately.
// A lagging first-page response cannot remove a reply that the server just accepted.
public sealed class CommentSession
{
    private sealed record Posted(long Root,CommentItem Reply,DateTimeOffset Added);
    private readonly Dictionary<long,List<Posted>> posted=[];
    private readonly Dictionary<long,Dictionary<long,DateTimeOffset>> removed=[];
    private readonly Dictionary<long,(long Id,DateTimeOffset At)> pins=[];
    public void SetPinned(long aid,long id)=>pins[aid]=(id,DateTimeOffset.UtcNow);
    public void Remove(long aid,long id)
    {
        if(posted.TryGetValue(aid,out var list))list.RemoveAll(p=>p.Reply.Id==id||p.Root==id);
        if(!removed.TryGetValue(aid,out var ids))removed[aid]=ids=[];ids[id]=DateTimeOffset.UtcNow;if(ids.Count>100)ids.Remove(ids.MinBy(p=>p.Value).Key);
    }
    private bool Removed(long aid,long id)=>removed.TryGetValue(aid,out var ids)&&ids.TryGetValue(id,out var at)&&DateTimeOffset.UtcNow-at<TimeSpan.FromMinutes(15);
    public void Add(long aid,long root,CommentItem reply)
    {
        if(reply.Id<=0)throw new ArgumentException("A server comment ID is required");if(!posted.TryGetValue(aid,out var list))posted[aid]=list=[];list.RemoveAll(p=>p.Reply.Id==reply.Id||DateTimeOffset.UtcNow-p.Added>TimeSpan.FromMinutes(15));list.Add(new(root,reply,DateTimeOffset.UtcNow));if(list.Count>100)list.RemoveRange(0,list.Count-100);
    }
    public IReadOnlyList<CommentItem> Merge(long aid,IReadOnlyList<CommentItem> items,bool includeNewRoots=true)
    {
        var list=posted.GetValueOrDefault(aid)??[];list.RemoveAll(p=>DateTimeOffset.UtcNow-p.Added>TimeSpan.FromMinutes(15));
        CommentItem Children(CommentItem item){var previous=item.Replies??[];if(previous.Count==0&&!list.Any(p=>p.Root==item.Id))return item;var replies=previous.Where(c=>!Removed(aid,c.Id)).Select(Children).ToList();foreach(var child in list.Where(p=>p.Root==item.Id&&!Removed(aid,p.Reply.Id)))if(replies.All(r=>r.Id!=child.Reply.Id))replies.Add(child.Reply);var count=Math.Max(item.ReplyCount-previous.Count(c=>Removed(aid,c.Id)),replies.Count);return count==item.ReplyCount&&replies.SequenceEqual(previous)?item:item with{Replies=replies,ReplyCount=count};}
        var result=items.Where(c=>!Removed(aid,c.Id)).Select(Children).ToList();if(includeNewRoots)foreach(var root in list.Where(p=>p.Root==0&&!Removed(aid,p.Reply.Id)))if(result.All(r=>r.Id!=root.Reply.Id))result.Insert(0,Children(root.Reply));if(pins.TryGetValue(aid,out var pin)&&DateTimeOffset.UtcNow-pin.At<TimeSpan.FromMinutes(15)){result=result.Select(c=>c with{Pinned=c.Id==pin.Id}).OrderByDescending(c=>c.Pinned).ToList();}return result;
    }
    public IReadOnlyList<CommentItem> MergeReplies(long aid,long root,IReadOnlyList<CommentItem> items)
    {
        var result=items.Where(c=>!Removed(aid,c.Id)).ToList();if(posted.TryGetValue(aid,out var list))foreach(var child in list.Where(p=>p.Root==root&&!Removed(aid,p.Reply.Id)))if(result.All(r=>r.Id!=child.Reply.Id))result.Add(child.Reply);return result;
    }
    public void UpdateLike(long aid,long id,bool liked,long count,bool? hated=null){if(!posted.TryGetValue(aid,out var list))return;for(int i=0;i<list.Count;i++)if(list[i].Reply.Id==id)list[i]=list[i] with{Reply=list[i].Reply with{Liked=liked,Likes=count,Hated=hated??(liked?false:list[i].Reply.Hated)}};}
}
