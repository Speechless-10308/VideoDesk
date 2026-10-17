using System.Text;

namespace VideoDesk.Core.Tests;

public sealed class MpvMediaSourceTests
{
    [Fact]
    public void SingleStream_PreservesItsAddress()
    {
        var stream=new Core.PlayStream("https://example.test/video.mp4",null,80,[],"",10);
        Assert.Equal(stream.VideoUrl,Core.MpvMediaSource.Create(stream));
    }

    [Fact]
    public void Dash_QuotesDelimitersAndUnicodeByUtf8ByteLength()
    {
        const string video="https://example.test/视频,%;a.m4s",audio="https://example.test/a,b;m4s";
        var source=Core.MpvMediaSource.Create(new(video,audio,80,[],"",10));
        Assert.Contains($"%{Encoding.UTF8.GetByteCount(video)}%{video}",source);
        Assert.Contains($"!new_stream;!no_chapters;%{Encoding.UTF8.GetByteCount(audio)}%{audio}",source);
    }
}
