namespace VideoDesk.Core;

// Callers provide synchronization when sharing a cache between threads.
public sealed class WeightedLruCache<TKey, TValue>(int capacity, long maxBytes) where TKey : notnull
{
    private sealed record Entry(TValue Value, long Bytes, LinkedListNode<TKey> Node);
    private readonly Dictionary<TKey, Entry> entries = [];
    private readonly LinkedList<TKey> recent = [];
    private long budget = Math.Max(0, maxBytes);
    public int Count => entries.Count;
    public long Bytes { get; private set; }
    public long Evictions { get; private set; }
    public long MaxBytes
    {
        get => budget;
        set { budget = Math.Max(0, value); Trim(); }
    }

    public bool TryGet(TKey key, out TValue value)
    {
        if (!entries.TryGetValue(key, out var entry)) { value = default!; return false; }
        recent.Remove(entry.Node);
        recent.AddLast(entry.Node);
        value = entry.Value;
        return true;
    }

    public bool Put(TKey key, TValue value, long bytes)
    {
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        Remove(key);
        if (capacity <= 0 || bytes > budget) return false;
        while (entries.Count >= capacity || Bytes > budget - bytes) EvictOldest();
        var node = recent.AddLast(key);
        entries.Add(key, new(value, bytes, node));
        Bytes += bytes;
        return true;
    }

    public void Clear() { entries.Clear(); recent.Clear(); Bytes = 0; }
    private void Remove(TKey key)
    {
        if (!entries.Remove(key, out var entry)) return;
        recent.Remove(entry.Node);
        Bytes -= entry.Bytes;
    }
    private void EvictOldest() { Remove(recent.First!.Value); Evictions++; }
    private void Trim() { while (entries.Count > 0 && (entries.Count > capacity || Bytes > budget)) EvictOldest(); }
}
