using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using VideoDesk.Core;

namespace VideoDesk.App;

// Metadata and a small sprite working set live with the video, rather than with
// a single mouse hover. Leaving the timeline never poisons an in-flight load.
internal sealed class StoryboardPreview : IDisposable
{
    private readonly PlaybackTimeline timeline;
    private readonly PlayerOverlay overlay;
    private readonly BilibiliClient api;
    private readonly ImageCache images;
    private readonly string bvid;
    private readonly long cid;
    private readonly CancellationTokenSource lifetime=new();
    private readonly Dictionary<int,BitmapSource> sprites=[];
    private Task<VideoStoryboard?>? metadata;
    private Task? loading;
    private Task? retry;
    private VideoStoryboard? storyboard;
    private IReadOnlyList<VideoChapter> chapters=[];
    private bool hovering,disposed;
    private double seconds,x;
    private int displayed=-1;
    private DateTime retryAfter;
    private BitmapSource? bitmap;

    public StoryboardPreview(PlaybackTimeline timeline,PlayerOverlay overlay,BilibiliClient api,ImageCache images,string bvid,long cid)
    {
        this.timeline=timeline;this.overlay=overlay;this.api=api;this.images=images;this.bvid=bvid;this.cid=cid;
        timeline.MouseMove+=Move;timeline.MouseLeave+=Leave;timeline.Unloaded+=Leave;timeline.IsVisibleChanged+=Visible;
        StartLoading();
    }
    internal void SetChapters(IReadOnlyList<VideoChapter> value){chapters=value;if(hovering)Show();}
    private void Move(object sender,MouseEventArgs e)=>PreviewAt(e.GetPosition(timeline).X);
    internal void PreviewAt(double position)
    {
        if(disposed)return;
        x=Math.Clamp(position,7,Math.Max(7,timeline.ActualWidth-7));
        seconds=Math.Clamp((x-7)/Math.Max(1,timeline.ActualWidth-14),0,1)*timeline.Maximum;hovering=true;
        timeline.SetHoverPosition(x);Show();StartLoading();
    }
    private void Show()
    {
        if(!hovering||disposed)return;
        if(storyboard is {} shots)
        {
            var frame=shots.FrameAt(seconds);
            if(frame!=displayed)
            {
                bitmap=sprites.TryGetValue(frame/(shots.Columns*shots.Rows),out var sprite)?Crop(shots,frame,sprite):null;
                displayed=bitmap==null?-1:frame;
            }
        }
        overlay.ShowTimelinePreview(timeline,x,seconds,bitmap,VideoPlayerFeatures.ChapterAt(chapters,seconds)?.Title);
    }
    private void StartLoading()
    {
        if(disposed||loading is {IsCompleted:false})return;
        if(DateTime.UtcNow<retryAfter){if(hovering&&retry is not {IsCompleted:false})retry=Retry();return;}
        if(storyboard is {} shots&&sprites.ContainsKey(shots.FrameAt(seconds)/(shots.Columns*shots.Rows)))return;
        loading=Load();
    }
    private async Task Retry()
    {
        try{await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1,(retryAfter-DateTime.UtcNow).TotalMilliseconds+10)),lifetime.Token);if(hovering)StartLoading();}catch(OperationCanceledException){}
    }
    private async Task Load()
    {
        var ct=lifetime.Token;
        try
        {
            metadata??=api.GetVideoStoryboard(bvid,cid,ct);
            storyboard=await metadata;
            if(storyboard is not {} shots){metadata=null;retryAfter=DateTime.UtcNow.AddSeconds(15);return;}
            while(!ct.IsCancellationRequested)
            {
                var frame=shots.FrameAt(seconds);var page=frame/(shots.Columns*shots.Rows);
                if(!sprites.ContainsKey(page))
                {
                    string original=shots.Images[page];string thumbnail=original;
                    if(Uri.TryCreate(original,UriKind.Absolute,out var uri)&&uri.Host.EndsWith(".hdslb.com",StringComparison.OrdinalIgnoreCase)&&!uri.AbsolutePath.Contains('@'))
                        thumbnail=original+"@"+(shots.Columns*160)+"w.jpg";
                    var sprite=await images.Get(thumbnail,shots.Columns*160,ct,storyboard:true);
                    if((sprite==null||Crop(shots,frame,sprite)==null)&&thumbnail!=original)sprite=await images.Get(original,shots.Columns*160,ct,storyboard:true);
                    ct.ThrowIfCancellationRequested();
                    if(sprite==null||Crop(shots,frame,sprite)==null)throw new IOException("预览图片暂未加载");
                    sprites[page]=sprite;
                    while(sprites.Count>2)sprites.Remove(sprites.Keys.Where(p=>p!=page).OrderByDescending(p=>Math.Abs(p-page)).First());
                }
                if(!hovering)return;
                var currentPage=shots.FrameAt(seconds)/(shots.Columns*shots.Rows);
                if(currentPage!=page)continue;
                displayed=-1;Show();return;
            }
        }
        catch(OperationCanceledException){}
        catch(Exception error) when(error is HttpRequestException or BilibiliException or IOException or NotSupportedException or TimeoutException)
        {
            if(storyboard==null)metadata=null;
            retryAfter=DateTime.UtcNow.AddMilliseconds(800);
            if(hovering)retry=Retry();
        }
    }
    internal static BitmapSource? Crop(VideoStoryboard shots,int frame,BitmapSource sprite)
    {
        int width=sprite.PixelWidth/shots.Columns,height=(int)Math.Round((double)shots.TileHeight*width/shots.TileWidth),local=frame%(shots.Columns*shots.Rows);
        if(width<=0||height<=0)return null;
        var rect=new Int32Rect(local%shots.Columns*width,local/shots.Columns*height,width,height);
        if(rect.X+rect.Width>sprite.PixelWidth||rect.Y+rect.Height>sprite.PixelHeight)return null;
        var crop=new CroppedBitmap(sprite,rect);crop.Freeze();return crop;
    }
    private void Visible(object sender,DependencyPropertyChangedEventArgs e){if(!timeline.IsVisible)Stop();}
    private void Leave(object? sender,RoutedEventArgs e)=>Stop();
    private void Stop(){hovering=false;bitmap=null;displayed=-1;timeline.SetHoverPosition(null);overlay.HideTimelinePreview();}
    public void Dispose()
    {
        if(disposed)return;disposed=true;Stop();lifetime.Cancel();lifetime.Dispose();sprites.Clear();
        timeline.MouseMove-=Move;timeline.MouseLeave-=Leave;timeline.Unloaded-=Leave;timeline.IsVisibleChanged-=Visible;
    }
}
