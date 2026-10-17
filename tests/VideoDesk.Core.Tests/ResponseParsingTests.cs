using System.Text.Json.Nodes;
using VideoDesk.Core;

namespace VideoDesk.Core.Tests;

public sealed class ResponseParsingTests
{
    [Fact]
    public void ParseWatchLater_CompletedAndUnavailableVideos_PreservesBothStatesAndPagination()
    {
        var response = JsonNode.Parse("""
            {"data":{"count":21,"list":[
              {"aid":1,"bvid":"BVtest","cid":99,"title":"测试","duration":120,"progress":-1},
              {"aid":2,"bvid":"","cid":0,"state":-2}
            ]}}
            """)!;

        var page = BilibiliClient.ParseWatchLater(response);

        Assert.Equal(-1, page.Items[0].Progress);
        Assert.False(page.Items[0].Unavailable);
        Assert.True(page.Items[1].Unavailable);
        Assert.True(page.HasMore);
        Assert.Equal(21, page.Total);
    }

    [Fact]
    public void ParseSearchResults_VerificationRequired_ThrowsInsteadOfShowingNoResults()
    {
        var response = JsonNode.Parse("""{"code":-352,"message":"需要验证","data":{"v_voucher":"test-voucher"}}""")!;

        var error = Assert.Throws<BilibiliException>(() => BilibiliClient.ParseSearchResults(response, 1));

        Assert.Equal(-352, error.Code);
    }

    [Fact]
    public void ParseComment_PictureAndSearchLink_KeepsDimensionsAndOfficialSource()
    {
        var response = JsonNode.Parse("""
            {"rpid":77,"content":{"message":"测试","pictures":[
              {"img_src":"http://i0.hdslb.com/test.png","img_width":1080,"img_height":1920}
            ],"jump_url":{"英雄联盟":{"title":"英雄联盟","pc_url":"//search.bilibili.com/all?keyword=英雄联盟&from_source=webcommentline_search"}}}}
            """)!;

        var comment = BilibiliClient.ParseComment(response);

        var picture = Assert.Single(comment.Pictures!);
        Assert.Equal(1080, picture.Width);
        Assert.Equal(1920, picture.Height);
        Assert.StartsWith("https://", picture.Url);
        Assert.Contains("from_source=webcommentline_search", comment.Links!["英雄联盟"].Url);
    }

    [Fact]
    public void ParseCommentSubmission_MissingReceipt_RejectsUnconfirmedSubmission()
    {
        Assert.Throws<InvalidDataException>(() => BilibiliClient.ParseCommentSubmission(JsonNode.Parse("{\"data\":{}}")!));
    }
}
