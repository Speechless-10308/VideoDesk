using System.Text;
using System.Text.Json.Nodes;
using VideoDesk.Core;

namespace VideoDesk.Core.Tests;

public sealed class VideoPlayerFeaturesTests
{
    [Fact]
    public void ParseChapters_ChapterTypeOnly_SortsAndDropsInvalidRanges()
    {
        var response=JsonNode.Parse("""
        {"data":{"view_points":[
          {"type":2,"from":52,"to":203,"content":"外观展示","imgUrl":"http://i0.hdslb.com/test.jpg"},
          {"type":1,"from":10,"to":20,"content":"高能点"},
          {"type":2,"from":0,"to":52,"content":"引言"},
          {"type":2,"from":203,"to":200,"content":"无效"}]}}
        """)!;
        var result=BilibiliClient.ParseVideoChapters(response);
        Assert.Equal(2,result.Count);Assert.Equal("引言",result[0].Title);Assert.Equal("外观展示",VideoPlayerFeatures.ChapterAt(result,52)!.Title);
        Assert.Equal("https://i0.hdslb.com/test.jpg",result[1].Image);Assert.Null(VideoPlayerFeatures.ChapterAt(result,-1));Assert.Null(VideoPlayerFeatures.ChapterAt(result,203));
    }

    [Fact]
    public void ParseVote_PreservesCommandIdOptionsAndVideoCoordinates()
    {
        var packet=Packet("#VOTE#","""
        {"vote_id":12,"question":"你更喜欢哪个？","cnt":100,"options":[{"idx":1,"desc":"小主机","cnt":40},{"idx":2,"desc":"笔记本","cnt":60}],"my_vote":2,"posX_2":81,"posY_2":29,"duration":7000}
        """,6600);
        var item=Assert.Single(VideoPlayerFeatures.ParseInteractions(packet));
        Assert.Equal("2223545217716752384",item.CommandId);Assert.Equal(VideoInteractionKind.Vote,item.Kind);Assert.Equal(12,item.Id);
        Assert.Equal(6.6,item.Start);Assert.Equal(7,item.Duration);Assert.Equal(.81,item.X);Assert.Equal(.29,item.Y);
        Assert.Equal(2,item.Selection);Assert.Equal("笔记本",item.Options[1].Text);Assert.Equal(0,item.VoteType);Assert.False(item.Options[1].HasSelfDefinition);
        Assert.False(item.Contains(6.59));Assert.True(item.Contains(6.6));Assert.False(item.Contains(13.6));
    }

    [Fact]
    public void ParseVote_PreservesVoteTypeAndCustomOptionFlag()
    {
        var item=Assert.Single(VideoPlayerFeatures.ParseInteractions(Packet("#VOTE#","""
        {"vote_id":12,"vote_type":1,"options":[{"idx":1,"desc":"A","has_self_def":false},{"idx":2,"desc":"其他","has_self_def":true}]}
        """,0)));
        Assert.Equal(1,item.VoteType);Assert.False(item.Options[0].HasSelfDefinition);Assert.True(item.Options[1].HasSelfDefinition);
    }

    [Fact]
    public void ParseRating_PreservesScoreAndSummaryDuration()
    {
        var item=Assert.Single(VideoPlayerFeatures.ParseInteractions(Packet("#GRADE#","""
        {"grade_id":42,"msg":"外观打分？","mid_score":8,"count":387,"avg_score":9.7,"duration":5000,"summary_duration":6000,"posX":333.5,"posY":187.5}
        """,86300)));
        Assert.Equal(VideoInteractionKind.Rating,item.Kind);Assert.Equal(8,item.Selection);Assert.Equal(9.7,item.Average);Assert.Equal(387,item.Count);
        Assert.Equal(.5,item.X);Assert.Equal(.5,item.Y);Assert.Equal(6,item.SummaryDuration);
    }

    [Theory]
    [InlineData("#UNKNOWN#","not JSON")]
    [InlineData("#VOTE#","{")]
    [InlineData("#VOTE#","{\"vote_id\":1,\"options\":{}}")]
    [InlineData("#GRADE#","{\"grade_id\":0}")]
    public void ParseInteractions_UnsupportedOrIncompleteCommand_IsIgnored(string command,string extra)
        =>Assert.Empty(VideoPlayerFeatures.ParseInteractions(Packet(command,extra,0)));

    [Fact]
    public void ParseInteractions_TruncatedPayload_IsRejected()
    {
        var packet=Packet("#GRADE#","{\"grade_id\":1}",1000);
        Assert.Throws<InvalidDataException>(()=>VideoPlayerFeatures.ParseInteractions(packet[..^1]));
    }

    [Fact]
    public void ParseStoryboardIndex_UnsignedBigEndian_IncludesSentinel()
        =>Assert.Equal(new double[]{0,0,5,10,300},BilibiliClient.ParseStoryboardIndex([0,0,0,0,0,5,0,10,1,44]));

    [Theory]
    [InlineData(new byte[]{0,0,0})]
    [InlineData(new byte[]{0,10,0,5})]
    public void ParseStoryboardIndex_BadLengthOrOrder_IsRejected(byte[] bytes)
        =>Assert.Throws<InvalidDataException>(()=>BilibiliClient.ParseStoryboardIndex(bytes));

    private static byte[] Packet(string command,string extra,ulong progress)
    {
        using var inner=new MemoryStream();Write(inner,1,2223545217716752384);Write(inner,4,command);Write(inner,5,"标题");Write(inner,6,progress);Write(inner,9,extra);
        using var outer=new MemoryStream();Varint(outer,74);Varint(outer,(ulong)inner.Length);inner.Position=0;inner.CopyTo(outer);return outer.ToArray();
    }
    private static void Write(Stream stream,int field,ulong value){Varint(stream,(ulong)(field*8));Varint(stream,value);}
    private static void Write(Stream stream,int field,string value){var bytes=Encoding.UTF8.GetBytes(value);Varint(stream,(ulong)(field*8+2));Varint(stream,(ulong)bytes.Length);stream.Write(bytes);}
    private static void Varint(Stream stream,ulong value){while(value>=128){stream.WriteByte((byte)(value|128));value>>=7;}stream.WriteByte((byte)value);}
}
