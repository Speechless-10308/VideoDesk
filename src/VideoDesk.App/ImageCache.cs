using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;
using VideoDesk.Core;

namespace VideoDesk.App;

internal sealed class ImageCache : IDisposable
{
    private readonly string directory;
    public long MaxDiskBytes {get;set;}=192*1048576;
    private int trimming,generation;
    public ImageCache(string directory,HttpMessageHandler? handler=null){this.directory=Directory.CreateDirectory(directory).FullName;http=handler==null?new():new(handler);http.Timeout=TimeSpan.FromSeconds(15);bitmaps=new(lifetime.Token);files=new(lifetime.Token);http.DefaultRequestHeaders.UserAgent.ParseAdd(VideoDesk.Core.BilibiliClient.UserAgent);http.DefaultRequestHeaders.Referrer=new Uri("https://www.bilibili.com/");TrimDisk();}
    private readonly HttpClient http;
    private readonly CancellationTokenSource lifetime=new();
    private readonly SharedImageRequests<(string Url,int Width),BitmapSource?> bitmaps;
    private readonly SharedImageRequests<string,byte[]?> files;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string,long> touches=new();
    private long nextTrim,addedSinceTrim;
    private readonly WeightedLruCache<(string Url,int Width),BitmapSource> images=new(64,64*1048576L);
    private readonly object gate=new();
    private readonly SemaphoreSlim limit=new(4,4);
    private readonly SemaphoreSlim storyboardSlots=new(2,2);
    public async Task<BitmapSource?> Get(string url,int width=360,CancellationToken ct=default,bool storyboard=false)
    {
        if(string.IsNullOrWhiteSpace(url))return null;
        ct.ThrowIfCancellationRequested();var key=(url,width);
        lock(gate)if(images.TryGet(key,out var existing)){return existing;}
        try{return await bitmaps.Get(key,token=>DecodeImage(url,width,token,storyboard?storyboardSlots:limit),ct).ConfigureAwait(false);}
        catch(OperationCanceledException){if(ct.IsCancellationRequested)throw;return null;}
        catch(HttpRequestException){return null;}catch(NotSupportedException){return null;}
    }
    internal Task<BitmapSource?> GetThumbnail(string url,int edge,CancellationToken ct)
        => Get(url,-Math.Clamp(edge,32,512),ct);
    private async Task<BitmapSource?> DecodeImage(string url,int width,CancellationToken ct,SemaphoreSlim slots)
    {
        var key=(url,width);var requestGeneration=generation;
        await slots.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock(gate)if(images.TryGet(key,out var existing)){return existing;}
            var file=CachePath(url);var bytes=await files.Get(url,token=>ReadBytes(url,requestGeneration,token),ct).ConfigureAwait(false);
            if(bytes==null)return null;
            async Task<BitmapImage> Decode(byte[] data)=>await Task.Run(()=>
            {
                ct.ThrowIfCancellationRequested();using var memory=new ImageDecodeStream(data);
                var decoder=BitmapDecoder.Create(memory,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None);var frame=decoder.Frames[0];
                var scale=width<0?Math.Min(1,-(double)width/Math.Max(frame.PixelWidth,frame.PixelHeight)):Math.Min(1,(double)width/frame.PixelWidth);
                // Long originals must not bypass the memory budget merely
                // because the requested display width is small.
                scale=Math.Min(scale,Math.Sqrt(8_388_608d/((long)frame.PixelWidth*frame.PixelHeight)));
                return DecodeBitmap(memory,frame.PixelWidth,frame.PixelHeight,scale,ct);
            },ct).ConfigureAwait(false);
            BitmapImage bitmap;
            try{bitmap=await Decode(bytes);}catch(Exception e) when(e is NotSupportedException or IOException or System.Runtime.InteropServices.COMException)
            {
                try{File.Delete(file);}catch(IOException){}
                var fresh=await files.Get(url,token=>ReadBytes(url,requestGeneration,token),ct).ConfigureAwait(false);
                if(fresh==null)return null;bitmap=await Decode(fresh);
            }
            ct.ThrowIfCancellationRequested();
            lock(gate)
            {
                var stride=(((long)bitmap.PixelWidth*Math.Max(32,bitmap.Format.BitsPerPixel)+31)/32)*4;
                if(requestGeneration==generation)images.Put(key,bitmap,stride*bitmap.PixelHeight);
            }
            return bitmap;
        }
        finally{slots.Release();}
    }
    private string CachePath(string url)=>Path.Combine(directory,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url)))+".img");
    internal Task<byte[]?> GetOriginalBytes(string url,CancellationToken ct)
        => files.Get(url,token=>ReadBytes(url,generation,token),ct);

    internal async Task<ViewerPicture?> GetViewerPicture(string url,CancellationToken ct)
    {
        await limit.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var bytes=await GetOriginalBytes(url,ct).ConfigureAwait(false);
            if(bytes==null)return null;
            try{return await Task.Run(()=>DecodeViewerPicture(bytes,ct),ct).ConfigureAwait(false);}
            catch(Exception error) when(error is NotSupportedException or IOException or System.Runtime.InteropServices.COMException)
            {
                try{File.Delete(CachePath(url));}catch(IOException){}
                var fresh=await GetOriginalBytes(url,ct).ConfigureAwait(false);if(fresh==null)return null;
                return await Task.Run(()=>DecodeViewerPicture(fresh,ct),ct).ConfigureAwait(false);
            }
        }
        finally{limit.Release();}
    }
    internal static ViewerPicture DecodeViewerPicture(byte[] bytes,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();using var memory=new ImageDecodeStream(bytes);
        var decoder=BitmapDecoder.Create(memory,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None);
        var frame=decoder.Frames[0];int width=frame.PixelWidth,height=frame.PixelHeight;
        // Bound decoded memory even for extremely long screenshots. Full bytes
        // remain on disk and downloads always preserve the original encoding.
        var scale=Math.Min(1,Math.Sqrt(16_777_216d/((long)width*height)));
        var bitmap=DecodeBitmap(memory,width,height,scale,ct);
        ct.ThrowIfCancellationRequested();return new(bitmap,width,height);
    }
    private static BitmapImage DecodeBitmap(Stream stream,int width,int height,double scale,CancellationToken ct)
    {
        stream.Position=0;var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth=Math.Max(1,(int)(width*scale));bitmap.DecodePixelHeight=Math.Max(1,(int)(height*scale));
        bitmap.StreamSource=stream;bitmap.EndInit();ct.ThrowIfCancellationRequested();bitmap.Freeze();return bitmap;
    }
    private async Task<byte[]?> ReadBytes(string url,int requestGeneration,CancellationToken ct)
    {
        var file=CachePath(url);
        try
        {
            if(File.Exists(file))
            {
                var bytes=await File.ReadAllBytesAsync(file,ct).ConfigureAwait(false);
                var now=DateTime.UtcNow.Ticks;
                if(!touches.TryGetValue(file,out var touched)||now-touched>TimeSpan.FromMinutes(5).Ticks){if(touches.Count>=2048)touches.Clear();touches[file]=now;File.SetLastWriteTimeUtc(file,DateTime.UtcNow);}
                return bytes;
            }
        }catch(IOException){}
        var data=await http.GetByteArrayAsync(url,ct).ConfigureAwait(false);
        var temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            if(generation==requestGeneration){await File.WriteAllBytesAsync(temporary,data,ct).ConfigureAwait(false);ct.ThrowIfCancellationRequested();if(generation==requestGeneration)File.Move(temporary,file,true);TrimDisk(Interlocked.Add(ref addedSinceTrim,data.Length)>=16*1048576);}
        }
        catch(IOException){}
        finally{try{if(File.Exists(temporary))File.Delete(temporary);}catch(IOException){}}
        return data;
    }
    public void TrimDisk(bool force=false)
    {
        var now=DateTime.UtcNow.Ticks;if(!force&&now<Interlocked.Read(ref nextTrim)||Interlocked.Exchange(ref trimming,1)!=0)return;
        Interlocked.Exchange(ref nextTrim,now+TimeSpan.FromSeconds(15).Ticks);Interlocked.Exchange(ref addedSinceTrim,0);
        _=Task.Run(()=>{try{var files=new DirectoryInfo(directory).EnumerateFiles("*.img").OrderByDescending(f=>f.LastWriteTimeUtc).ToArray();long bytes=0;foreach(var file in files){bytes+=file.Length;if(bytes>MaxDiskBytes||DateTime.UtcNow-file.LastWriteTimeUtc>TimeSpan.FromDays(30))try{file.Delete();}catch(IOException){}}}catch(IOException){}finally{Interlocked.Exchange(ref trimming,0);}});
    }
    public async Task Clear(){Interlocked.Increment(ref generation);bitmaps.CancelAll();files.CancelAll();lock(gate)images.Clear();touches.Clear();await Task.Run(()=>{foreach(var file in Directory.EnumerateFiles(directory,"*.img"))try{File.Delete(file);}catch(IOException){}});}
    public void Dispose(){lifetime.Cancel();bitmaps.CancelAll();files.CancelAll();http.Dispose();lifetime.Dispose();}
}
