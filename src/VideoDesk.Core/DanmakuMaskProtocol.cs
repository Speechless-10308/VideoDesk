using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace VideoDesk.Core;

// Big endian index and zlib frame records follow the public official player mask decoder.
public static class DanmakuMaskProtocol
{
    public static int EntryCount(ReadOnlySpan<byte> header)
    {
        if(header.Length<16||!header[..4].SequenceEqual("MASK"u8)||header[8]!=2)throw new InvalidDataException("不支持的弹幕遮罩格式");
        var count=BinaryPrimitives.ReadInt32BigEndian(header[12..]);if(count is <1 or >10000)throw new InvalidDataException("遮罩索引长度无效");return count;
    }
    public static IReadOnlyList<DanmakuMaskIndex> ParseIndex(byte[] bytes,int count)
    {
        if(bytes.Length<count*16)throw new InvalidDataException("遮罩索引不完整");var result=new List<DanmakuMaskIndex>();
        for(int i=0;i<count;i++){var entry=bytes.AsSpan(i*16,16);var time=BinaryPrimitives.ReadInt64BigEndian(entry);var offset=BinaryPrimitives.ReadInt64BigEndian(entry[8..]);if(time<0||offset<16+count*16||result.Count>0&&(time<=result[^1].Time*1000||offset<=result[^1].Offset))throw new InvalidDataException("遮罩索引无效");result.Add(new(time/1000d,offset));}return result;
    }
    public static IReadOnlyList<DanmakuMaskFrame> ParseFrames(byte[] compressed)
    {
        using Stream stream=compressed.Length>=2&&compressed[0]==0x1f&&compressed[1]==0x8b?new GZipStream(new MemoryStream(compressed),CompressionMode.Decompress):new ZLibStream(new MemoryStream(compressed),CompressionMode.Decompress);using var output=new MemoryStream();var buffer=new byte[8192];int read;while((read=stream.Read(buffer))>0){if(output.Length+read>16*1048576)throw new InvalidDataException("遮罩片段过大");output.Write(buffer,0,read);}var bytes=output.ToArray();var frames=new List<DanmakuMaskFrame>();int cursor=0;
        while(cursor<bytes.Length){if(bytes.Length-cursor<12)throw new InvalidDataException("遮罩帧不完整");var length=BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(cursor));var ms=BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(cursor+8));if(length<0||length>1048576||length>bytes.Length-cursor-12)throw new InvalidDataException("遮罩帧长度无效");var encoded=Encoding.UTF8.GetString(bytes,cursor+12,length);const string prefix="data:image/svg+xml;base64,";if(!encoded.StartsWith(prefix,StringComparison.Ordinal))throw new InvalidDataException("遮罩帧不是 SVG");var svg=Encoding.UTF8.GetString(Convert.FromBase64String(encoded[prefix.Length..]));frames.Add(new(ms/1000d,svg));cursor+=12+length;if(frames.Count>5000)throw new InvalidDataException("遮罩片段帧数过多");}return frames;
    }
}
