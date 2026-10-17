using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VideoDesk.Core;

public record VideoChapter(double Start,double End,string Title,string Image="");
public enum VideoInteractionKind { Vote,Rating }
public record VideoPollOption(int Id,string Text,long Count,bool HasSelfDefinition=false);
public record VideoInteraction(string CommandId,long Id,VideoInteractionKind Kind,double Start,double Duration,
    string Title,double X,double Y,long Count,IReadOnlyList<VideoPollOption> Options,int Selection=0,double Average=0,double SummaryDuration=6,int VoteType=0)
{
    public bool Contains(double seconds)=>seconds>=Start&&seconds<Start+Duration;
}

public static class VideoPlayerFeatures
{
    public static VideoChapter? ChapterAt(IReadOnlyList<VideoChapter> chapters,double seconds)
    {
        int low=0,high=chapters.Count-1;
        while(low<=high){int middle=low+(high-low)/2;if(chapters[middle].Start<=seconds)low=middle+1;else high=middle-1;}
        return high>=0&&seconds<chapters[high].End?chapters[high]:null;
    }

    public static IReadOnlyList<VideoInteraction> ParseInteractions(byte[] bytes)
    {
        if(bytes.Length>2*1024*1024)throw new InvalidDataException("互动弹幕数据过大");
        var reader=new ProtoReader(bytes);var items=new List<VideoInteraction>();
        while(reader.Remaining)
        {
            var tag=reader.Varint();
            if(tag==74){if(items.Count>=1024)break;if(ParseCommand(reader.Block()) is {} item)items.Add(item);}
            else reader.Skip((int)(tag&7));
        }
        return items.DistinctBy(i=>i.CommandId).OrderBy(i=>i.Start).ToArray();
    }

    private static VideoInteraction? ParseCommand(ReadOnlySpan<byte> bytes)
    {
        var reader=new ProtoReader(bytes);string command="",title="",extra="",idString="";ulong id=0,progress=0;
        while(reader.Remaining)
        {
            var tag=reader.Varint();switch(tag)
            {
                case 8:id=reader.Varint();break;
                case 34:command=Encoding.UTF8.GetString(reader.Block());break;
                case 42:title=Encoding.UTF8.GetString(reader.Block());break;
                case 48:progress=reader.Varint();break;
                case 74:extra=Encoding.UTF8.GetString(reader.Block());break;
                case 82:idString=Encoding.UTF8.GetString(reader.Block());break;
                default:reader.Skip((int)(tag&7));break;
            }
        }
        if(command is not ("#VOTE#" or "#GRADE#")||progress>604800000)return null;
        JsonNode? data;try{data=JsonNode.Parse(extra);}catch(JsonException){return null;}
        if(data is not JsonObject)return null;
        var kind=command=="#VOTE#"?VideoInteractionKind.Vote:VideoInteractionKind.Rating;
        var externalId=data.Num(kind==VideoInteractionKind.Vote?"vote_id":"grade_id");if(externalId<=0)return null;
        double duration=data.Real("duration")/1000d;if(!double.IsFinite(duration)||duration<=0)duration=5;
        double x=data["posX_2"]!=null?data.Real("posX_2")/100:data["posX"]!=null?data.Real("posX")/667:.75;
        double y=data["posY_2"]!=null?data.Real("posY_2")/100:data["posY"]!=null?data.Real("posY")/375:.25;
        var options=(data["options"] as JsonArray??[]).Where(o=>o is JsonObject&&o.Num("idx")>0)
            .Select(o=>new VideoPollOption((int)o.Num("idx"),o.Str("desc"),Math.Max(0,o.Num("cnt")),o?["has_self_def"]?.ToString() is "true" or "1")).DistinctBy(o=>o.Id).Take(10).ToArray();
        if(kind==VideoInteractionKind.Vote&&options.Length<2)return null;
        var selected=(int)data.Num(kind==VideoInteractionKind.Vote?"my_vote":"mid_score");
        if(kind==VideoInteractionKind.Vote&&!options.Any(o=>o.Id==selected)||kind==VideoInteractionKind.Rating&&(selected<0||selected>10))selected=0;
        var commandId=idString.Length>0?idString:id.ToString();if(!ulong.TryParse(commandId,out var parsedId)||parsedId==0)return null;
        var average=data.Real("avg_score");if(!double.IsFinite(average))average=0;
        var summary=data["summary_duration"]==null?6:data.Real("summary_duration")/1000;if(!double.IsFinite(summary))summary=6;
        return new(commandId,externalId,kind,progress/1000d,Math.Clamp(duration,.5,120),
            data.Str(kind==VideoInteractionKind.Vote?"question":"msg",title),double.IsFinite(x)?Math.Clamp(x,0,1):.75,double.IsFinite(y)?Math.Clamp(y,0,1):.25,
            Math.Max(0,data.Num(kind==VideoInteractionKind.Vote?"cnt":"count")),options,selected,Math.Clamp(average,0,10),Math.Clamp(summary,.5,30),(int)data.Num("vote_type"));
    }

    private ref struct ProtoReader(ReadOnlySpan<byte> bytes)
    {
        private readonly ReadOnlySpan<byte> bytes=bytes;
        private int offset;
        internal bool Remaining=>offset<bytes.Length;
        internal ulong Varint()
        {
            ulong value=0;for(int shift=0;shift<64;shift+=7){if(!Remaining)throw new InvalidDataException("互动弹幕数据不完整");byte b=bytes[offset++];if(shift==63&&b>1)throw new InvalidDataException("无效的互动弹幕整数");value|=(ulong)(b&127)<<shift;if((b&128)==0)return value;}
            throw new InvalidDataException("无效的互动弹幕整数");
        }
        internal ReadOnlySpan<byte> Block(){ulong size=Varint();if(size>(ulong)(bytes.Length-offset))throw new InvalidDataException("互动弹幕长度无效");var block=bytes.Slice(offset,(int)size);offset+=(int)size;return block;}
        internal void Skip(int wire)
        {
            if(wire==0){Varint();return;}if(wire==2){Block();return;}int length=wire switch{1=>8,5=>4,_=>throw new InvalidDataException("互动弹幕编码无效")};
            if(length>bytes.Length-offset)throw new InvalidDataException("互动弹幕数据不完整");offset+=length;
        }
    }
}
