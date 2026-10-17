using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using VideoDesk.App.Tests.Support;

namespace VideoDesk.App.Tests;

[Collection("Native player")]
public sealed class PlaybackTimelineTests
{
    [Fact]
    public Task ClickHeldAcrossPlaybackUpdates_CommitsSelectedPositionOnce() => PlayerTestWindow.Run(async () =>
    {
        using var view = new TimelineWindow(); var timeline = view.Timeline;
        var requests = new List<double>(); timeline.SeekRequested += requests.Add;
        Down(timeline); Assert.True(timeline.IsScrubbing);
        var target = timeline.Value;
        Assert.Equal(timeline.Maximum, target);
        for (var i = 0; i < 5; i++)
        {
            timeline.SetPlaybackPosition(i);
            await Dispatcher.Yield(DispatcherPriority.Background);
        }
        Assert.Equal(target, timeline.Value); Assert.Empty(requests);
        Up(timeline);
        Assert.Equal(target, Assert.Single(requests)); Assert.False(timeline.IsScrubbing);
        Assert.False(timeline.IsMouseCaptured);
        timeline.SetPlaybackPosition(3); Assert.Equal(3, timeline.Value);
    });

    [Fact]
    public Task CaptureLost_CommitsOnceEvenIfMouseUpArrivesLater() => PlayerTestWindow.Run(async () =>
    {
        using var view = new TimelineWindow(); var timeline = view.Timeline;
        var requests = new List<double>(); timeline.SeekRequested += requests.Add;
        Down(timeline); var target = timeline.Value;
        Mouse.Capture(null); await Dispatcher.Yield(DispatcherPriority.Background);
        Assert.False(timeline.IsScrubbing); Assert.Equal(target, Assert.Single(requests));
        Up(timeline); Assert.Single(requests);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task NewLoadOrDisabledControls_CancelOldGestureWithoutSeeking(bool disable) => PlayerTestWindow.Run(() =>
    {
        using var view = new TimelineWindow(); var timeline = view.Timeline;
        var requests = new List<double>(); timeline.SeekRequested += requests.Add;
        Down(timeline);
        if (disable) timeline.IsEnabled = false; else timeline.CancelScrub();
        timeline.Maximum = 200; timeline.SetPlaybackPosition(12); timeline.IsEnabled = true;
        Up(timeline);
        Assert.False(timeline.IsScrubbing); Assert.False(timeline.IsMouseCaptured);
        Assert.Equal(12, timeline.Value); Assert.Empty(requests);
        return Task.CompletedTask;
    });

    [Fact]
    public Task KeyboardNavigation_SeeksOnceAndIgnoresOtherKeys() => PlayerTestWindow.Run(() =>
    {
        using var view = new TimelineWindow(); var timeline = view.Timeline;
        var requests = new List<double>(); timeline.SeekRequested += requests.Add;
        timeline.Value = 10;
        Press(timeline, Key.Right); Press(timeline, Key.PageUp); Press(timeline, Key.Home); Press(timeline, Key.Enter);
        Assert.Equal(new double[] {15, 25, 0}, requests);
        Assert.False(timeline.IsScrubbing);
        return Task.CompletedTask;
    });

    internal static void Down(PlaybackTimeline timeline) => timeline.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
    internal static void Up(PlaybackTimeline timeline) => timeline.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
    private static void Press(PlaybackTimeline timeline, Key key) => timeline.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(timeline), Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });

    private sealed class TimelineWindow : IDisposable
    {
        public PlaybackTimeline Timeline { get; } = new() { Maximum = 100 };
        private readonly Window window;
        public TimelineWindow()
        {
            // Routed input uses the existing mouse position. The offscreen
            // window makes it fall past the right endpoint without moving it.
            window = new Window { Content = Timeline, Width = 400, Height = 80, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            window.Show(); window.UpdateLayout();
        }
        public void Dispose() { Timeline.CancelScrub(); window.Close(); }
    }
}
