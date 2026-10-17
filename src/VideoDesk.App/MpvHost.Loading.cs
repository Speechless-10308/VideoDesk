using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using VideoDesk.Core;

namespace VideoDesk.App;

internal enum PlayerPresentation { Loading, Ready, Buffering, Failed }

internal sealed partial class MpvHost
{
    private readonly DispatcherTimer startupTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
    private (PlayStream Stream, double Start)? currentLoad;
    private bool softwareFallback, hasFrame;
    private long? recoveryRevision;
    private bool Recovering => recoveryRevision == LoadRevision;
    private double resumePosition;
    private readonly PlaybackHealth health = new();
    private readonly Stopwatch healthClock = Stopwatch.StartNew();
    public PlayerPresentation Presentation { get; private set; } = PlayerPresentation.Loading;
    public string LoadingMessage { get; private set; } = "正在加载视频";
    public string LastNativeError { get; private set; } = "";
    public bool CanInteract => Ready && hasFrame && Presentation is PlayerPresentation.Ready or PlayerPresentation.Buffering;
    public event Action? PresentationChanged;

    private void SetPresentation(PlayerPresentation state, string message = "")
    {
        if (Presentation == state && LoadingMessage == message) return;
        Presentation = state; LoadingMessage = message; PresentationChanged?.Invoke();
    }

    private void CheckStartup()
    {
        if (!Ready || currentLoad == null || Recovering || Presentation == PlayerPresentation.Failed) return;
        if (!hasFrame && loadState.Playable && Number("video-out-params/h") > 0)
        {
            hasFrame = true;
        }
        if (hasFrame && MediaReady) resumePosition = ResumePosition;
        var buffering = Text("paused-for-cache") == "yes";
        var decision = health.Observe(healthClock.Elapsed, Number("time-pos"), hasFrame, hasFrame && loadState.Paused, hasFrame && Text("eof-reached") == "yes", buffering);
        if (decision == PlaybackHealthAction.Retry) RecoverOrFail(hasFrame ? "播放连接中断" : "视频加载超时", retryReserved: true);
        else if (decision == PlaybackHealthAction.Fail) FailPlayback(hasFrame ? "视频连接暂时无法恢复" : "视频加载超时");
        else if (hasFrame) SetPresentation(decision == PlaybackHealthAction.Buffering ? PlayerPresentation.Buffering : PlayerPresentation.Ready, decision == PlaybackHealthAction.Buffering ? "正在缓冲" : "");
    }

    private void RecoverOrFail(string reason, bool retryReserved = false)
    {
        if (Recovering || Presentation == PlayerPresentation.Failed) return;
        if (preview || currentLoad is not { } load || !retryReserved && !health.TryRetry())
        {
            FailPlayback(reason); return;
        }
        var revision = LoadRevision;
        recoveryRevision = revision;
        var position = hasFrame ? ResumePosition : load.Start;
        var paused = loadState.Paused;
        SetPresentation(PlayerPresentation.Loading, "正在重新加载视频");
        // Finish the current native event batch before replacing the file.
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (!Ready || LoadRevision != revision) { if (recoveryRevision == revision) recoveryRevision = null; return; }
            try
            {
                PrepareLoad(position); softwareFallback = hardware;
                loadState.SetPaused(paused); currentLoad = pendingLoad = (load.Stream, position);
                prepared = false; health.Start(healthClock.Elapsed, position, newMedia: false); LoadPending();
            }
            catch (Exception) { FailPlayback("视频暂时无法播放"); }
            finally { if (recoveryRevision == revision) recoveryRevision = null; }
        }, DispatcherPriority.Loaded);
    }

    public void FailPlayback(string reason)
    {
        resumePosition = ResumePosition;
        startupTimer.Stop(); CancelScheduledSeek(); hasFrame = false; loadState.Failed();
        SetPresentation(PlayerPresentation.Failed, reason);
        if (Ready) InvokeCommand(["stop"], false, out _);
        PlaybackFailed?.Invoke(reason);
    }

    private void HandleEnd()
    {
        var duration = currentLoad?.Stream.Duration ?? 0;
        if (!loadState.Seeking && !loadState.HasPendingSeek && duration > 3 && ResumePosition < duration - 2)
            RecoverOrFail("播放连接提前中断");
        else NotifyEnd();
    }

    private void ReadNativeLog(IntPtr data)
    {
        var log = Marshal.PtrToStructure<LogMessage>(data);
        var level = Marshal.PtrToStringUTF8(log.Level);
        var message = Marshal.PtrToStringUTF8(log.Text) ?? "";
        message = Regex.Replace(message, @"https?://[^\s""'<>]+", "[媒体地址]", RegexOptions.IgnoreCase).Trim();
        message = message.Length > 400 ? message[..400] : message;
        var prefix = Marshal.PtrToStringUTF8(log.Prefix) ?? "mpv";
        if (level is "error" or "fatal") LastNativeError = prefix + ": " + message;
        if (App.Diagnostics.Enabled)
        {
            var revision = LoadRevision;
            _ = App.Diagnostics.AppendAsync("mpv.log", () => $"{DateTimeOffset.UtcNow:O} {revision} {level} {prefix}: {message}{Environment.NewLine}");
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct LogMessage { public IntPtr Prefix, Level, Text; public int LogLevel; }
    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int mpv_request_log_messages(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string level);
}
