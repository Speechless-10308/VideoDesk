using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VideoDesk.Core;

namespace VideoDesk.App.Tests.Support;

internal sealed class PlayerTestWindow : IAsyncDisposable
{
    private readonly Window window;
    public MpvHost Player { get; } = new(hardware: false);
    public List<PlayerPresentation> States { get; } = [];
    public PlaybackTimeline Timeline { get; } = new() { Maximum = 20 };

    public PlayerTestWindow()
    {
        var content = new DockPanel(); DockPanel.SetDock(Timeline, Dock.Bottom); content.Children.Add(Timeline); content.Children.Add(Player);
        window = new Window { Width = 160, Height = 160, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Content = content };
        Player.PresentationChanged += () => States.Add(Player.Presentation);
        window.Show();
        Player.Set("mute", "yes");
    }

    public void Load(string url, double start = 0) => Player.Load(new PlayStream(url, null, 16, [], "rawvideo", 40), start);
    public void AddDanmaku(DanmakuSurface renderer)
    {
        renderer.Height=120;DockPanel.SetDock(renderer,Dock.Bottom);((DockPanel)window.Content).Children.Insert(0,renderer);window.Width=640;window.Height=480;
    }

    public async Task Until(Func<bool> condition, int seconds = 15)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(seconds))
                throw new TimeoutException($"Player: {Player.Presentation}; position={Player.Number("time-pos")}; ready={Player.MediaReady}; error={Player.LastNativeError}");
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        window.Content = null; Player.Dispose(); await Player.ShutdownCompleted; window.Close();
    }

    public static async Task Run(Func<Task> action)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            _ = dispatcher.BeginInvoke(async () =>
            {
                try { await action(); completed.TrySetResult(); }
                catch (Exception error) { completed.TrySetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        await completed.Task;
    }
}
