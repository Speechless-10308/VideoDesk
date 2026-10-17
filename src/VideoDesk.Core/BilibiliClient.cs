using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace VideoDesk.Core;

public sealed partial class BilibiliClient : IDisposable
{
    public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36";
    private readonly CookieContainer accountCookies = new();
    private readonly CookieContainer guestCookies = new();
    private readonly HttpClient account;
    private readonly HttpClient guest;
    private readonly CaptureStore captures;
    private readonly LocalStore local;
    private readonly SemaphoreSlim keyLock = new(1,1);
    private readonly SemaphoreSlim visitorLock = new(1,1);
    private bool accountVisitor,guestVisitor;
    private string imgKey = "", subKey = "";
    private DateTimeOffset keyTime;
    public BilibiliClient(CaptureStore captures, LocalStore local, Func<CookieContainer,HttpMessageHandler>? handlerFactory=null)
    {
        this.captures=captures; this.local=local;
        account=Create(accountCookies,handlerFactory); guest=Create(guestCookies,handlerFactory);
        var saved=local.LoadSession();
        if (!string.IsNullOrEmpty(saved)) { try { accountCookies.SetCookies(new Uri("https://www.bilibili.com"),saved); } catch(CookieException){} }
        foreach(var identity in new[]{true,false})if(local.LoadDeviceCookies(identity) is {Length:>0} devices)try{(identity?accountCookies:guestCookies).SetCookies(new Uri("https://www.bilibili.com/"),devices);}catch(CookieException){}
    }
    private static HttpClient Create(CookieContainer cookies,Func<CookieContainer,HttpMessageHandler>? handlerFactory)
    {
        var c=new HttpClient(handlerFactory?.Invoke(cookies)??new HttpClientHandler{CookieContainer=cookies,AutomaticDecompression=DecompressionMethods.All}) {Timeout=TimeSpan.FromSeconds(25)};
        c.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        c.DefaultRequestHeaders.Referrer=new Uri("https://www.bilibili.com/");
        return c;
    }
    public bool HasAccountCookie => accountCookies.GetCookies(new Uri("https://api.bilibili.com/")).Cast<Cookie>().Any(c=>c.Name=="SESSDATA"&&!c.Expired);
    public bool GuestHasAccountCookie => guestCookies.GetCookies(new Uri("https://api.bilibili.com/")).Cast<Cookie>().Any(c=>c.Name is "SESSDATA" or "bili_jct");
    private async Task EnsureVisitor(bool useAccount,CancellationToken ct)
    {
        await visitorLock.WaitAsync(ct);
        try
        {
            if(useAccount?accountVisitor:guestVisitor)return;
            var jar=useAccount?accountCookies:guestCookies;
            var present=jar.GetCookies(new Uri("https://api.bilibili.com/")).Cast<Cookie>().Where(c=>!c.Expired).Select(c=>c.Name).ToHashSet();
            if(!present.Contains("buvid3")||!present.Contains("buvid4"))
            {
                var r=await Request("visitor-init","https://api.bilibili.com/x/frontend/finger/spi",useAccount,ct);
                foreach(var (field,name) in new[]{("b_3","buvid3"),("b_4","buvid4")}){var value=r["data"].Str(field);if(!present.Contains(name)&&!string.IsNullOrWhiteSpace(value))jar.Add(new Cookie(name,value,"/",".bilibili.com"));}
            }
            PersistDeviceCookies(useAccount);
            if(useAccount)accountVisitor=true;else guestVisitor=true;
        }
        finally{visitorLock.Release();}
    }
    private void PersistDeviceCookies(bool useAccount)
    {
        var jar=useAccount?accountCookies:guestCookies;
        local.SaveDeviceCookies(useAccount,string.Join(",",jar.GetCookies(new Uri("https://api.bilibili.com/")).Cast<Cookie>().Where(c=>!c.Expired&&c.Name is "buvid3" or "buvid4").Select(c=>$"{c.Name}={c.Value}; Domain=.bilibili.com; Path=/")));
    }
    public string PlaybackCookies => accountCookies.GetCookieHeader(new Uri("https://api.bilibili.com/"));
    private async Task<JsonNode> Request(string label,string url,bool useAccount,CancellationToken ct, bool allowApiError=false,string? referer=null)
    {
        using var req=new HttpRequestMessage(HttpMethod.Get,url);
        if(referer!=null){req.Headers.Referrer=new Uri(referer);req.Headers.TryAddWithoutValidation("Origin","https://www.bilibili.com");}
        var cookies=captures.Enabled?(useAccount?accountCookies:guestCookies).GetCookieHeader(req.RequestUri!):null;
        var sw=captures.Enabled?Stopwatch.StartNew():null;
        using var res=await (useAccount?account:guest).SendAsync(req,ct).ConfigureAwait(false);
        var text=await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if(sw!=null)await captures.RecordAsync(label,useAccount?"account":"guest",req,res,text,sw.ElapsedMilliseconds,cookies:cookies).ConfigureAwait(false);
        if(!res.IsSuccessStatusCode) throw new HttpRequestException($"请求被服务器拒绝（HTTP {(int)res.StatusCode}）",null,res.StatusCode);
        var root=JsonNode.Parse(text)??throw new InvalidDataException("接口返回为空");
        var code=root.Num("code");
        if(code!=0&&!allowApiError) throw new BilibiliException((int)code,root.Str("message","请求失败"));
        return root;
    }
    private async Task<string> Signed(IDictionary<string,string> args,CancellationToken ct)
    {
        await keyLock.WaitAsync(ct);
        try { if(imgKey.Length==0||DateTimeOffset.UtcNow-keyTime>TimeSpan.FromHours(1)) { var n=await Request("wbi-keys","https://api.bilibili.com/x/web-interface/nav",false,ct,true); imgKey=n["data"]?["wbi_img"].Str("img_url")??""; subKey=n["data"]?["wbi_img"].Str("sub_url")??""; keyTime=DateTimeOffset.UtcNow; } }
        finally {keyLock.Release();}
        return WbiSigner.Sign(args,imgKey,subKey,DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }
    public async Task<AccountInfo> GetAccount(CancellationToken ct=default)
    {
        var r=await Request("account-state","https://api.bilibili.com/x/web-interface/nav",true,ct,true);
        var d=r["data"];
        return new(d.Flag("isLogin"),d.Str("uname","未登录"),d.Str("face"),d.Num("mid"),d?["vip"].Num("status")==1,(int)(d?["level_info"]?.Num("current_level")??-1),d.Flag("is_senior_member"),ParseUserIdentity(d));
    }
    public async Task<IReadOnlyList<VideoCard>> GetHome(DiscoveryMode mode,int page=1,bool popular=false,CancellationToken ct=default)
    {
        await EnsureVisitor(mode==DiscoveryMode.Regular,ct).ConfigureAwait(false);
        var uri=popular?$"https://api.bilibili.com/x/web-interface/popular?ps=20&pn={page}":$"https://api.bilibili.com/x/web-interface/index/top/feed/rcmd?fresh_type=3&ps=20&fresh_idx={page}&fresh_idx_1h={page}&brush={page-1}&fetch_row=1&homepage_ver=1";
        var r=await Request(popular?"popular":"home-recommendations",uri,mode==DiscoveryMode.Regular,ct).ConfigureAwait(false);
        var items=r["data"]?[popular?"list":"item"]?.AsArray()??[];
        return items.Where(n=>!string.IsNullOrEmpty(n.Str("bvid"))).Select(Card).DistinctBy(v=>v.Bvid).ToArray();
    }
    public async Task<IReadOnlyList<VideoCard>> Search(string keyword,DiscoveryMode mode,int page=1,string order="totalrank",CancellationToken ct=default,string fromSource="")
        => (await SearchResults(keyword,mode,page,order,ct,fromSource).ConfigureAwait(false)).Items;
    public async Task<SearchPage> SearchResults(string keyword,DiscoveryMode mode,int page=1,string order="totalrank",CancellationToken ct=default,string fromSource="")
    {
        await EnsureVisitor(mode==DiscoveryMode.Regular,ct);
        var args=new Dictionary<string,string>{{"search_type","video"},{"keyword",keyword},{"page",page.ToString()},{"page_size","20"},{"order",order}};
        if(fromSource.Length>0)args["from_source"]=fromSource;
        var q=await Signed(args,ct);
        var r=await Request("search","https://api.bilibili.com/x/web-interface/wbi/search/type?"+q,mode==DiscoveryMode.Regular,ct);
        return ParseSearchResults(r,page);
    }
    public static SearchPage ParseSearchResults(JsonNode r,int page)
    {
        var data=r["data"];if(data.Str("v_voucher").Length>0)throw new BilibiliException(-352,"B站要求验证本次搜索。请在官方搜索页完成验证后重试");
        if(data is null||data["result"] is not System.Text.Json.Nodes.JsonArray)throw new InvalidDataException("搜索响应没有结果列表，请重试本次搜索");
        var items=data["result"]!.AsArray().Where(n=>n.Str("bvid").Length>0).Select(n=>new VideoCard(n.Str("bvid"),n.Num("aid"),WebUtility.HtmlDecode(Regex.Replace(n.Str("title"),"<[^>]*>","")),ImageUrl(n.Str("pic")),n.Str("author"),n.Num("mid"),n.Num("play"),n.Num("video_review"),ParseDuration(n.Str("duration")),DateLabel(n.Num("pubdate")),PublishedAt:n.Num("pubdate"),Followed:n.Flag("is_followed"))).ToArray();
        var total=data.Num("numResults");return new(items,total,page< data.Num("numPages")&&items.Length>0);
    }
    public async Task<VideoDetail> GetDetail(string bvid,CancellationToken ct=default)
    {
        var r=await Request("video-detail","https://api.bilibili.com/x/web-interface/view?bvid="+Uri.EscapeDataString(bvid),true,ct);
        var d=r["data"];
        var parts=(d?["pages"]?.AsArray()??[]).Select(n=>new VideoPart(n.Num("cid"),(int)n.Num("page"),n.Str("part"),n.Real("duration"))).ToArray();
        return new(Card(d),d.Str("desc"),ImageUrl(d?["owner"].Str("face")??""),d?["stat"].Num("like")??0,d?["stat"].Num("coin")??0,d?["stat"].Num("favorite")??0,parts,(int)(d.Num("copyright")),(d?["staff"]?.AsArray()??[]).Select(n=>new VideoContributor(n.Num("mid"),n.Str("name"),ImageUrl(n.Str("face")),n.Str("title"))).ToArray(),ParseVideoCollection(d?["ugc_season"]));
    }
    public async Task<PlayStream> GetPlayStream(string bvid,long cid,int quality=80,CancellationToken ct=default)
    {
        var args=new Dictionary<string,string>{{"bvid",bvid},{"cid",cid.ToString()},{"qn",quality.ToString()},{"fnval","4048"},{"fnver","0"},{"fourk","1"}};
        var q=await Signed(args,ct);
        var r=await Request("playurl","https://api.bilibili.com/x/player/wbi/playurl?"+q,true,ct);
        var d=r["data"]??throw new InvalidDataException("缺少播放信息");
        if(d.Num("is_preview")==1) throw new BilibiliException(-1,"当前账号只能试看此视频");
        var qids=d["accept_quality"]?.AsArray()??[]; var qlabels=d["accept_description"]?.AsArray()??[];
        var qualities=qids.Select((n,i)=>new Quality(int.Parse(n!.ToString()),i<qlabels.Count?qlabels[i]!.ToString():n.ToString())).ToArray();
        if(d["dash"] is JsonNode dash)
        {
            var streams=dash["video"]?.AsArray()??throw new InvalidDataException("没有可播放视频流");
            var selected=streams.Where(n=>n.Num("id")==quality).OrderBy(n=>n.Num("codecid")==7?0:1).FirstOrDefault()
                ??streams.OrderByDescending(n=>n.Num("id")<=quality?n.Num("id"):-n.Num("id")).ThenBy(n=>n.Num("codecid")==7?0:1).FirstOrDefault()
                ??throw new InvalidDataException("当前账号没有可用的视频流");
            var audio=dash["audio"]?.AsArray().OrderByDescending(n=>n.Num("bandwidth")).FirstOrDefault();
            var available=streams.Select(n=>(int)n.Num("id")).ToHashSet();
            return new(selected.Str("baseUrl",selected.Str("base_url")),audio?.Str("baseUrl",audio.Str("base_url")),(int)selected.Num("id"),qualities.Where(p=>available.Contains(p.Id)).ToArray(),selected.Str("codecs"),dash.Real("duration"));
        }
        var urls=d["durl"]?.AsArray();
        if(urls?.Count==1) return new(urls[0].Str("url"),null,(int)d.Num("quality"),qualities,"progressive",d.Real("timelength")/1000);
        throw new InvalidDataException("该播放格式需要额外适配，请在官方页面观看");
    }
    public async Task<IReadOnlyList<VideoCard>> GetRelated(string bvid,DiscoveryMode mode,CancellationToken ct=default)
    {
        var r=await Request("related","https://api.bilibili.com/x/web-interface/archive/related?bvid="+Uri.EscapeDataString(bvid),mode==DiscoveryMode.Regular,ct);
        return (r["data"]?.AsArray()??[]).Select(Card).ToArray();
    }
    public async Task<IReadOnlyList<CommentItem>> GetComments(long aid,CancellationToken ct=default) => (await GetCommentPage(aid,ct:ct)).Items;
    public async Task<QrSession> CreateQr(CancellationToken ct=default)
    {
        var r=await Request("qr-create","https://passport.bilibili.com/x/passport-login/web/qrcode/generate",true,ct);
        return new(r["data"].Str("url"),r["data"].Str("qrcode_key"));
    }
    public async Task<JsonNode> ReplayReadOnly(string captureRequestPath,CancellationToken ct=default)
    {
        var entry=JsonNode.Parse(await File.ReadAllTextAsync(captureRequestPath,ct))??throw new InvalidDataException("抓包记录为空");
        if(entry.Str("method")!="GET")throw new InvalidOperationException("复现工具只发送只读 GET 请求");
        var uri=new Uri(entry.Str("url"));
        string[] paths=["/x/web-interface/nav","/x/frontend/finger/spi","/x/web-interface/index/top/feed/rcmd","/x/web-interface/popular","/x/web-interface/wbi/search/type","/x/web-interface/view","/x/player/wbi/playurl","/x/web-interface/archive/related","/x/v2/reply","/x/v2/reply/reply","/x/web-interface/history/cursor","/x/web-interface/archive/has/like","/x/web-interface/archive/coins","/x/v2/fav/video/favoured","/x/v3/fav/folder/created/list-all","/x/v3/fav/resource/list","/x/v3/fav/folder/info","/x/relation","/x/polymer/web-dynamic/v1/feed/all","/x/v2/dm/web/seg.so"];
        var allowed=uri.Host=="api.bilibili.com"&&paths.Contains(uri.AbsolutePath)||uri.Host=="s.search.bilibili.com"&&uri.AbsolutePath is "/main/hotword" or "/main/suggest";
        if(uri.Scheme!="https"||!allowed)throw new InvalidOperationException("这个请求不在已验证的只读接口范围中");
        var useAccount=entry.Str("identity")=="account";await EnsureVisitor(useAccount,ct);
        var url=uri.ToString();
        if(uri.Query.Contains("w_rid="))
        {
            var args=uri.Query.TrimStart('?').Split('&').Select(s=>s.Split('=',2)).Where(p=>p.Length==2&&p[0] is not "w_rid" and not "wts").ToDictionary(p=>Uri.UnescapeDataString(p[0]),p=>Uri.UnescapeDataString(p[1]));
            url=uri.GetLeftPart(UriPartial.Path)+"?"+await Signed(args,ct);
        }
        if(uri.AbsolutePath=="/x/v2/dm/web/seg.so")
        {
            var bytes=await RequestDanmakuSegmentBytes("replay-danmaku-segment",url,useAccount,ct);
            return System.Text.Json.JsonSerializer.SerializeToNode(new{format="protobuf",encoding="base64",data=Convert.ToBase64String(bytes)})!;
        }
        return await Request("replay-"+entry.Str("label","request"),url,useAccount,ct,true);
    }
    public async Task<QrPoll> PollQr(string key,CancellationToken ct=default)
    {
        var r=await Request("qr-poll","https://passport.bilibili.com/x/passport-login/web/qrcode/poll?qrcode_key="+Uri.EscapeDataString(key),true,ct);
        var d=r["data"]; var code=(int)d.Num("code");
        if(code==0) { var saved=string.Join(",",accountCookies.GetCookies(new Uri("https://api.bilibili.com/")).Cast<Cookie>().Select(c=>$"{c.Name}={c.Value}; Domain=.bilibili.com; Path=/"));local.SaveSession(saved); }
        return new(code,d.Str("message"));
    }
    private static VideoCard Card(JsonNode? n) => new(n.Str("bvid"),n.Num("aid")>0?n.Num("aid"):n.Num("id"),n.Str("title"),ImageUrl(n.Str("pic")),n?["owner"].Str("name")??"",n?["owner"].Num("mid")??0,n?["stat"].Num("view")??0,n?["stat"].Num("danmaku")??0,n.Real("duration"),DateLabel(n.Num("pubdate")),PublishedAt:n.Num("pubdate"),Followed:n.Flag("is_followed"),LikeBadge:ParseLikeBadge(n?["rcmd_reason"]));
    private static string ParseLikeBadge(JsonNode? reason)
    {
        var text=(reason is JsonObject?reason.Str("content"):reason is JsonValue?reason.ToString():"").Trim();return Regex.IsMatch(text,@"^\d+(?:\.\d+)?(?:万|亿)?点赞$")?text:"";
    }
    public static string ImageUrl(string s)=>s.StartsWith("//")?"https:"+s:s.Replace("http://","https://");
    public static string CountLabel(long n)=>n>=100000000?$"{n/100000000d:0.#}亿":n>=10000?$"{n/10000d:0.#}万":n.ToString();
    public static string DurationLabel(double s)=>s>=3600?TimeSpan.FromSeconds(s).ToString(@"h\:mm\:ss"):TimeSpan.FromSeconds(Math.Max(0,s)).ToString(@"mm\:ss");
    private static double ParseDuration(string s)=>s.Split(':').Reverse().Select((p,i)=>double.TryParse(p,out var n)?n*Math.Pow(60,i):0).Sum();
    private static string DateLabel(long timestamp)=>timestamp>0?TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.FromUnixTimeSeconds(timestamp),"China Standard Time").ToString("M-d"):"";
    public void Dispose(){account.Dispose();guest.Dispose();keyLock.Dispose();visitorLock.Dispose();}
}




