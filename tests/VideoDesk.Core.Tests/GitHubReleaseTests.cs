using System.Net;
using VideoDesk.Core.Tests.Support;

namespace VideoDesk.Core.Tests;

public sealed class GitHubReleaseTests
{
    [Fact]
    public async Task GetLatest_NoPublishedRelease_ReturnsNull()
    {
        using var http=new HttpClient(new StubHttpHandler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))));
        Assert.Null(await GitHubRelease.GetLatest(http,TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetLatest_PublicRequest_ParsesVersionWithoutAccountCredentials()
    {
        using var http=new HttpClient(new StubHttpHandler((request,_)=>
        {
            Assert.Equal("https://api.github.com/repos/Speechless-10308/VideoDesk/releases/latest",request.RequestUri!.AbsoluteUri);
            Assert.NotEmpty(request.Headers.UserAgent);Assert.Null(request.Headers.Authorization);Assert.False(request.Headers.Contains("Cookie"));
            return Task.FromResult(StubHttpHandler.Json("""{"tag_name":"v0.1.2","html_url":"https://github.com/Speechless-10308/VideoDesk/releases/tag/v0.1.2","draft":false,"prerelease":false}"""));
        }));
        var release=await GitHubRelease.GetLatest(http,TestContext.Current.CancellationToken);
        Assert.NotNull(release);Assert.True(release.IsNewerThan(new Version(0,1,1,0)));Assert.False(release.IsNewerThan(new Version(0,1,2)));
    }

    [Theory]
    [InlineData("https://example.test/download")]
    [InlineData("http://github.com/Speechless-10308/VideoDesk/releases/tag/v0.1.2")]
    public async Task GetLatest_UnexpectedDownloadPage_IsRejected(string page)
    {
        using var http=new HttpClient(new StubHttpHandler((_,_)=>Task.FromResult(StubHttpHandler.Json($$"""{"tag_name":"v0.1.2","html_url":"{{page}}"}"""))));
        await Assert.ThrowsAsync<InvalidDataException>(()=>GitHubRelease.GetLatest(http,TestContext.Current.CancellationToken));
    }
}
