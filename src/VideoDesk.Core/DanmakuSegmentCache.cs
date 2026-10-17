namespace VideoDesk.Core;

public sealed class DanmakuSegmentCache(long cid, double duration, Func<int, CancellationToken, Task<IReadOnlyList<DanmakuItem>>> load)
{
    private const int Capacity = 4;
    private readonly object gate = new();
    private readonly Dictionary<int, Task<IReadOnlyList<DanmakuItem>>> segments = [];
    private readonly LinkedList<int> recent = [];
    public long Cid => cid;
    public int SegmentCount { get; } = (int)Math.Clamp(Math.Ceiling(double.IsFinite(duration) ? duration / 360 : 1), 1, 10000);
    public int CachedSegments { get { lock (gate) return segments.Count; } }
    public int SegmentAt(double position) => (int)Math.Clamp(Math.Floor(double.IsFinite(position) ? position / 360 : 0) + 1, 1, SegmentCount);
    public void Invalidate(int index){lock(gate){segments.Remove(index);recent.Remove(index);}}

    public Task<IReadOnlyList<DanmakuItem>> GetSegmentAsync(int index, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (index < 1 || index > SegmentCount) throw new ArgumentOutOfRangeException(nameof(index));
        Task<IReadOnlyList<DanmakuItem>> task;
        lock (gate)
        {
            if (!segments.TryGetValue(index, out task!) || task.IsFaulted || task.IsCanceled)
            {
                task = load(index, ct);
                segments[index] = task;
            }
            recent.Remove(index);
            recent.AddLast(index);
            while (segments.Count > Capacity)
            {
                var oldest = recent.First!.Value;
                recent.RemoveFirst();
                segments.Remove(oldest);
            }
        }
        return task.WaitAsync(ct);
    }

    public async Task<IReadOnlyList<DanmakuItem>> LoadWindowAsync(double position, CancellationToken ct)
    {
        var current = SegmentAt(position);
        // Keep the previous segment for messages crossing a boundary, then
        // prefetch only the next segment. Seeking loads the new window directly.
        var indices = new[] { current, current + 1, current - 1 }.Where(i => i >= 1 && i <= SegmentCount);
        var byId = new Dictionary<long, DanmakuItem>();
        foreach (var index in indices)
        {
            var items = await GetSegmentAsync(index, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            foreach (var item in items) byId.TryAdd(item.Id, item);
        }
        return byId.Values.OrderBy(item => item.Time).ThenBy(item => item.Id).ToArray();
    }
}
