using VideoDesk.Core;

namespace VideoDesk.Core.Tests;

public sealed class PlaybackClockTests
{
    [Fact]
    public void Advance_PausedClock_HoldsPosition()
    {
        var clock = new PlaybackClock();
        clock.Synchronize(12, true, 1, 0);

        Assert.Equal(12, clock.Advance(10));
    }

    [Fact]
    public void Synchronize_Seek_JumpsToRequestedPosition()
    {
        var clock = new PlaybackClock();
        clock.Synchronize(12, false, 1, 0);

        clock.Synchronize(50, true, 1, 1);

        Assert.Equal(50, clock.Position);
        Assert.Equal(1, clock.Discontinuities);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1)]
    [InlineData(2)]
    public void Advance_PlaybackSpeed_MatchesElapsedTime(double speed)
    {
        var clock = new PlaybackClock();
        clock.Synchronize(10, false, speed, 0);

        Assert.Equal(10 + speed, clock.Advance(1), 6);
    }

    [Fact]
    public void Synchronize_QuantizedDecoderSamples_KeepsMotionContinuous()
    {
        var clock = new PlaybackClock();
        clock.Synchronize(0, false, 1, 0);
        double previous = 0;

        // 24 fps decoder samples observed by a 144 Hz renderer over ten seconds.
        for (var frame = 1; frame <= 1440; frame++)
        {
            var wall = frame / 144d;
            if (frame % 12 == 0)
                clock.Synchronize(Math.Floor(wall * 24) / 24, false, 1, wall);
            var position = clock.Advance(wall);
            Assert.InRange(position - previous, 0, 0.009);
            previous = position;
        }

        Assert.Equal(0, clock.Discontinuities);
        Assert.InRange(clock.Position, 9.92, 10.08);
    }
}
