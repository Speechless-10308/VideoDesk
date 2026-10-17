using VideoDesk.Core;
using VideoDesk.Core.Tests.Support;

namespace VideoDesk.Core.Tests;

[Trait("Category", "Storage")]
public sealed class LocalStoreTests
{
    [Fact]
    public async Task FlushProgressAsync_UpdatedProgress_SurvivesRestart()
    {
        using var directory = new TemporaryDirectory();
        var store = new LocalStore(directory.Path);
        store.SaveProgress(new("BVtest", 42, 10, 100, DateTimeOffset.UtcNow, "测试视频"));
        store.SaveProgress(new("BVtest", 42, 50, 100, DateTimeOffset.UtcNow, "测试视频"));

        await store.FlushProgressAsync();
        var restored = new LocalStore(directory.Path);
        restored.LoadProgress();

        Assert.Equal(50, restored.GetProgress("BVtest")!.Position);
    }

    [Fact]
    public async Task RemoveProgress_PendingSave_DoesNotResurrectDeletedRecord()
    {
        using var directory = new TemporaryDirectory();
        var store = new LocalStore(directory.Path);
        store.SaveProgress(new("BVtest", 42, 50, 100, DateTimeOffset.UtcNow, "测试视频"));

        store.RemoveProgress("BVtest");
        await store.FlushProgressAsync();
        var restored = new LocalStore(directory.Path);
        restored.LoadProgress();

        Assert.Empty(restored.History);
    }

    [Fact]
    public void Preferences_SaveAndReload_PreservesThemeAndDanmakuSettings()
    {
        using var directory = new TemporaryDirectory();
        var preferences = new ClientPreferences
        {
            Theme = "Dark",
            InterfaceScale = 1.1,
            Danmaku = new() { Opacity = .3, BlockedWords = "屏蔽词" }
        };

        preferences.Save(directory.Path);
        var restored = ClientPreferences.Load(directory.Path);

        Assert.Equal("Dark", restored.Theme);
        Assert.Equal(1.1, restored.InterfaceScale);
        Assert.Equal(.3, restored.Danmaku.Opacity);
        Assert.Equal("屏蔽词", restored.Danmaku.BlockedWords);
    }
}
