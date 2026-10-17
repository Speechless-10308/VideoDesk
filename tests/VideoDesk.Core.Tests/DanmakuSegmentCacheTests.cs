using VideoDesk.Core;

namespace VideoDesk.Core.Tests;

public sealed class DanmakuSegmentCacheTests
{
    [Fact]
    public async Task LoadWindowAsync_Seek_LoadsOnlyAdjacentSegments()
    {
        var requested = new List<int>();
        var cache = new DanmakuSegmentCache(42, 7200, (index, _) =>
        {
            requested.Add(index);
            return Task.FromResult<IReadOnlyList<DanmakuItem>>([]);
        });

        await cache.LoadWindowAsync(3600, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { 10, 11, 12 }, requested.Order());
    }

    [Fact]
    public async Task GetSegmentAsync_ConcurrentReaders_SharePendingRequest()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<DanmakuItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        var cache = new DanmakuSegmentCache(42, 720, (_, _) =>
        {
            requests++;
            return completion.Task;
        });
        var first = cache.GetSegmentAsync(1, TestContext.Current.CancellationToken);
        var second = cache.GetSegmentAsync(1, TestContext.Current.CancellationToken);

        completion.SetResult([new(1, 1, 1, 25, 0xFFFFFF, "同一段弹幕")]);
        await Task.WhenAll(first, second);

        Assert.Equal(1, requests);
        Assert.Equal(await first, await second);
    }

    [Fact]
    public async Task GetSegmentAsync_FailedLoad_CanRetry()
    {
        var requests = 0;
        var cache = new DanmakuSegmentCache(42, 720, (_, _) => ++requests == 1
            ? Task.FromException<IReadOnlyList<DanmakuItem>>(new HttpRequestException("offline"))
            : Task.FromResult<IReadOnlyList<DanmakuItem>>([]));
        await Assert.ThrowsAsync<HttpRequestException>(() => cache.GetSegmentAsync(1, TestContext.Current.CancellationToken));

        Assert.Empty(await cache.GetSegmentAsync(1, TestContext.Current.CancellationToken));
        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task LoadWindowAsync_OverlappingSegments_DeduplicatesAndSortsMessages()
    {
        var earlier = new DanmakuItem(1, 359, 1, 25, 0xFFFFFF, "早");
        var later = new DanmakuItem(2, 361, 1, 25, 0xFFFFFF, "晚");
        var cache = new DanmakuSegmentCache(42, 720, (_, _) =>
            Task.FromResult<IReadOnlyList<DanmakuItem>>([later, earlier]));

        var result = await cache.LoadWindowAsync(360, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { earlier, later }, result);
    }
}
