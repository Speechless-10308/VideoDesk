namespace VideoDesk.Core;

// A native player can be initialized while no media is ready to receive seeks.
public sealed class PlaybackLoadState
{
    public long Revision { get; private set; }
    public bool Loading { get; private set; }
    public bool Loaded { get; private set; }
    public bool Playable { get; private set; }
    public bool Paused { get; private set; }
    public bool Seeking { get; private set; }
    public bool HasPendingSeek => pendingSeek.HasValue;
    public double? SeekTarget => pendingSeek ?? (Seeking ? start : null);
    private long? expectedEntry, activeEntry;
    private double start;
    private double? pendingSeek;

    public void Begin(double position = 0)
    {
        Revision++;
        Loading = true;
        Loaded = Playable = Seeking = Paused = false;
        expectedEntry = activeEntry = null;
        start = Math.Max(0, position);
        pendingSeek = null;
    }

    public void Expect(long entry) => expectedEntry = entry;
    public bool IsCurrent(long entry) => expectedEntry == entry;

    public void Started(long entry)
    {
        if (!IsCurrent(entry)) return;
        activeEntry = entry;
        Loaded = Playable = false;
    }

    public bool FileLoaded()
    {
        if (!Loading || expectedEntry == null || activeEntry != expectedEntry) return false;
        Loading = false;
        Loaded = true;
        return true;
    }

    public bool Ended(long entry)
    {
        if (!IsCurrent(entry)) return false;
        Failed();
        return true;
    }

    public void Failed()
    {
        Loading = Loaded = Playable = Seeking = false;
        pendingSeek = null;
    }

    public void SetPaused(bool paused) => Paused = paused;

    public bool PlaybackRestarted()
    {
        if (!Loaded || activeEntry != expectedEntry) return false;
        Playable = true;
        Seeking = false;
        return true;
    }

    public bool Seek(double seconds, bool absolute, double position = 0)
    {
        if (!double.IsFinite(seconds) || !Loading && !Loaded) return false;
        var origin = pendingSeek ?? (Playable && !Seeking ? position : start);
        pendingSeek = Math.Max(0, absolute ? seconds : origin + seconds);
        return true;
    }

    public double? TakeSeek()
    {
        if (!Playable) return null;
        var value = pendingSeek;
        pendingSeek = null;
        if (value.HasValue)
        {
            Seeking = true;
            start = value.Value;
        }
        return value;
    }

    public void SeekFailed() => Seeking = false;
}
