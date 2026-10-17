using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VideoDesk.App.Tests.Support;

namespace VideoDesk.App.Tests;

[Collection("Native player")]
public sealed class ImageCacheTests
{
    [Fact]
    public Task Thumbnail_DoesNotEnlargeSmallSourceAndReusesDecodedBitmap() => PlayerTestWindow.Run(async () =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "VideoDesk.ImageTests-"+Guid.NewGuid().ToString("N"));
        try
        {
            using var cache = new ImageCache(directory, new ImageHandler(Encode(32,16)));
            var image = await cache.Get("https://image.invalid/small.png",640);
            Assert.NotNull(image); Assert.Equal(32,image.PixelWidth); Assert.Equal(16,image.PixelHeight);
            Assert.Same(image,await cache.Get("https://image.invalid/small.png",640));
        }
        finally { if(Directory.Exists(directory))Directory.Delete(directory,true); }
    });

    [Fact]
    public Task ViewerBitmap_ReleasesEncodedBufferAndRemainsReadable() => PlayerTestWindow.Run(() =>
    {
        var (picture,buffer) = DecodeUnreferencedBuffer();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(buffer.IsAlive);
        var pixels = new byte[picture.Bitmap.PixelWidth*picture.Bitmap.PixelHeight*4];
        picture.Bitmap.CopyPixels(pixels,picture.Bitmap.PixelWidth*4,0);
        Assert.True(picture.Bitmap.IsFrozen); Assert.Contains((byte)255,pixels);
        GC.KeepAlive(picture); return Task.CompletedTask;
    });

    [Fact]
    public Task TallOriginal_BoundsDecodedPixelsAndPreservesOriginalBytes() => PlayerTestWindow.Run(async () =>
    {
        var directory=Path.Combine(Path.GetTempPath(),"VideoDesk.ImageTests-"+Guid.NewGuid().ToString("N"));
        try
        {
            var bytes=Encode(512,20000);
            using var cache=new ImageCache(directory,new ImageHandler(bytes));
            var image=await cache.Get("https://image.invalid/long.png",1024);
            Assert.NotNull(image); Assert.InRange((long)image.PixelWidth*image.PixelHeight,1,8_388_608);
            Assert.Equal(512d/20000,image.PixelWidth/(double)image.PixelHeight,precision:3);
            Assert.Equal(bytes,await cache.GetOriginalBytes("https://image.invalid/long.png",CancellationToken.None));
        }
        finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
    });

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (ViewerPicture Picture,WeakReference Buffer) DecodeUnreferencedBuffer()
    {
        var bytes=Encode(32,16);var weak=new WeakReference(bytes);
        return (ImageCache.DecodeViewerPicture(bytes,CancellationToken.None),weak);
    }

    private static byte[] Encode(int width,int height)
    {
        var pixels=new byte[width*height*4];Array.Fill(pixels,(byte)255);
        var bitmap=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var memory=new MemoryStream();encoder.Save(memory);return memory.ToArray();
    }
    private sealed class ImageHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
            =>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)});
    }
}
