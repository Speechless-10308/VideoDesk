namespace VideoDesk.App;

// Share work, but keep cancellation local to each waiter. When the last waiter
// leaves, cancel the producer as well, so scrolled-away images stop doing work.
internal sealed class SharedImageRequests<TKey,T> where TKey:notnull
{
    private sealed class Entry(CancellationToken lifetime)
    {
        internal readonly CancellationTokenSource Cancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        internal Task<T> Task=null!;
        internal int Waiters=1;
        internal bool Completed;
    }
    private readonly object gate=new();
    private readonly Dictionary<TKey,Entry> entries=[];
    private readonly CancellationToken lifetime;
    internal SharedImageRequests(CancellationToken lifetime)=>this.lifetime=lifetime;
    internal async Task<T> Get(TKey key,Func<CancellationToken,Task<T>> factory,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();Entry entry;
        lock(gate)
        {
            if(entries.TryGetValue(key,out var found)&&!found.Completed&&!found.Cancellation.IsCancellationRequested){entry=found;entry.Waiters++;}
            else{entry=new(lifetime);entries[key]=entry;entry.Task=Produce(key,entry,factory);}
        }
        try{return await entry.Task.WaitAsync(ct).ConfigureAwait(false);}
        finally{lock(gate){entry.Waiters--;if(entry.Waiters==0&&!entry.Completed)entry.Cancellation.Cancel();}}
    }
    private async Task<T> Produce(TKey key,Entry entry,Func<CancellationToken,Task<T>> factory)
    {
        try{return await factory(entry.Cancellation.Token).ConfigureAwait(false);}
        finally
        {
            lock(gate){entry.Completed=true;if(entries.TryGetValue(key,out var current)&&ReferenceEquals(current,entry))entries.Remove(key);}
            entry.Cancellation.Dispose();
        }
    }
    internal void CancelAll()
    {
        lock(gate){foreach(var entry in entries.Values.ToArray())if(!entry.Completed)entry.Cancellation.Cancel();entries.Clear();}
    }
}
