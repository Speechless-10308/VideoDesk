using VideoDesk.Core;

namespace VideoDesk.Core.Tests;

public sealed class WeightedLruCacheTests
{
    [Fact]
    public void Put_OverMemoryBudget_EvictsLeastRecentlyUsedItem()
    {
        var cache = new WeightedLruCache<string, string>(10, 10);
        cache.Put("first", "a", 4);
        cache.Put("second", "b", 4);
        cache.TryGet("first", out _);

        cache.Put("third", "c", 4);

        Assert.False(cache.TryGet("second", out _));
        Assert.True(cache.TryGet("first", out _));
        Assert.True(cache.TryGet("third", out _));
        Assert.Equal(8, cache.Bytes);
    }

    [Fact]
    public void Put_OverItemLimit_EvictsOldestEvenWhenMemoryIsAvailable()
    {
        var cache = new WeightedLruCache<int, int>(2, 100);
        cache.Put(1, 1, 1);
        cache.Put(2, 2, 1);

        cache.Put(3, 3, 1);

        Assert.False(cache.TryGet(1, out _));
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void Put_ReplacingExistingKey_ReleasesOldWeight()
    {
        var cache = new WeightedLruCache<string, string>(2, 10);
        cache.Put("image", "large", 8);

        cache.Put("image", "small", 2);

        Assert.True(cache.TryGet("image", out var value));
        Assert.Equal("small", value);
        Assert.Equal(2, cache.Bytes);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void MaxBytes_ReducedBudget_TrimsImmediately()
    {
        var cache = new WeightedLruCache<int, int>(10, 10);
        cache.Put(1, 1, 4);
        cache.Put(2, 2, 4);

        cache.MaxBytes = 4;

        Assert.False(cache.TryGet(1, out _));
        Assert.True(cache.TryGet(2, out _));
        Assert.Equal(4, cache.Bytes);
    }

    [Fact]
    public void Put_OversizedItem_DoesNotEvictUnrelatedItems()
    {
        var cache = new WeightedLruCache<int, int>(10, 10);
        cache.Put(1, 1, 4);

        Assert.False(cache.Put(2, 2, 11));

        Assert.True(cache.TryGet(1, out _));
        Assert.Equal(4, cache.Bytes);
    }
}
