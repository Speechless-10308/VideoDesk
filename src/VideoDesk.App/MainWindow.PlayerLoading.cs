namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private void UpdatePlayerPresentation(MpvHost host)
    {
        if (!ReferenceEquals(player, host)) return;
        overlay?.SetPlaybackPresentation(host.Presentation, host.LoadingMessage, host.LastNativeError, () => RetryPlayback(host));
        if (seek != null) seek.IsEnabled = host.CanInteract;
    }

    private async Task RetryPlayback(MpvHost host)
    {
        if (!ReferenceEquals(player, host) || detail == null || part == null) return;
        CancelCollectionOpen();
        var request = BeginStreamOpen(navigation.Token); var ct = request.Token;
        var bvid = detail.Video.Bvid; var cid = part.Cid;
        var start = host.ResumePosition;
        host.PrepareLoad(start); var revision = host.LoadRevision;
        try
        {
            var stream = await api.GetPlayStream(bvid, cid, preferences.DefaultQuality, ct);
            ct.ThrowIfCancellationRequested();
            if (!ReferenceEquals(player, host) || host.LoadRevision != revision || part?.Cid != cid) return;
            PopulateQualities(stream); host.Load(stream, start); timer.Start();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception) { if (!ct.IsCancellationRequested && ReferenceEquals(player, host) && host.LoadRevision == revision) host.FailPlayback("视频暂时无法播放"); }
        finally { EndStreamOpen(request); }
    }
}
