using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace VideoDesk.Core;

public sealed partial class BilibiliClient
{
    public async Task<DanmakuLikeState> GetDanmakuLikeState(long cid,long dmid,CancellationToken ct=default)
    {var r=await Request("danmaku-like-state",$"https://api.bilibili.com/x/v2/dm/thumbup/detail?oid={cid}&dmid={dmid}",true,ct);return new(r["data"].Flag("user_like"),r["data"].Num("likes"));}
    public async Task<IReadOnlyList<DanmakuFilter>> GetDanmakuFilters(CancellationToken ct=default)
    {var r=await Request("danmaku-filters","https://api.bilibili.com/x/dm/filter/user",true,ct);return (r["data"]?["rule"]?.AsArray()??[]).Select(n=>new DanmakuFilter((int)n.Num("type"),n.Str("filter"))).Where(f=>f.Type is >=0 and <=2&&f.Filter.Length<=500).ToArray();}
    public async Task<DanmakuMaskInfo?> GetDanmakuMaskInfo(long aid,long cid,CancellationToken ct=default)
    {var q=await Signed(new Dictionary<string,string>{{"aid",aid.ToString()},{"cid",cid.ToString()}},ct);var r=await Request("player-metadata","https://api.bilibili.com/x/player/wbi/v2?"+q,true,ct);var d=r["data"]?["dm_mask"];var url=d.Str("mask_url");return string.IsNullOrEmpty(url)?null:new(ImageUrl(url),(int)d.Num("fps"));}
    public async Task<byte[]> GetDanmakuMaskRange(string url,long start,long? end,CancellationToken ct=default)
    {
        var uri=new Uri(url);if(uri.Scheme!="https"||!(uri.Host.EndsWith(".hdslb.com",StringComparison.OrdinalIgnoreCase)||uri.Host.EndsWith(".bilivideo.com",StringComparison.OrdinalIgnoreCase))||start<0||end<start)throw new ArgumentException("无效的官方弹幕遮罩地址");
        using var req=new HttpRequestMessage(HttpMethod.Get,uri);req.Headers.Range=new RangeHeaderValue(start,end);var watch=captures.Enabled?Stopwatch.StartNew():null;using var response=await guest.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false);if(response.Content.Headers.ContentLength>16*1048576)throw new InvalidDataException("遮罩片段过大");using var stream=await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);using var output=new MemoryStream();var buffer=new byte[8192];int read;while((read=await stream.ReadAsync(buffer,ct).ConfigureAwait(false))>0){if(output.Length+read>16*1048576)throw new InvalidDataException("遮罩片段过大");await output.WriteAsync(buffer.AsMemory(0,read),ct).ConfigureAwait(false);}var bytes=output.ToArray();
        if(watch!=null)await captures.RecordAsync("danmaku-mask-range","guest",req,response,JsonSerializer.Serialize(new{format="mask",encoding="base64",data=Convert.ToBase64String(bytes)}),watch.ElapsedMilliseconds).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if(response.StatusCode!=System.Net.HttpStatusCode.PartialContent&&start!=0)throw new InvalidDataException("遮罩服务未返回所请求的片段");return bytes;
    }
}
