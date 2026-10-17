using VideoDesk.Core;
using VideoDesk.Core.Tests.Support;

namespace VideoDesk.Core.Tests;

public sealed class BilibiliClientTests
{
    private static BilibiliClient Client(TemporaryDirectory directory,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, bool loggedIn = false)
    {
        var store = new LocalStore(directory.Path);
        if (loggedIn)
            store.SaveSession("SESSDATA=unit-test-session; Domain=.bilibili.com; Path=/,bili_jct=unit-test-csrf; Domain=.bilibili.com; Path=/");
        return new(new CaptureStore(Path.Combine(directory.Path, "captures"), enabled: false), store,
            _ => new StubHttpHandler(respond));
    }

    [Fact]
    public async Task GetVideoStoryboard_EmptyJsonIndex_LoadsBinaryIndexAndCachesSuccess()
    {
        using var directory=new TemporaryDirectory();int calls=0;
        using var client=Client(directory,(request,_)=>
        {
            calls++;
            if(request.RequestUri!.AbsolutePath=="/index.bin")
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new ByteArrayContent([0,0,0,0,0,5,0,10])});
            return Task.FromResult(StubHttpHandler.Json("""
            {"code":0,"data":{"pvdata":"https://example.test/index.bin","img_x_len":2,"img_y_len":2,"img_x_size":160,"img_y_size":90,"image":["https://example.test/sprite.jpg"],"index":[]}}
            """));
        });
        var first=await client.GetVideoStoryboard("BVtest",99,TestContext.Current.CancellationToken);
        Assert.NotNull(first);Assert.Equal(new double[]{0,5,10},first.Times);
        Assert.Same(first,await client.GetVideoStoryboard("BVtest",99,TestContext.Current.CancellationToken));Assert.Equal(2,calls);
    }

    [Fact]
    public async Task GetVideoStoryboard_TransientFailure_AllowsNextRequest()
    {
        using var directory=new TemporaryDirectory();int calls=0;
        using var client=Client(directory,(_,_)=>
        {
            if(++calls==1)throw new HttpRequestException("temporary");
            return Task.FromResult(StubHttpHandler.Json("""
            {"code":0,"data":{"img_x_len":1,"img_y_len":1,"img_x_size":160,"img_y_size":90,"image":["https://example.test/sprite.jpg"],"index":[0,0]}}
            """));
        });
        await Assert.ThrowsAsync<HttpRequestException>(()=>client.GetVideoStoryboard("BVtest",99,TestContext.Current.CancellationToken));
        Assert.NotNull(await client.GetVideoStoryboard("BVtest",99,TestContext.Current.CancellationToken));Assert.Equal(2,calls);
    }

    [Theory]
    [InlineData(VideoInteractionKind.Vote,2,"vote","option_id")]
    [InlineData(VideoInteractionKind.Rating,8,"grade","grade_score")]
    public async Task SubmitInteraction_EncodedRequest_ContainsChoiceCommandAndCsrf(VideoInteractionKind kind,int selection,string endpoint,string choiceKey)
    {
        using var directory=new TemporaryDirectory();int calls=0;
        using var client=Client(directory,async(request,ct)=>
        {
            calls++;Assert.Equal(HttpMethod.Post,request.Method);Assert.Equal("/x/v2/dm/command/"+endpoint+"/post",request.RequestUri!.AbsolutePath);
            var form=await StubHttpHandler.ReadForm(request,ct);Assert.Equal(selection.ToString(),form[choiceKey]);Assert.Equal("1234567890123456789",form["cmd_id_str"]);
            Assert.Equal("1200",form["progress"]);Assert.Equal("unit-test-csrf",form["csrf"]);Assert.Equal("42",form[kind==VideoInteractionKind.Vote?"vote_id":"grade_id"]);
            if(kind==VideoInteractionKind.Vote){Assert.Equal("0",form["vote_type"]);Assert.Equal("0",form["has_self_def"]);}
            return StubHttpHandler.Json("{\"code\":0,\"data\":{\"visible\":true}}");
        },loggedIn:true);
        var item=new VideoInteraction("1234567890123456789",42,kind,0,5,"test",.5,.5,0,[new(1,"A",0),new(2,"B",0)]);
        await client.SubmitVideoInteraction(7,99,item,selection,1.2,TestContext.Current.CancellationToken);Assert.Equal(1,calls);
    }

    [Fact]
    public async Task SubmitVote_CustomOption_UsesServerTypeAndNumericFlag()
    {
        using var directory=new TemporaryDirectory();
        using var client=Client(directory,async(request,ct)=>
        {
            var form=await StubHttpHandler.ReadForm(request,ct);
            Assert.Equal("1",form["vote_type"]);Assert.Equal("1",form["has_self_def"]);Assert.Equal("2",form["option_id"]);
            return StubHttpHandler.Json("{\"code\":0}");
        },loggedIn:true);
        var item=new VideoInteraction("123",42,VideoInteractionKind.Vote,0,5,"test",.5,.5,0,[new(1,"A",0),new(2,"其他",0,true)],VoteType:1);
        await client.SubmitVideoInteraction(7,99,item,2,1,TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SubmitRating_InvalidScore_DoesNotSendRequest()
    {
        using var directory=new TemporaryDirectory();using var client=Client(directory,(_,_)=>throw new InvalidOperationException("Unexpected HTTP"),loggedIn:true);
        var item=new VideoInteraction("123",42,VideoInteractionKind.Rating,0,5,"test",.5,.5,0,[]);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>client.SubmitVideoInteraction(7,99,item,5,1,TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetVideoStoryboard_Request_UsesVideoRefererAndTimestampIndex()
    {
        using var directory = new TemporaryDirectory();
        using var client = Client(directory, (request, _) =>
        {
            Assert.Equal("/x/player/videoshot", request.RequestUri!.AbsolutePath);
            Assert.Contains("cid=99", request.RequestUri.Query);
            Assert.Contains("index=1", request.RequestUri.Query);
            Assert.Equal("https://www.bilibili.com/video/BVtest", request.Headers.Referrer!.AbsoluteUri);
            return Task.FromResult(StubHttpHandler.Json("{\"code\":-404,\"message\":\"not found\"}"));
        });

        Assert.Null(await client.GetVideoStoryboard("BVtest", 99, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetVideoStoryboard_ServerError_DoesNotBecomeAnEmptySuccess()
    {
        using var directory = new TemporaryDirectory();
        using var client = Client(directory, (_, _) =>
            Task.FromResult(StubHttpHandler.Json("{\"code\":-403,\"message\":\"forbidden\"}")));

        var error = await Assert.ThrowsAsync<BilibiliException>(() => client.GetVideoStoryboard("BVtest", 99, TestContext.Current.CancellationToken));

        Assert.Equal(-403, error.Code);
    }

    [Theory]
    [InlineData(42, "aid", "42")]
    [InlineData(0, "bvid", "BVtest")]
    public async Task AddWatchLater_VideoIdentifier_SendsEncodedFormAndCsrf(long aid, string key, string expected)
    {
        using var directory = new TemporaryDirectory();
        using var client = Client(directory, async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/x/v2/history/toview/add", request.RequestUri!.AbsolutePath);
            var form = await StubHttpHandler.ReadForm(request, ct);
            Assert.Equal(expected, form[key]);
            Assert.Equal("unit-test-csrf", form["csrf"]);
            return StubHttpHandler.Json("{\"code\":0}");
        }, loggedIn: true);

        await client.AddWatchLater("BVtest", aid, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RemoveWatchLater_InvalidId_RejectsBeforeTransport()
    {
        using var directory = new TemporaryDirectory();
        using var client = Client(directory, (_, _) => throw new InvalidOperationException("No request expected"), loggedIn: true);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.RemoveWatchLater(0, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetWatchLater_LoggedOut_RejectsBeforeTransport()
    {
        using var directory = new TemporaryDirectory();
        using var client = Client(directory, (_, _) => throw new InvalidOperationException("No request expected"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetWatchLater(ct: TestContext.Current.CancellationToken));

        Assert.Contains("登录", error.Message);
    }

    [Fact]
    public async Task SendDanmaku_AcceptedMessage_KeepsServerIdTimeAndText()
    {
        const string text = "测试 & + 汉字";
        using var directory = new TemporaryDirectory();
        using var client = Client(directory, async (request, ct) =>
        {
            Assert.Equal("/x/v2/dm/post", request.RequestUri!.AbsolutePath);
            var form = await StubHttpHandler.ReadForm(request, ct);
            Assert.Equal("12345", form["progress"]);
            Assert.Equal("99", form["oid"]);
            Assert.Equal(text, form["msg"]);
            Assert.Equal("unit-test-csrf", form["csrf"]);
            return StubHttpHandler.Json("{\"code\":0,\"data\":{\"dmid\":12345678901234567}}");
        }, loggedIn: true);

        var result = await client.SendDanmaku(42, 99, 12.345, text, TestContext.Current.CancellationToken);

        Assert.Equal(12345678901234567, result.Id);
        Assert.Equal(12.345, result.Time);
        Assert.Equal(text, result.Text);
    }

    [Fact]
    public async Task SendDanmaku_MissingReceipt_RejectsWithoutRetry()
    {
        var requests = 0;
        using var directory = new TemporaryDirectory();
        using var client = Client(directory, (_, _) =>
        {
            requests++;
            return Task.FromResult(StubHttpHandler.Json("{\"code\":0,\"data\":{}}"));
        }, loggedIn: true);

        await Assert.ThrowsAsync<InvalidDataException>(() => client.SendDanmaku(42, 99, 12, "测试", TestContext.Current.CancellationToken));

        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task AddCoins_AmbiguousTimeout_DoesNotRetryCharge()
    {
        var requests = 0;
        using var directory = new TemporaryDirectory();
        using var client = Client(directory, (_, _) =>
        {
            requests++;
            throw new TaskCanceledException("Simulated transport timeout");
        }, loggedIn: true);

        await Assert.ThrowsAsync<TimeoutException>(() => client.AddCoins("BVtest", 1, false, TestContext.Current.CancellationToken));

        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task SendComment_NativeTransport_PreservesReplyContextAndEncoding()
    {
        const string text = "测试 & + 汉字\n第二行";
        using var directory = new TemporaryDirectory();
        using var client = Client(directory, async (request, ct) =>
        {
            // Native session initialization is part of the public send operation.
            if (request.RequestUri!.AbsolutePath == "/x/frontend/finger/spi")
                return StubHttpHandler.Json("{\"code\":0,\"data\":{\"b_3\":\"test-buvid3\",\"b_4\":\"test-buvid4\"}}");
            if (request.RequestUri.AbsolutePath == "/x/internal/gaia-gateway/ExClimbWuzhi")
                return StubHttpHandler.Json("{\"code\":0}");
            Assert.Equal("/x/v2/reply/add", request.RequestUri!.AbsolutePath);
            var form = await StubHttpHandler.ReadForm(request, ct);
            Assert.Equal("42", form["oid"]);
            Assert.Equal("7", form["root"]);
            Assert.Equal("8", form["parent"]);
            Assert.Equal(text, form["message"]);
            return StubHttpHandler.Json("{\"code\":0,\"data\":{\"rpid\":123}}");
        }, loggedIn: true);

        var result = await client.SendComment(42, text, 7, 8, TestContext.Current.CancellationToken);

        Assert.Equal(123, result.Id);
        Assert.Equal(CommentPublication.Submitted, result.Publication);
    }
}
