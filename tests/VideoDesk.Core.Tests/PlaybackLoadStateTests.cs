namespace VideoDesk.Core.Tests;

public sealed class PlaybackLoadStateTests
{
    [Fact]
    public void SeekDuringLoad_LastPositionWaitsForFirstFrameOfMatchingFile()
    {
        var state=new PlaybackLoadState();state.Begin();state.Expect(10);
        Assert.True(state.Seek(20,true));Assert.True(state.Seek(42,true));Assert.Null(state.TakeSeek());
        state.Started(10);Assert.True(state.FileLoaded());Assert.Null(state.TakeSeek());
        Assert.True(state.PlaybackRestarted());Assert.Equal(42,state.TakeSeek());Assert.Null(state.TakeSeek());
        Assert.True(state.Seek(60,true));
    }
    [Fact]
    public void ReplacedFile_LateEventsCannotReadyOrEndNewLoad()
    {
        var state=new PlaybackLoadState();state.Begin();state.Expect(10);state.Started(10);state.FileLoaded();
        state.Begin();state.Expect(11);state.Seek(30,true);state.Started(10);
        Assert.False(state.FileLoaded());Assert.False(state.Ended(10));Assert.True(state.Loading);
        state.Started(11);Assert.True(state.FileLoaded());state.PlaybackRestarted();Assert.Equal(30,state.TakeSeek());
    }
    [Fact]
    public void NewLoad_DiscardsPreviousPendingSeek()
    {
        var state=new PlaybackLoadState();state.Begin();state.Expect(10);state.Seek(90,true);
        state.Begin();state.Expect(11);state.Started(11);state.FileLoaded();Assert.Null(state.TakeSeek());Assert.Equal(2,state.Revision);
    }
    [Fact]
    public void RelativeSeekDuringLoad_AccumulatesFromRequestedStart()
    {
        var state=new PlaybackLoadState();state.Begin(10);state.Expect(2);state.Seek(5,false);state.Seek(-3,false);
        state.Started(2);state.FileLoaded();state.PlaybackRestarted();Assert.Equal(12,state.TakeSeek());
    }
    [Fact]
    public void FailedOrIdleFile_DoesNotAcceptOrReplaySeeks()
    {
        var state=new PlaybackLoadState();Assert.False(state.Seek(5,true));
        state.Begin();state.Expect(1);state.Seek(15,true);Assert.True(state.Ended(1));
        Assert.False(state.Seek(25,true));Assert.False(state.FileLoaded());Assert.Null(state.TakeSeek());
    }
    [Fact]
    public void PlaybackIntent_NewVideoPlaysAndExplicitPauseIsRetainedWhileLoading()
    {
        var state=new PlaybackLoadState();state.Begin();state.SetPaused(true);
        state.Expect(1);state.Started(1);state.FileLoaded();state.PlaybackRestarted();Assert.True(state.Paused);
        state.Begin();Assert.False(state.Paused);Assert.False(state.Playable);
        state.SetPaused(true);state.Expect(2);state.Started(2);state.FileLoaded();Assert.True(state.Paused);
        state.SetPaused(false);state.PlaybackRestarted();Assert.False(state.Paused);
    }
    [Fact]
    public void RapidSeeks_KeepLastTargetAndWaitForFirstDecodedFrame()
    {
        var state=new PlaybackLoadState();state.Begin();state.Expect(1);state.Started(1);state.FileLoaded();
        for(var i=1;i<=100;i++)state.Seek(i,true);
        Assert.Null(state.TakeSeek());Assert.True(state.HasPendingSeek);Assert.False(state.Playable);
        state.PlaybackRestarted();Assert.Equal(100,state.TakeSeek());Assert.True(state.Seeking);
        state.Seek(5,false,position:0);Assert.Equal(105,state.TakeSeek());
        state.PlaybackRestarted();Assert.False(state.Seeking);Assert.Null(state.TakeSeek());
    }
    [Fact]
    public void ReplacedFile_QueuedSeekAndRestartCannotAffectItsReplacement()
    {
        var state=new PlaybackLoadState();state.Begin();state.Expect(1);state.Started(1);state.FileLoaded();state.PlaybackRestarted();state.Seek(55,true);
        state.Begin();state.Expect(2);Assert.Null(state.TakeSeek());Assert.False(state.PlaybackRestarted());
        state.Started(1);Assert.False(state.FileLoaded());Assert.False(state.Ended(1));
        state.Started(2);state.FileLoaded();state.PlaybackRestarted();Assert.Null(state.TakeSeek());Assert.False(state.Seeking);
        state.Started(1);Assert.True(state.Loaded);Assert.True(state.Playable);
    }
    [Fact]
    public void SeekFailure_DoesNotLeavePlaybackWaitingForASeekToFinish()
    {
        var state=new PlaybackLoadState();state.Begin();state.Expect(1);state.Started(1);state.FileLoaded();state.PlaybackRestarted();
        state.Seek(20,true);state.TakeSeek();state.SeekFailed();Assert.False(state.Seeking);Assert.True(state.Playable);
        state.Seek(40,true);Assert.Equal(40,state.TakeSeek());
    }
    [Fact]
    public void LoadFailure_DiscardsPendingActionsAndAllowsANewLoad()
    {
        var state=new PlaybackLoadState();state.Begin();state.Seek(25,true);state.Failed();
        Assert.False(state.Loading);Assert.False(state.Loaded);Assert.Null(state.SeekTarget);Assert.False(state.Seek(30,true));
        state.Begin();state.Expect(2);state.Started(2);Assert.True(state.FileLoaded());Assert.True(state.PlaybackRestarted());
    }
}
