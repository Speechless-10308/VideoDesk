using System.Net;
using System.Text.Json.Nodes;

namespace VideoDesk.Core;

public record GitHubRelease(Version Version,Uri Page)
{
    public bool IsNewerThan(Version current)=>Version>Normalize(current);
    private static Version Normalize(Version value)=>new(value.Major,value.Minor,Math.Max(0,value.Build),Math.Max(0,value.Revision));

    public static async Task<GitHubRelease?> GetLatest(HttpClient http,CancellationToken ct=default)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,"https://api.github.com/repos/Speechless-10308/VideoDesk/releases/latest");
        request.Headers.UserAgent.ParseAdd("VideoDesk");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response=await http.SendAsync(request,ct).ConfigureAwait(false);
        if(response.StatusCode==HttpStatusCode.NotFound)return null;
        response.EnsureSuccessStatusCode();
        await using var stream=await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var data=await JsonNode.ParseAsync(stream,cancellationToken:ct).ConfigureAwait(false);
        if(data?["draft"]?.GetValue<bool>()==true||data?["prerelease"]?.GetValue<bool>()==true)return null;
        var tag=data?["tag_name"]?.GetValue<string>()?.Trim().TrimStart('v','V');
        if(!Version.TryParse(tag,out var version))throw new InvalidDataException("无法识别发布版本号");
        if(!Uri.TryCreate(data?["html_url"]?.GetValue<string>(),UriKind.Absolute,out var page)||
            page.Scheme!=Uri.UriSchemeHttps||page.Host!="github.com"||!page.AbsolutePath.StartsWith("/Speechless-10308/VideoDesk/releases/",StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("发布页面地址无效");
        return new(Normalize(version),page);
    }
}
