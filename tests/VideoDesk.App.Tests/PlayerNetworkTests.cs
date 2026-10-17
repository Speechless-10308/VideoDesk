using VideoDesk.App.Tests.Support;

namespace VideoDesk.App.Tests;

[CollectionDefinition("Native player", DisableParallelization = true)]
public sealed class NativePlayerCollection;

[Collection("Native player")]
public sealed class PlayerNetworkTests
{
    [Fact]
    public Task TimelineClickAcrossPollTicks_ReachesTargetOnFirstRelease() => PlayerTestWindow.Run(async () =>
    {
        await using var server = new FaultVideoServer(); await using var view = new PlayerTestWindow();
        view.Load(server.Url); await view.Until(() => view.Player.CanInteract);
        var requests = 0; var polls = 0;
        view.Timeline.SeekRequested += position => { requests++; view.Player.Command("seek", position.ToString(System.Globalization.CultureInfo.InvariantCulture), "absolute+exact"); };
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) => { polls++; if (!view.Player.Seeking) view.Timeline.SetPlaybackPosition(view.Player.Number("time-pos")); };
        try
        {
            PlaybackTimelineTests.Down(view.Timeline); timer.Start();
            await view.Until(() => polls >= 5);
            Assert.Equal(20, view.Timeline.Value);
            PlaybackTimelineTests.Up(view.Timeline);
            await view.Until(() => !view.Player.Seeking && view.Player.Number("time-pos") >= 19.5);
            Assert.Equal(1, requests); Assert.Equal(1, view.Player.LoadRevision);
            Assert.InRange(view.Player.Number("time-pos"), 19.5, 22);
        }
        finally { timer.Stop(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RepeatedForwardAndBackwardSeeks_KeepLatestTargetAndPauseIntent(bool paused) => PlayerTestWindow.Run(async () =>
    {
        await using var server = new FaultVideoServer(); await using var view = new PlayerTestWindow();
        view.Load(server.Url); await view.Until(() => view.Player.CanInteract);
        view.Player.Set("pause", paused ? "yes" : "no");
        foreach (var target in new[] {20, 5, 30, 8, 25})
        {
            view.Player.Command("seek", target.ToString(), "absolute+exact");
            await view.Until(() => !view.Player.Seeking && Math.Abs(view.Player.Number("time-pos") - target) < .75);
        }
        for (var i = 1; i <= 100; i++) view.Player.Command("seek", (i % 30).ToString(), "absolute+exact");
        await view.Until(() => !view.Player.Seeking && Math.Abs(view.Player.Number("time-pos") - 10) < .75);
        Assert.Equal(paused ? "yes" : "no", view.Player.Text("pause")); Assert.Equal(1, view.Player.LoadRevision);
    });

    [Fact]
    public Task DelayedHeaders_BlockControlsUntilFirstFrame() => PlayerTestWindow.Run(async () =>
    {
        await using var server = new FaultVideoServer { Fault = NetworkFault.DelayHeaders };
        await using var view = new PlayerTestWindow(); view.Load(server.Url);
        await view.Until(() => server.Requests > 0);
        Assert.False(view.Player.CanInteract); Assert.Equal(PlayerPresentation.Loading, view.Player.Presentation);
        view.Player.Command("seek", "20", "absolute+exact");
        server.Fault = NetworkFault.None; await view.Until(() => view.Player.CanInteract);
        Assert.InRange(view.Player.Number("time-pos"), 0, 5);
    });

    [Fact]
    public Task ShortMidstreamStall_ShowsBufferingThenResumes() => PlayerTestWindow.Run(async () =>
    {
        await using var server = new FaultVideoServer(); await using var view = new PlayerTestWindow();
        view.Load(server.Url); await view.Until(() => view.Player.CanInteract);
        var revision = view.Player.LoadRevision; server.Fault = NetworkFault.StallBody;
        await view.Until(() => view.Player.Presentation == PlayerPresentation.Buffering);
        var position = view.Player.Number("time-pos"); server.Fault = NetworkFault.None;
        await view.Until(() => view.Player.Presentation == PlayerPresentation.Ready && view.Player.Number("time-pos") > position + .25);
        Assert.Equal(revision, view.Player.LoadRevision);
    });

    [Fact]
    public Task PermanentMidstreamStall_FailsAfterBoundedRetryAndCanBeRetriedManually() => PlayerTestWindow.Run(async () =>
    {
        await using var server = new FaultVideoServer(); await using var view = new PlayerTestWindow();
        view.Load(server.Url); await view.Until(() => view.Player.CanInteract && view.Player.Number("time-pos") >= 1);
        var revision = view.Player.LoadRevision; var ended = 0; view.Player.PlaybackEnded += _ => ended++;
        server.Fault = NetworkFault.StallBody;
        await view.Until(() => view.Player.Presentation == PlayerPresentation.Failed, seconds: 45);
        Assert.False(view.Player.CanInteract); Assert.Equal(0, ended); Assert.Equal(revision + 1, view.Player.LoadRevision);
        Assert.Contains(PlayerPresentation.Buffering, view.States);
        var position = view.Player.ResumePosition; Assert.True(position >= 1);
        server.Fault = NetworkFault.None; view.Load(server.Url, position);
        await view.Until(() => view.Player.CanInteract);
        Assert.True(view.Player.Number("time-pos") >= position - .5);
    });

    [Fact]
    public Task ConnectionClosedMidstream_DoesNotCountAsNormalVideoCompletion() => PlayerTestWindow.Run(async () =>
    {
        await using var server = new FaultVideoServer(); await using var view = new PlayerTestWindow();
        view.Load(server.Url); await view.Until(() => view.Player.CanInteract && view.Player.Number("time-pos") >= 1);
        var position = view.Player.Number("time-pos"); var ended = 0; view.Player.PlaybackEnded += _ => ended++;
        server.Fault = NetworkFault.DropOnce;
        await view.Until(() => server.DroppedConnections == 1 && view.Player.CanInteract && view.Player.Number("time-pos") > position + 1, seconds: 25);
        Assert.Equal(0, ended);
    });

    [Fact]
    public Task SeekWhileNetworkStalls_ResumesAtRequestedPosition() => PlayerTestWindow.Run(async () =>
    {
        await using var server = new FaultVideoServer(); await using var view = new PlayerTestWindow();
        view.Load(server.Url); await view.Until(() => view.Player.CanInteract);
        server.Fault = NetworkFault.StallBody; view.Player.Command("seek", "20", "absolute+exact");
        await view.Until(() => view.Player.Presentation == PlayerPresentation.Buffering);
        server.Fault = NetworkFault.None;
        await view.Until(() => view.Player.Presentation == PlayerPresentation.Ready && view.Player.Number("time-pos") >= 19.5);
    });

    [Fact]
    public Task UnavailableSource_StopsRetryingAndReportsFailure() => PlayerTestWindow.Run(async () =>
    {
        await using var server = new FaultVideoServer { Fault = NetworkFault.Reject };
        await using var view = new PlayerTestWindow(); view.Load(server.Url);
        await view.Until(() => view.Player.Presentation == PlayerPresentation.Failed, seconds: 35);
        Assert.False(view.Player.CanInteract); Assert.Equal(2, view.Player.LoadRevision);
    });

    [Fact]
    public Task ReplacingDelayedLoad_IgnoresOldEvents() => PlayerTestWindow.Run(async () =>
    {
        await using var server = new FaultVideoServer { Fault = NetworkFault.DelayHeaders };
        await using var view = new PlayerTestWindow(); view.Load(server.Url);
        await view.Until(() => server.Requests > 0);
        view.Load(server.Url + "?replacement"); server.Fault = NetworkFault.None;
        await view.Until(() => view.Player.CanInteract);
        Assert.Equal(2, view.Player.LoadRevision); Assert.Equal(PlayerPresentation.Ready, view.Player.Presentation);
    });

    [Fact]
    public Task NoInitialResponse_EventuallyOffersRetryInsteadOfAnEndlessSpinner() => PlayerTestWindow.Run(async () =>
    {
        await using var server = new FaultVideoServer { Fault = NetworkFault.DelayHeaders };
        await using var view = new PlayerTestWindow(); view.Load(server.Url);
        await view.Until(() => view.Player.Presentation == PlayerPresentation.Failed, seconds: 35);
        Assert.False(view.Player.CanInteract); Assert.Equal(2, view.Player.LoadRevision);
    });

    [Fact]
    public Task LowBandwidth_ReportsBufferingAndResumesWhenBandwidthReturns() => PlayerTestWindow.Run(async () =>
    {
        await using var server = new FaultVideoServer(); await using var view = new PlayerTestWindow();
        view.Load(server.Url); await view.Until(() => view.Player.CanInteract);
        server.ChunkDelayMilliseconds = 500;
        await view.Until(() => view.Player.Presentation == PlayerPresentation.Buffering);
        var position = view.Player.Number("time-pos"); server.ChunkDelayMilliseconds = 50;
        await view.Until(() => view.Player.Presentation == PlayerPresentation.Ready && view.Player.Number("time-pos") > position + .25);
    });

    [Fact]
    public Task DashAudioAndVideo_LoadAndSeekAsOneMedia() => PlayerTestWindow.Run(async () =>
    {
        await using var server = new FaultVideoServer(); await using var view = new PlayerTestWindow();
        view.Player.Load(new Core.PlayStream(server.Url, server.AudioUrl, 16, [], "rawvideo", 40));
        await view.Until(() => view.Player.CanInteract);
        Assert.Equal(8000, view.Player.Number("audio-params/samplerate"));
        view.Player.Command("seek", "20", "absolute+exact");
        await view.Until(() => view.Player.Number("time-pos") >= 19.5);
        Assert.Equal(8000, view.Player.Number("audio-params/samplerate"));
    });

    [Fact]
    public Task NormalCompletion_ReusesHostAndAutomaticallyPlaysNextFile() => PlayerTestWindow.Run(async () =>
    {
        await using var server = new FaultVideoServer(); await using var view = new PlayerTestWindow();
        view.Load(server.Url); await view.Until(() => view.Player.CanInteract);
        var completions = 0;
        view.Player.PlaybackEnded += revision =>
        {
            if (++completions == 1) _ = view.Player.Dispatcher.BeginInvoke(() => view.Load(server.Url + "?next"));
        };
        view.Player.Command("seek", "39", "absolute+exact");
        await view.Until(() => view.Player.LoadRevision == 2 && view.Player.CanInteract && view.Player.Number("time-pos") > .2);
        Assert.Equal(1, completions); Assert.Equal("no", view.Player.Text("pause"));
    });

    [Fact]
    public Task UserReplacement_TakesPrecedenceOverQueuedAutomaticRecovery() => PlayerTestWindow.Run(async () =>
    {
        await using var failed = new FaultVideoServer { Fault = NetworkFault.Reject };
        await using var working = new FaultVideoServer(); await using var view = new PlayerTestWindow();
        var replaced = false;
        view.Player.PresentationChanged += () =>
        {
            if (!replaced && failed.Requests > 0 && view.Player.Presentation == PlayerPresentation.Loading)
            {
                replaced = true; view.Load(working.Url);
            }
        };
        view.Load(failed.Url); await view.Until(() => view.Player.CanInteract);
        Assert.True(replaced); Assert.Equal(2, view.Player.LoadRevision);
        Assert.True(view.Player.Number("time-pos") < 5);
    });
}
