using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VideoDesk.Core;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private CancellationTokenSource? cardPreviewCancellation;
    private CardVideoPreview? cardPreview;
    private FrameworkElement? cardPreviewAnchor;
    private bool PreviewContainsPointer(FrameworkElement cover)=>CardVideoPreview.ContainsPointer(cover);
    private void StopCardPreview()
    {
        cardPreviewCancellation?.Cancel();cardPreviewCancellation?.Dispose();cardPreviewCancellation=null;
        var previous=cardPreview;cardPreview=null;cardPreviewAnchor=null;previous?.Dispose();
    }
    private void AttachCardPreview(Button tile,Grid cover,VideoCard video)
    {
        ToolTipService.SetIsEnabled(tile,false);
        var later=LaterButton(video,compact:true);later.HorizontalAlignment=HorizontalAlignment.Right;later.VerticalAlignment=VerticalAlignment.Top;later.Margin=new(6);later.Visibility=Visibility.Collapsed;cover.Children.Add(later);
        tile.MouseEnter+=(_,_)=>{later.Visibility=Visibility.Visible;};tile.MouseLeave+=(_,_)=>{if(!tile.IsKeyboardFocusWithin)later.Visibility=Visibility.Collapsed;};
        tile.GotKeyboardFocus+=(_,_)=>later.Visibility=Visibility.Visible;tile.LostKeyboardFocus+=(_,_)=>{if(!tile.IsMouseOver)later.Visibility=Visibility.Collapsed;};
        cover.MouseEnter+=async(_,_)=>await StartCardPreview(tile,cover,video);
        cover.MouseLeave+=(_,_)=>{if(cardPreviewAnchor==cover&&!PreviewContainsPointer(cover))StopCardPreview();};
        tile.Unloaded+=(_,_)=>{if(cardPreviewAnchor==cover)StopCardPreview();};tile.IsVisibleChanged+=(_,_)=>{if(!tile.IsVisible&&cardPreviewAnchor==cover)StopCardPreview();};
    }
    private async Task StartCardPreview(Button tile,Grid cover,VideoCard video)
        {
            if(cardPreviewAnchor==cover)return;StopCardPreview();cardPreviewAnchor=cover;
            var session=CancellationTokenSource.CreateLinkedTokenSource(navigation.Token);cardPreviewCancellation=session;var ct=session.Token;
            try
            {
                await Task.Delay(650,ct);if(!PreviewContainsPointer(cover)||!IsActive||MouseBusy())return;

                var data=await api.GetDetail(video.Bvid,ct);var part=data.Parts.FirstOrDefault();if(part==null)return;
                var stream=await api.GetPlayStream(video.Bvid,part.Cid,16,ct);await MpvHost.WaitForShutdownCapacityAsync(ct);ct.ThrowIfCancellationRequested();
                if(!PreviewContainsPointer(cover)||!IsActive||MouseBusy())return;
                cardPreview=new(this,cover,preferences.HardwareDecoding,preferences.Danmaku,LaterButton(video,compact:true),()=>_ = Run(()=>OpenVideo(video.Bvid)),()=>{if(cardPreviewAnchor==cover)StopCardPreview();},()=>PreviewContainsPointer(cover));

                cardPreview.Load(stream);_=LoadPreviewDanmaku(cardPreview,part.Cid,ct);
            }
            catch(OperationCanceledException){}
            catch(Exception error) when(error is BilibiliException or System.Net.Http.HttpRequestException or System.IO.IOException or InvalidOperationException or TimeoutException){if(cardPreviewAnchor==cover)StopCardPreview();}
            finally{if(!ct.IsCancellationRequested&&cardPreview==null&&cardPreviewAnchor==cover)StopCardPreview();}
        bool MouseBusy()=>System.Windows.Input.Mouse.LeftButton==System.Windows.Input.MouseButtonState.Pressed||tile.ContextMenu?.IsOpen==true||searchPopup.IsOpen;
    }
    private async Task LoadPreviewDanmaku(CardVideoPreview preview,long cid,CancellationToken ct)
    {
        try{var messages=await api.GetDanmakuSegment(cid,1,ct);ct.ThrowIfCancellationRequested();if(ReferenceEquals(cardPreview,preview))preview.SetDanmaku(messages);}
        catch(OperationCanceledException){}catch(Exception error) when(error is System.Net.Http.HttpRequestException or BilibiliException or System.IO.IOException or TimeoutException){}
    }
}
