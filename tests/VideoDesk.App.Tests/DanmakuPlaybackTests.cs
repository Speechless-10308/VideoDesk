using System.Diagnostics;
using System.Windows;
using VideoDesk.App.Tests.Support;
using VideoDesk.Core;

namespace VideoDesk.App.Tests;

[Collection("Native player")]
public sealed class DanmakuPlaybackTests
{
    [Fact]
    public Task EarlyMessages_WaitForVideoFrameAndDiscardEarlierSeek() => PlayerTestWindow.Run(async () =>
    {
        var waiting=false;var position=10d;var checks=0;
        using var renderer=new DanmakuSurface(()=>(position,true,1),new DanmakuPreferences{PersonMask=false},()=>{checks++;return waiting;});
        var window=new Window{Content=renderer,Width=640,Height=240,Left=-10000,Top=-10000,ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None};
        try
        {
            renderer.SetMessages([Message(1,10)]);window.Show();await Until(()=>renderer.ActiveIds.Contains(1));
            waiting=true;renderer.Wake();Assert.Empty(renderer.ActiveIds);
            position=100;renderer.SetWindowMessages([Message(2,100)]);
            position=200;renderer.SetWindowMessages([Message(3,200)]);
            var before=checks;await Until(()=>checks>=before+2);Assert.Empty(renderer.ActiveIds);
            waiting=false;renderer.Wake();await Until(()=>renderer.ActiveIds.Contains(3));
            Assert.DoesNotContain(1,renderer.ActiveIds);Assert.DoesNotContain(2,renderer.ActiveIds);
        }
        finally{window.Close();}
    });

    [Fact]
    public Task RealSeekWithStalledVideo_DoesNotRenderEarlyDanmaku() => PlayerTestWindow.Run(async () =>
    {
        await using var server=new FaultVideoServer();await using var view=new PlayerTestWindow();var host=view.Player;
        using var renderer=new DanmakuSurface(()=>(host.Number("time-pos"),host.Text("pause")=="yes"||host.Text("paused-for-cache")=="yes",host.Number("speed")),new DanmakuPreferences{PersonMask=false},()=>!host.CanInteract||host.Seeking);
        view.AddDanmaku(renderer);host.StateChanged+=name=>{if(name=="seeking")renderer.Wake();};
        view.Load(server.Url);await view.Until(()=>host.CanInteract);
        server.Fault=NetworkFault.StallBody;host.Command("seek","20","absolute+exact");renderer.SetWindowMessages([Message(20,20)]);
        await view.Until(()=>host.Presentation==PlayerPresentation.Buffering);Assert.True(host.Seeking);Assert.Empty(renderer.ActiveIds);
        server.Fault=NetworkFault.None;await view.Until(()=>!host.Seeking&&host.Number("time-pos")>=19.9);
        await Until(()=>renderer.ActiveIds.Contains(20));Assert.Equal(1,host.LoadRevision);
    });

    private static DanmakuItem Message(long id,double time)=>new(id,time,1,25,0xFFFFFF,"跳转后的弹幕");
    private static async Task Until(Func<bool> condition)
    {
        var watch=Stopwatch.StartNew();while(!condition()){if(watch.Elapsed>TimeSpan.FromSeconds(8))throw new TimeoutException("弹幕播放状态未在期限内恢复");await Task.Delay(30,TestContext.Current.CancellationToken);}
    }
}
