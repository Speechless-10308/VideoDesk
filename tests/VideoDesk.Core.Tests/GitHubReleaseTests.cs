using System.Net;
using VideoDesk.Core.Tests.Support;

namespace VideoDesk.Core.Tests;

public sealed class GitHubReleaseTests
{
    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.OK)]
    public async Task GetLatest_NoPublishedRelease_ReturnsNull(HttpStatusCode status)
    {
        using var http=new HttpClient(new StubHttpHandler((_,_)=>Task.FromResult(new HttpResponseMessage(status){Content=new StringContent("[]")})));
        Assert.Null(await GitHubRelease.GetLatest(http,TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetLatest_PublicRequest_ParsesVersionWithoutAccountCredentials(bool prerelease)
    {
        using var http=new HttpClient(new StubHttpHandler((request,_)=>
        {
            Assert.Equal("https://api.github.com/repos/Speechless-10308/VideoDesk/releases?per_page=100",request.RequestUri!.AbsoluteUri);
            Assert.NotEmpty(request.Headers.UserAgent);Assert.Null(request.Headers.Authorization);Assert.False(request.Headers.Contains("Cookie"));
            return Task.FromResult(StubHttpHandler.Json($$"""[{"tag_name":"v0.1.2","html_url":"https://github.com/Speechless-10308/VideoDesk/releases/tag/v0.1.2","draft":false,"prerelease":{{prerelease.ToString().ToLowerInvariant()}}}]"""));
        }));
        var release=await GitHubRelease.GetLatest(http,TestContext.Current.CancellationToken);
        Assert.NotNull(release);Assert.True(release.IsNewerThan(new Version(0,1,1,0)));Assert.False(release.IsNewerThan(new Version(0,1,2)));
    }

    [Fact]
    public async Task GetLatest_UnorderedReleases_SelectsHighestVersionAndSkipsDrafts()
    {
        using var http=new HttpClient(new StubHttpHandler((_,_)=>Task.FromResult(StubHttpHandler.Json("""
        [
          {"tag_name":"v0.1.1","html_url":"https://github.com/Speechless-10308/VideoDesk/releases/tag/v0.1.1"},
          {"tag_name":"v0.1.3","draft":true},
          {"tag_name":"nightly"},
          {"tag_name":"v0.1.2","html_url":"https://github.com/Speechless-10308/VideoDesk/releases/tag/v0.1.2","prerelease":true}
        ]
        """))));
        var release=await GitHubRelease.GetLatest(http,TestContext.Current.CancellationToken);
        Assert.NotNull(release);Assert.Equal(new Version(0,1,2,0),release.Version);
    }

    [Theory]
    [InlineData("https://example.test/download")]
    [InlineData("http://github.com/Speechless-10308/VideoDesk/releases/tag/v0.1.2")]
    public async Task GetLatest_UnexpectedDownloadPage_IsRejected(string page)
    {
        using var http=new HttpClient(new StubHttpHandler((_,_)=>Task.FromResult(StubHttpHandler.Json($$"""[{"tag_name":"v0.1.2","html_url":"{{page}}"}]"""))));
        await Assert.ThrowsAsync<InvalidDataException>(()=>GitHubRelease.GetLatest(http,TestContext.Current.CancellationToken));
    }
}
