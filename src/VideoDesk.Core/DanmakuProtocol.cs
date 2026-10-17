using System.Text;

namespace VideoDesk.Core;

public static class DanmakuProtocol
{
    public static IReadOnlyList<DanmakuItem> Parse(byte[] bytes)
    {
        if(bytes.Length>0&&bytes[0]=='{')throw new InvalidDataException("弹幕接口返回了错误响应");
        var reader=new Reader(bytes);var items=new List<DanmakuItem>();
        while(reader.Remaining){var tag=reader.Varint();if(tag==10)items.Add(ParseItem(reader.Block()));else reader.Skip((int)(tag&7));}
        return items.Where(i=>i.Text.Length>0&&i.Time>=0).ToArray();
    }
    private static DanmakuItem ParseItem(ReadOnlySpan<byte> bytes)
    {
        var r=new Reader(bytes);long id=0;double time=0;int mode=1,font=25,weight=0,pool=0;uint color=0xFFFFFF,attributes=0;string text="",sender="";
        while(r.Remaining){var tag=r.Varint();switch(tag){case 8:id=(long)r.Varint();break;case 16:time=r.Varint()/1000d;break;case 24:mode=(int)r.Varint();break;case 32:font=(int)r.Varint();break;case 40:color=(uint)r.Varint();break;case 50:sender=Encoding.UTF8.GetString(r.Block());break;case 58:text=Encoding.UTF8.GetString(r.Block());break;case 72:weight=(int)r.Varint();break;case 88:pool=(int)r.Varint();break;case 104:attributes=(uint)r.Varint();break;default:r.Skip((int)(tag&7));break;}}
        return new(id,time,mode,font,color,text,weight,pool,attributes,sender);
    }
    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> data=data;private int offset;
        public bool Remaining=>offset<data.Length;
        public ulong Varint(){ulong value=0;for(int shift=0;shift<64;shift+=7){if(!Remaining)throw new InvalidDataException("弹幕数据不完整");byte b=data[offset++];value|=(ulong)(b&127)<<shift;if((b&128)==0)return value;}throw new InvalidDataException("无效的弹幕整数");}
        public ReadOnlySpan<byte> Block(){var length=checked((int)Varint());if(length>data.Length-offset)throw new InvalidDataException("弹幕数据长度无效");var value=data.Slice(offset,length);offset+=length;return value;}
        public void Skip(int wire){switch(wire){case 0:Varint();return;case 2:Block();return;case 1:Advance(8);return;case 5:Advance(4);return;default:throw new InvalidDataException("未知的弹幕编码");}}
        private void Advance(int length){if(length>data.Length-offset)throw new InvalidDataException("弹幕数据不完整");offset+=length;}
    }
}
