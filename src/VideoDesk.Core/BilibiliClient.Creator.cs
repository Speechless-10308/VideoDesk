using System.Net;
using System.Text.Json.Nodes;

namespace VideoDesk.Core;

public sealed partial class BilibiliClient
{
    public async Task<CreatorProfile> GetCreatorProfile(long mid,CancellationToken ct=default)
    {
        var q=await Signed(new Dictionary<string,string>{{"mid",mid.ToString()},{"web_location","333.999"}},ct);
        var profileTask=Request("creator-profile","https://api.bilibili.com/x/space/wbi/acc/info?"+q,true,ct);
        var statsTask=Request("creator-relations",$"https://api.bilibili.com/x/relation/stat?vmid={mid}",true,ct);
        var likesTask=Request("creator-stats",$"https://api.bilibili.com/x/space/upstat?mid={mid}",true,ct,true);
        await Task.WhenAll(profileTask,statsTask,likesTask).ConfigureAwait(false);var d=profileTask.Result["data"];var s=statsTask.Result["data"];
        return new(mid,d.Str("name"),ImageUrl(d.Str("face")),ImageUrl(d?["top_photo_v2"].Str("l_img",d.Str("top_photo"))??d.Str("top_photo")),d.Str("sign"),s.Num("follower"),s.Num("following"),likesTask.Result["data"].Num("likes"),d.Flag("is_followed"),(int)(d?["level"]?.GetValue<int>()??-1),d.Flag("is_senior_member"),ParseUserIdentity(d));
    }
    public async Task<CreatorVideoPage> GetCreatorVideos(long mid,int page=1,string keyword="",string order="pubdate",CancellationToken ct=default)
    {
        var q=await Signed(new Dictionary<string,string>{{"mid",mid.ToString()},{"pn",page.ToString()},{"ps","20"},{"tid","0"},{"keyword",keyword},{"order",order},{"platform","web"},{"web_location","333.1387"}},ct);
        var r=await Request("creator-videos","https://api.bilibili.com/x/space/wbi/arc/search?"+q,true,ct);var d=r["data"];
        if(d.Str("v_voucher").Length>0)throw new BilibiliException(-352,"B站要求验证空间访问，请在官方空间完成验证后重试");
        var items=(d?["list"]?["vlist"]?.AsArray()??[]).Select(n=>new VideoCard(n.Str("bvid"),n.Num("aid"),WebUtility.HtmlDecode(n.Str("title")),ImageUrl(n.Str("pic")),n.Str("author"),n.Num("mid")>0?n.Num("mid"):mid,n.Num("play"),n.Num("video_review"),ParseDuration(n.Str("length")),DateLabel(n.Num("created")),PublishedAt:n.Num("created"))).ToArray();var total=d?["page"].Num("count")??0;return new(items,total,page*20<total&&items.Length>0);
    }
    public async Task<CreatorDynamicPage> GetCreatorDynamics(long mid,string offset="",CancellationToken ct=default)
    {
        var r=await Request("creator-dynamics",$"https://api.bilibili.com/x/polymer/web-dynamic/v1/feed/space?host_mid={mid}&offset={Uri.EscapeDataString(offset)}&timezone_offset=-480&features=itemOpusStyle",true,ct);
        return await ReadDynamicPage(r["data"],mid,ct);
    }
    public static CreatorDynamic ParseCreatorDynamic(JsonNode? n,long fallbackMid=0,int depth=0)
    {
        var modules=n?["modules"];var author=modules?["module_author"];var dynamic=modules?["module_dynamic"];var major=dynamic?["major"];var archive=major?["archive"];var opus=major?["opus"];var desc=dynamic?["desc"]??opus?["summary"];var stat=modules?["module_stat"];var basic=n?["basic"];
        if(string.IsNullOrEmpty(desc.Str("text"))&&opus?["summary"]!=null)desc=opus["summary"];
        var bodyText=desc.Str("text");
        if(bodyText.Length==0)bodyText=string.Concat((desc?["rich_text_nodes"]?.AsArray()??[]).Select(p=>p.Str("orig_text",p.Str("text"))));
        if(bodyText.Length==0)bodyText=opus.Str("title");
        var mid=author.Num("mid");if(mid==0)mid=fallbackMid;
        var emotes=new Dictionary<string,CommentEmote>();var links=new Dictionary<string,CommentLink>();
        foreach(var node in desc?["rich_text_nodes"]?.AsArray()??[])
        {
            var text=node.Str("orig_text",node.Str("text"));if(text.Length==0)continue;var kind=node.Str("type");
            if(kind=="RICH_TEXT_NODE_TYPE_EMOJI")emotes[text]=new(ImageUrl(node?["emoji"].Str("icon_url")??""),(int)(node?["emoji"].Num("size")??1));
            else if(kind!="RICH_TEXT_NODE_TYPE_TEXT")
            {
                var url=ImageUrl(node.Str("jump_url"));if(kind=="RICH_TEXT_NODE_TYPE_AT")url="https://space.bilibili.com/"+node.Str("rid");
                if(url.Length==0&&kind=="RICH_TEXT_NODE_TYPE_TOPIC")url="https://search.bilibili.com/all?keyword="+Uri.EscapeDataString(text.Trim('#'));
                if(url.Length==0&&kind=="RICH_TEXT_NODE_TYPE_BV")url=node.Str("rid");
                if(url.Length>0)links[text]=new(node.Str("text",text),url);
            }
        }
        var pictures=(major?["draw"]?["items"]?.AsArray()??opus?["pics"]?.AsArray()??[]).Select(p=>ImageUrl(p.Str("src",p.Str("url")))).Where(s=>s.Length>0).ToArray();
        VideoCard? video=archive==null?null:new(archive.Str("bvid"),archive.Num("aid"),archive.Str("title"),ImageUrl(archive.Str("cover")),author.Str("name"),mid,ParseCount(archive?["stat"].Str("play")??"0"),ParseCount(archive?["stat"].Str("danmaku")??"0"),ParseDuration(archive.Str("duration_text")));
        DynamicAttachment? attachment=null;var other=major?["article"]??major?["common"]??major?["music"]??major?["pgc"]??major?["courses"];
        if(other!=null)attachment=new(other.Str("title"),other.Str("desc",other.Str("sub_title")),ImageUrl(other.Str("cover",other?["covers"]?.AsArray().FirstOrDefault()?.ToString()??"")),ImageUrl(other.Str("jump_url")),other.Str("label"));
        var topic=dynamic?["topic"];var original=depth<2&&n?["orig"] is {} orig?ParseCreatorDynamic(orig,0,depth+1):null;
        var deleted=n.Str("type")=="DYNAMIC_TYPE_NONE"||major?["none"]!=null;
        return new(n.Str("id_str"),bodyText,author.Str("pub_time"),pictures,video,mid,author.Str("name"),ImageUrl(author.Str("face")),author.Str("pub_action"),emotes,links,original,archive.Str("desc"),stat?["like"].Num("count")??0,stat?["comment"].Num("count")??0,stat?["forward"].Num("count")??0,stat?["like"].Flag("status")==true,topic.Str("name"),ImageUrl(topic.Str("jump_url")),attachment,deleted,basic.Num("comment_id_str"),(int)basic.Num("comment_type"),stat?["comment"].Flag("forbidden")==true,stat?["forward"].Flag("forbidden")==true,stat?["like"].Flag("forbidden")==true,ParseUserIdentity(author));
    }
    private static long ParseCount(string s){if(long.TryParse(s,out var n))return n;if(double.TryParse(s.TrimEnd('万','亿'),System.Globalization.NumberStyles.Any,System.Globalization.CultureInfo.InvariantCulture,out var v))return (long)(v*(s.EndsWith('亿')?100000000:s.EndsWith('万')?10000:1));return 0;}
    public async Task<(IReadOnlyList<CreatorCollection> Items,bool HasMore)> GetCreatorCollections(long mid,int page=1,CancellationToken ct=default)
    {
        var r=await Request("creator-collections",$"https://api.bilibili.com/x/polymer/web-space/seasons_series_list?mid={mid}&page_num={page}&page_size=20",true,ct);var d=r["data"]?["items_lists"];
        CreatorCollection Parse(JsonNode? n,bool series){var m=n?["meta"];return new(m.Num(series?"series_id":"season_id"),m.Str("name"),ImageUrl(m.Str("cover")),m.Num("total"),series);}
        var items=(d?["seasons_list"]?.AsArray()??[]).Select(n=>Parse(n,false)).Concat((d?["series_list"]?.AsArray()??[]).Select(n=>Parse(n,true))).ToArray();return (items,page*20<(d?["page"].Num("total")??0));
    }
    public async Task<CreatorVideoPage> GetCreatorCollectionVideos(long mid,CreatorCollection collection,int page=1,CancellationToken ct=default)
    {
        var url=collection.Series?$"https://api.bilibili.com/x/series/archives?mid={mid}&series_id={collection.Id}&pn={page}&ps=20&sort=desc&only_normal=true":$"https://api.bilibili.com/x/polymer/web-space/seasons_archives_list?mid={mid}&season_id={collection.Id}&page_num={page}&page_size=20&sort_reverse=false";
        var r=await Request("creator-collection-videos",url,true,ct);var d=r["data"];var items=(d?["archives"]?.AsArray()??[]).Select(Card).Select(v=>v with{Mid=mid}).ToArray();var total=d?["page"].Num("total")??collection.Count;return new(items,total,page*20<total&&items.Length>0);
    }
}




