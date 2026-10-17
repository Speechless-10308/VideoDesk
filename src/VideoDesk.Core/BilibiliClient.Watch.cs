using System.Text.Json.Nodes;

namespace VideoDesk.Core;

public sealed partial class BilibiliClient
{
    public static CommentSubmission ParseCommentSubmission(JsonNode response)
    {
        var d=response["data"]??throw new InvalidDataException("评论提交响应缺少结果");
        if(d.Flag("need_captcha")||d.Flag("need_captcha_v2"))return new(0,null,CommentPublication.VerificationRequired,"B站要求验证，本次发布未确认；评论草稿已保留",d.Str("url"));
        var id=d.Num("rpid");if(id<=0)throw new InvalidDataException(d.Str("success_toast","没有收到评论编号，不能确认提交成功"));
        var reply=d["reply"] is {} n?ParseComment(n):null;
        return new(id,reply,CommentPublication.Submitted,d.Str("success_toast","发送成功"));
    }
    public async Task<CommentPublication> GetCommentPublication(long aid,long rpid,CancellationToken ct=default)
    {
        var r=await Request("comment-public-status",$"https://api.bilibili.com/x/v2/reply/detail?type=1&oid={aid}&root={rpid}",false,ct,true);
        if(r.Num("code") is 12006 or 12022)return CommentPublication.AwaitingReview;
        if(r.Num("code")!=0)return CommentPublication.Submitted;
        var root=r["data"]?["root"];return root?.Num("rpid")==rpid&&root.Num("state")==0?CommentPublication.Visible:CommentPublication.AwaitingReview;
    }
    public async Task<IReadOnlyList<DanmakuItem>> GetDanmakuSegment(long cid,int segment,CancellationToken ct=default)
    {
        var bytes=await RequestDanmakuSegmentBytes("danmaku-segment",$"https://api.bilibili.com/x/v2/dm/web/seg.so?type=1&oid={cid}&segment_index={segment}",true,ct).ConfigureAwait(false);return DanmakuProtocol.Parse(bytes).OrderBy(m=>m.Time).ToArray();
    }
    public async Task<string> GetOfficialSource(string url,CancellationToken ct=default)
    {
        var uri=new Uri(url);if(uri.Scheme!="https"||!(uri.Host.EndsWith(".bilibili.com")||uri.Host.EndsWith(".hdslb.com")))throw new ArgumentException("Official HTTPS source required");
        using var req=new HttpRequestMessage(HttpMethod.Get,uri);using var res=await guest.SendAsync(req,ct);var text=await res.Content.ReadAsStringAsync(ct);if(captures.Enabled)await captures.RecordAsync("official-watch-source","guest",req,res,System.Text.Json.JsonSerializer.Serialize(new{format="source",text}),0);res.EnsureSuccessStatusCode();return text;
    }
    public async Task<CreatorSummary> GetCreatorSummary(long mid,CancellationToken ct=default)
    {
        var r=await Request("creator-card",$"https://api.bilibili.com/x/web-interface/card?mid={mid}&photo=true",true,ct);var d=r["data"];var c=d?["card"];
        return new(mid,c.Str("name"),ImageUrl(c.Str("face")),d.Num("follower"),d.Num("archive_count"),(int)(c?["level_info"]?.Num("current_level")??-1),c.Flag("is_senior_member"),ParseUserIdentity(c));
    }
    public async Task<OnlineViewers> GetOnlineViewers(string bvid,long cid,CancellationToken ct=default)
    {
        var r=await Request("online-viewers",$"https://api.bilibili.com/x/player/online/total?bvid={Uri.EscapeDataString(bvid)}&cid={cid}&ts={(DateTimeOffset.UtcNow.ToUnixTimeSeconds()+29)/30}",true,ct);var d=r["data"];var switches=d?["show_switch"];return new(d.Str("total","—"),d.Str("total","—"),switches.Flag("total"));
    }
    public async Task<IReadOnlyList<VideoTag>> GetVideoTags(string bvid,long aid,CancellationToken ct=default)
    {
        var r=await Request("video-tags",$"https://api.bilibili.com/x/tag/archive/tags?aid={aid}&bvid={Uri.EscapeDataString(bvid)}",true,ct);return (r["data"]?.AsArray()??[]).Where(n=>n.Str("tag_name").Length>0).Select(n=>new VideoTag(n.Num("tag_id"),n.Str("tag_name"),n.Str("tag_type"))).ToArray();
    }
    public async Task<string> ResolveVideoAid(long aid,CancellationToken ct=default)
    {
        var r=await Request("video-detail",$"https://api.bilibili.com/x/web-interface/view?aid={aid}",true,ct);return r["data"].Str("bvid");
    }
    public async Task<VideoHeatmap?> GetVideoHeatmap(string bvid,long aid,long cid,CancellationToken ct=default)
    {
        var r=await Request("video-heatmap",$"https://bvc.bilivideo.com/pbp/data?aid={aid}&cid={cid}&bvid={Uri.EscapeDataString(bvid)}&r=loader",false,ct);
        return ParseVideoHeatmap(r);
    }
    public static VideoHeatmap? ParseVideoHeatmap(JsonNode r)
    {
        var module=((r["data"]??r)["modules"]?.AsArray()??[]).FirstOrDefault(m=>m.Str("load_mode")=="pbp");var d=module?["params"]?["data"];var values=(d?["events"]?["default"]?.AsArray()??[]).Select(n=>double.TryParse(n?.ToString(),System.Globalization.NumberStyles.Any,System.Globalization.CultureInfo.InvariantCulture,out var v)?Math.Max(0,v):0).ToArray();
        return d?.Real("step_sec") is >0&&values.Length>0?new(d.Real("step_sec"),values):null;
    }
    public static VideoCollectionContext? ParseVideoCollection(JsonNode? season)
    {
        if(season?.Num("id") is not >0)return null;var mid=season.Num("mid");
        var videos=(season["sections"]?.AsArray()??[]).SelectMany(section=>section?["episodes"]?.AsArray()??[]).Where(e=>e.Str("bvid").Length>0).Select(e=>{var a=e?["arc"];var author=a?["author"];return new VideoCard(e.Str("bvid"),e.Num("aid"),a.Str("title",e.Str("title")),ImageUrl(a.Str("pic")),author.Str("name"),author.Num("mid")>0?author.Num("mid"):mid,a?["stat"].Num("view")??0,a?["stat"].Num("danmaku")??0,a.Real("duration"),DateLabel(a.Num("pubdate")));}).DistinctBy(v=>v.Bvid).ToArray();
        return new(season.Num("id"),mid,season.Str("title"),videos,season.Num("ep_count")>0?season.Num("ep_count"):videos.Length);
    }
}

