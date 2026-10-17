using VideoDesk.Core;

namespace VideoDesk.Core.Tests;

public sealed class PlaybackHealthTests
{
    [Fact]
    public void InitialSilence_RetriesOnceThenFailsInsteadOfWaitingForever()
    {
        var health=new PlaybackHealth();health.Start(TimeSpan.Zero,0);
        Assert.Equal(PlaybackHealthAction.None,health.Observe(TimeSpan.FromSeconds(5),0,false,false,false,false));
        Assert.Equal(PlaybackHealthAction.Retry,health.Observe(TimeSpan.FromSeconds(12),0,false,false,false,false));
        health.Start(TimeSpan.FromSeconds(12),0,newMedia:false);
        Assert.Equal(PlaybackHealthAction.Fail,health.Observe(TimeSpan.FromSeconds(30),0,false,false,false,false));
        Assert.Equal(1,health.Retries);
    }

    [Fact]
    public void MidPlaybackShortStall_ReportsBufferingThenRecoversWithProgress()
    {
        var health=new PlaybackHealth();health.Start(TimeSpan.Zero,10);
        Assert.Equal(PlaybackHealthAction.Buffering,health.Observe(TimeSpan.FromSeconds(2),10,true,false,false,true));
        Assert.Equal(PlaybackHealthAction.None,health.Observe(TimeSpan.FromSeconds(4),11,true,false,false,false));
        Assert.Equal(0,health.Retries);
    }

    [Fact]
    public void MidPlaybackPermanentStall_RetryHasIndependentDeadline()
    {
        var health=new PlaybackHealth();health.Start(TimeSpan.Zero,20);
        Assert.Equal(PlaybackHealthAction.Retry,health.Observe(TimeSpan.FromSeconds(12),20,true,false,false,true));
        health.Start(TimeSpan.FromSeconds(12),20,newMedia:false);
        Assert.Equal(PlaybackHealthAction.Fail,health.Observe(TimeSpan.FromSeconds(30),20,false,false,false,true));
    }

    [Fact]
    public void DeliberatePauseAndNormalEnd_DoNotTriggerNetworkRecovery()
    {
        var health=new PlaybackHealth();health.Start(TimeSpan.Zero,20);
        Assert.Equal(PlaybackHealthAction.None,health.Observe(TimeSpan.FromHours(1),20,true,true,false,false));
        Assert.Equal(PlaybackHealthAction.None,health.Observe(TimeSpan.FromHours(2),20,true,false,true,false));
        Assert.Equal(0,health.Retries);
    }

    [Fact]
    public void SilentDecoderStall_IsDetectedEvenWithoutNativeBufferingFlag()
    {
        var health=new PlaybackHealth();health.Start(TimeSpan.Zero,20);
        Assert.Equal(PlaybackHealthAction.Buffering,health.Observe(TimeSpan.FromSeconds(2),20,true,false,false,false));
        Assert.Equal(PlaybackHealthAction.Retry,health.Observe(TimeSpan.FromSeconds(12),20,true,false,false,false));
    }

    [Fact]
    public void ExplicitNewLoad_ResetsTheRecoveryBudget()
    {
        var health=new PlaybackHealth();health.Start(TimeSpan.Zero,0);Assert.True(health.TryRetry());Assert.False(health.TryRetry());
        health.Start(TimeSpan.FromSeconds(1),0);Assert.True(health.TryRetry());
    }
}
