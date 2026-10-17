namespace VideoDesk.Core;

public enum PlaybackHealthAction { None, Buffering, Retry, Fail }

// Uses a monotonic clock supplied by the player. Pausing is not a network
// failure; a retry gets its own deadline and cannot turn into an endless loop.
public sealed class PlaybackHealth
{
    private TimeSpan lastProgress;
    private double lastPosition;
    private bool waitingForRetry;
    public int Retries { get; private set; }

    public void Start(TimeSpan now, double position, bool newMedia = true)
    {
        lastProgress = now; lastPosition = position; waitingForRetry = false;
        if (newMedia) Retries = 0;
    }

    public bool TryRetry()
    {
        if (waitingForRetry || Retries >= 1) return false;
        Retries++; waitingForRetry = true; return true;
    }

    public PlaybackHealthAction Observe(TimeSpan now, double position, bool hasFrame, bool paused, bool ended, bool buffering)
    {
        if (waitingForRetry) return PlaybackHealthAction.None;
        if (paused || ended || hasFrame && Math.Abs(position - lastPosition) > .02)
        {
            lastProgress = now; lastPosition = position;
            return !paused && !ended && buffering ? PlaybackHealthAction.Buffering : PlaybackHealthAction.None;
        }
        var elapsed = now - lastProgress;
        if (elapsed >= TimeSpan.FromSeconds(Retries == 0 ? 12 : 18))
            return TryRetry() ? PlaybackHealthAction.Retry : PlaybackHealthAction.Fail;
        return buffering || hasFrame && elapsed >= TimeSpan.FromSeconds(1.5)
            ? PlaybackHealthAction.Buffering : PlaybackHealthAction.None;
    }
}
