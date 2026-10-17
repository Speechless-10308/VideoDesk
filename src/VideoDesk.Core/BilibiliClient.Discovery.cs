using System.Text.Json.Nodes;

namespace VideoDesk.Core;

public sealed partial class BilibiliClient
{
    public async Task<CreatorDynamicPage> GetFollowingDynamics(bool videoOnly=false,string offset="",CancellationToken ct=default)
    {
        var response=await Request("following-dynamics",$"https://api.bilibili.com/x/polymer/web-dynamic/v1/feed/all?type={(videoOnly?"video":"all")}&offset={Uri.EscapeDataString(offset)}&timezone_offset=-480&features=itemOpusStyle",true,ct);
        return await ReadDynamicPage(response["data"],0,ct);
    }
    private async Task<CreatorDynamicPage> ReadDynamicPage(JsonNode? data,long mid,CancellationToken ct)
    {
        var source=(data?["items"]?.AsArray()??[]).ToArray();var items=source.Select(n=>ParseCreatorDynamic(n,mid)).ToArray();
        // Only truncated summaries need another read. Normal cards use the feed response directly.
        using var limit=new SemaphoreSlim(3);
        await Task.WhenAll(source.Select(async(n,i)=>
        {
            var module=n?["modules"]?["module_dynamic"];var summary=module?["desc"];
            if(summary.Str("text").Length==0)summary=module?["major"]?["opus"]?["summary"]??summary;
            if(!summary.Flag("has_more")||items[i].Deleted)return;
            await limit.WaitAsync(ct);
            try{items[i]=await GetDynamicDetail(items[i].Id,ct);}catch(TaskCanceledException) when(!ct.IsCancellationRequested){}catch(Exception error) when(error is BilibiliException or HttpRequestException or TimeoutException){/* Keep the supplied summary if detail is unavailable. */}
            finally{limit.Release();}
        }));
        return new(items,data.Str("offset"),data.Flag("has_more"));
    }
    public async Task<IReadOnlyList<PopularPeriod>> GetPopularPeriods(bool music,DiscoveryMode mode,CancellationToken ct=default)
    {
        await EnsureVisitor(mode==DiscoveryMode.Regular,ct);
        var query=await Signed(new Dictionary<string,string>{{"web_location","333.934"},{music?"list_type":"type",music?"1":"all"}},ct);
        var response=await Request(music?"music-periods":"weekly-periods","https://api.bilibili.com"+(music?"/x/copyright-music-publicity/toplist/all_period":"/x/web-interface/popular/series/list")+"?"+query,mode==DiscoveryMode.Regular,ct,referer:"https://www.bilibili.com/v/popular/"+(music?"music":"weekly"));
        var list=response["data"]?["list"];
        if(!music)return (list?.AsArray()??[]).Select(n=>new PopularPeriod((int)n.Num("number"),n.Str("name"),n.Str("subject"))).OrderByDescending(p=>p.Id).ToArray();
        return (list?.AsObject()??[]).SelectMany(p=>p.Value?.AsArray()??[]).Select(n=>new PopularPeriod((int)n.Num("ID"),$"第 {n.Num("priod")} 期 · {DateLabel(n.Num("publish_time"))}",PublishedAt:n.Num("publish_time"))).OrderByDescending(p=>p.PublishedAt).ToArray();
    }
    public async Task<PopularPage> GetPopularPage(PopularCategory category,DiscoveryMode mode,int page=1,int period=0,int region=0,CancellationToken ct=default)
    {
        await EnsureVisitor(mode==DiscoveryMode.Regular,ct);
        var path=category switch
        {
            PopularCategory.General=>$"/x/web-interface/popular?ps=20&pn={page}",
            PopularCategory.Weekly=>$"/x/web-interface/popular/series/one?number={period}",
            PopularCategory.Precious=>$"/x/web-interface/popular/precious?page={page}&page_size=100",
            PopularCategory.Ranking when region<0=>$"{(region==-1?"/pgc/web/rank/list":"/pgc/season/rank/web/list")}?season_type={-region}&day=3",
            PopularCategory.Ranking=>$"/x/web-interface/ranking/v2?rid={region}&type=all",
            _=>$"/x/copyright-music-publicity/toplist/music_list?list_id={period}"
        };
        var parts=path.Split('?',2);var args=parts[1].Split('&').Select(s=>s.Split('=',2)).ToDictionary(s=>s[0],s=>s[1]);args["web_location"]="333.934";
        var signed=await Signed(args,ct);var referer="https://www.bilibili.com/v/popular/"+(category switch{PopularCategory.Weekly=>"weekly",PopularCategory.Precious=>"history",PopularCategory.Ranking=>"rank",PopularCategory.Music=>"music",_=>"all"});
        return ParsePopularPage(await Request("popular-"+category.ToString().ToLowerInvariant(),"https://api.bilibili.com"+parts[0]+"?"+signed,mode==DiscoveryMode.Regular,ct,referer:referer),category);
    }
    public static PopularPage ParsePopularPage(JsonNode response,PopularCategory category)
    {
        var data=response["data"]??response["result"];var index=0;
        var entries=(data?["list"]?.AsArray()??[]).Select(n=>
        {
            index++;
            if(n?["new_ep"]!=null)
            {
                var video=new VideoCard(n?["new_ep"].Str("bvid")??"",0,n.Str("title"),ImageUrl(n.Str("cover")),n?["new_ep"].Str("index_show")??"",0,n?["stat"].Num("view")??0,n?["stat"].Num("danmaku")??0,0);
                return new PopularEntry(video,Rank:index,Url:n.Str("url"));
            }
            if(category==PopularCategory.Music)
            {
                var creation=n.Str("creation_bvid");var bvid=creation.Length>0?creation:n.Str("mv_bvid");
                var video=new VideoCard(bvid,n.Num(creation.Length>0?"creation_aid":"mv_aid"),creation.Length>0?n.Str("creation_title"):n.Str("music_title"),ImageUrl(n.Str(creation.Length>0?"creation_cover":"mv_cover")),creation.Length>0?n.Str("creation_nickname"):n.Str("singer"),n.Num("creation_up"),n.Num("creation_play"),0,n.Real("creation_duration"));
                return new PopularEntry(video,Recommendation:n.Str("recommendation"),Rank:(int)n.Num("rank"),Url:"https://www.bilibili.com/v/popular/music",MusicTitle:n.Str("music_title"),Heat:n.Num("heat"));
            }
            var reason=n?["rcmd_reason"];var text=reason is JsonObject?reason.Str("content"):reason?.ToString()??"";
            return new PopularEntry(Card(n),category==PopularCategory.Weekly?text:"",category==PopularCategory.Precious?n.Str("achievement",text):"",category==PopularCategory.Ranking?index:0);
        }).ToArray();
        return new(entries,(category==PopularCategory.General&&!data.Flag("no_more")&&entries.Length>0)||(category==PopularCategory.Precious&&data.Flag("has_more")),data.Str("explain",data.Str("note",data.Str("reminder"))));
    }
}
