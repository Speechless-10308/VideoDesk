using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VideoDesk.Core;

public sealed partial class BilibiliClient
{
    private readonly SemaphoreSlim mutationLock=new(1,1);
    private string Csrf => accountCookies.GetCookies(new Uri("https://api.bilibili.com/")).Cast<Cookie>().FirstOrDefault(c=>c.Name=="bili_jct")?.Value??"";
    // Mutations deliberately have no automatic retry: a timeout can mean that the server accepted the operation.
    private async Task<JsonNode> Post(string label,string path,Dictionary<string,string> values,CancellationToken ct,Action<HttpRequestMessage>? configure=null,bool allowApiError=false)
    {
        if(!HasAccountCookie||string.IsNullOrEmpty(Csrf))throw new InvalidOperationException("请先扫码登录");
        await mutationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            values["csrf"]=Csrf;values["csrf_token"]=Csrf;
            using var req=new HttpRequestMessage(HttpMethod.Post,"https://api.bilibili.com"+path){Content=new FormUrlEncodedContent(values)};
            configure?.Invoke(req);
            var body=captures.Enabled?await req.Content.ReadAsStringAsync(ct).ConfigureAwait(false):null;var sw=captures.Enabled?Stopwatch.StartNew():null;
            using var res=await account.SendAsync(req,ct).ConfigureAwait(false);var text=await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if(sw!=null)await captures.RecordAsync(label,"account",req,res,text,sw.ElapsedMilliseconds,body,accountCookies.GetCookieHeader(req.RequestUri!)).ConfigureAwait(false);
            if(!res.IsSuccessStatusCode)throw new HttpRequestException($"请求失败（HTTP {(int)res.StatusCode}）",null,res.StatusCode);
            var r=JsonNode.Parse(text)??throw new InvalidDataException("响应为空");if(r.Num("code")!=0&&!allowApiError)throw new BilibiliException((int)r.Num("code"),r.Str("message"));return r;
        }
        catch(TaskCanceledException) when(!ct.IsCancellationRequested){throw new TimeoutException("请求超时，服务器可能已经收到操作。请刷新状态后再决定是否重试。");}
        finally{mutationLock.Release();}
    }
    public async Task<IReadOnlyList<SearchTopic>> GetHotSearch(CancellationToken ct=default)
    {
        var r=await Request("search-hot","https://s.search.bilibili.com/main/hotword?limit=10",false,ct,true);
        return (r["list"]?.AsArray()??r["data"]?["list"]?.AsArray()??[]).Select(n=>new SearchTopic(n.Str("keyword",n.Str("show_name")),ImageUrl(n.Str("icon")))).Where(n=>n.Keyword.Length>0).Take(10).ToArray();
    }
    public async Task<IReadOnlyList<string>> GetSearchSuggestions(string term,CancellationToken ct=default)
    {
        var r=await Request("search-suggestions","https://s.search.bilibili.com/main/suggest?term="+Uri.EscapeDataString(term)+"&main_ver=v1&highlight=&func=suggest&suggest_type=accurate",false,ct,true);
        return (r["result"]?["tag"]?.AsArray()??r["data"]?["tag"]?.AsArray()??[]).Select(n=>WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(n.Str("value",n.Str("name")),"<[^>]*>",""))).Where(s=>s.Length>0).Take(10).ToArray();
    }
    public async Task<CommentPage> GetCommentPage(long aid,int page=1,bool newest=false,CancellationToken ct=default,long next=0,int type=1)
    {
        var r=await Request("comments-cursor",$"https://api.bilibili.com/x/v2/reply/main?type={type}&oid={aid}&mode={(newest?2:3)}&next={next}&ps=20",true,ct);
        var d=r["data"];var items=(d?["replies"]?.AsArray()??[]).Where(n=>n!=null).Select(ParseComment).ToArray();
        var top=page==1?(d?["top_replies"]?.AsArray()??[]).Where(n=>n!=null).Select(ParseComment):[];
        if(page==1&&d?["top"]?["upper"] is {} upper)top=top.Prepend(ParseComment(upper));
        var cursor=d?["cursor"];return new(top.Concat(items).DistinctBy(c=>c.Id).ToArray(),cursor.Num("all_count"),page,!cursor.Flag("is_end")&&items.Length>0,cursor.Num("next"));
    }
    public async Task<CommentPage> GetReplies(long aid,long root,int page=1,CancellationToken ct=default,int type=1)
    {
        var r=await Request("comment-replies",$"https://api.bilibili.com/x/v2/reply/reply?type={type}&oid={aid}&root={root}&pn={page}&ps=20",true,ct);
        var d=r["data"];var items=(d?["replies"]?.AsArray()??[]).Select(ParseComment).ToArray();var total=d?["page"].Num("count")??0;return new(items,total,page,page*20<total&&items.Length>0);
    }
    public static CommentItem ParseComment(JsonNode? n)
    {
        var c=n?["content"];var control=n?["reply_control"];var member=n?["member"];
        var pictures=(c?["pictures"]?.AsArray()??[]).Select(p=>new CommentPicture(ImageUrl(p.Str("img_src")),(int)p.Num("img_width"),(int)p.Num("img_height"),ImageUrl(p.Str("top_right_icon")))).ToArray();
        var links=(c?["jump_url"]?.AsObject()??new()).ToDictionary(p=>p.Key,p=>new CommentLink(p.Value.Str("title",p.Key),ImageUrl(p.Value.Str("pc_url",p.Value.Str("url"))),ImageUrl(p.Value.Str("prefix_icon")),p.Value.Flag("match_once")));
        foreach(var mention in c?["members"]?.AsArray()??[])if(mention.Num("mid")>0)links["@"+mention.Str("uname")]=new("@"+mention.Str("uname"),"https://space.bilibili.com/"+mention.Num("mid"));
        CommentNote? note=control.Flag("is_note")&&n.Str("note_cvid_str").Length>0?new("https://www.bilibili.com/h5/note-app/view?cvid="+n.Str("note_cvid_str"),"note"):control.Flag("is_note_v2")&&n.Str("dynamic_id_str").Length>0?new("https://www.bilibili.com/opus/"+n.Str("dynamic_id_str"),"opus"):null;
        return new(member.Str("uname"),c.Str("message"),n.Num("like"),n.Num("rpid"),ImageUrl(member.Str("avatar")),n.Num("ctime"),n.Num("mid"),n.Num("rcount"),(n?["replies"]?.AsArray()??[]).Select(ParseComment).ToArray(),n.Num("action")==1,(c?["emote"]?.AsObject()??new()).ToDictionary(p=>p.Key,p=>new CommentEmote(ImageUrl(p.Value.Str("url")),(int)(p.Value?["meta"].Num("size")??1))),(int)(member?["level_info"]?.Num("current_level")??-1),member.Flag("is_senior_member"),(int)n.Num("state"),pictures,links,note,n?["up_action"].Flag("like")==true||control.Flag("up_like"),control.Flag("is_up_top"),n.Num("action")==2,ParseUserIdentity(member));
    }
    public async Task<HistoryPage> GetHistory(long max=0,long viewedAt=0,string business="",CancellationToken ct=default)
    {
        var r=await Request("history-cursor",$"https://api.bilibili.com/x/web-interface/history/cursor?ps=30&max={max}&view_at={viewedAt}&business={Uri.EscapeDataString(business)}",true,ct).ConfigureAwait(false);
        var d=r["data"];var items=(d?["list"]?.AsArray()??[]).Where(n=>n?["history"].Str("bvid")?.Length>0).Select(n=>new WatchProgress(n!["history"].Str("bvid"),n["history"].Num("cid"),n.Num("progress")==-1?n.Real("duration"):n.Real("progress"),n.Real("duration"),DateTimeOffset.FromUnixTimeSeconds(n.Num("view_at")),n.Str("title"),ImageUrl(n.Str("cover")),n.Str("author_name"),"",true,n["history"].Num("oid"),n.Num("author_mid"))).ToArray();
        var cursor=d?["cursor"];return new(items,cursor.Num("max"),cursor.Num("view_at"),cursor.Str("business"),items.Length>0&&cursor.Num("max")!=0);
    }
    public async Task ReportHistory(string bvid,long cid,double position,CancellationToken ct=default,long aid=0,double duration=0,double playedSeconds=0,long startedAt=0)
    {
        if(aid==0)aid=(await GetDetail(bvid,ct)).Video.Aid;
        var mid=accountCookies.GetCookies(new Uri("https://api.bilibili.com/")).Cast<Cookie>().FirstOrDefault(c=>c.Name=="DedeUserID")?.Value??"0";
        var started=startedAt>0?startedAt:DateTimeOffset.UtcNow.ToUnixTimeSeconds()-(long)playedSeconds;
        var played=Math.Max(0,(int)position).ToString();var active=Math.Max(0,(int)playedSeconds).ToString();var videoDuration=Math.Max(0,(int)duration).ToString();
        var signed=await Signed(new Dictionary<string,string>{{"w_start_ts",started.ToString()},{"w_mid",mid},{"w_aid",aid.ToString()},{"w_dt","2"},{"w_realtime",active},{"w_played_time",played},{"w_real_played_time",active},{"w_video_duration",videoDuration},{"w_last_play_progress_time",played}},ct);
        _=await Post("history-report","/x/click-interface/web/heartbeat?"+signed,new(){{"aid",aid.ToString()},{"bvid",bvid},{"cid",cid.ToString()},{"mid",mid},{"start_ts",started.ToString()},{"type","3"},{"dt","2"},{"play_type","0"},{"realtime",active},{"played_time",played},{"real_played_time",active},{"video_duration",videoDuration},{"last_play_progress_time",played},{"max_play_progress_time",played},{"mobi_app","web"},{"device","web"},{"platform","web"}},ct);
    }
    public async Task DeleteHistory(long aid,CancellationToken ct=default)=>_ = await Post("history-delete","/x/v2/history/delete",new(){{"kid","archive_"+aid}},ct);
    public async Task ClearCloudHistory(CancellationToken ct=default)=>_ = await Post("history-clear","/x/v2/history/clear",new(),ct);
    public async Task<VideoAccountState> GetVideoAccountState(string bvid,long aid=0,CancellationToken ct=default)
    {
        if(!HasAccountCookie)return new(false,0,false);
        if(aid==0)aid=(await GetDetail(bvid,ct).ConfigureAwait(false)).Video.Aid;
        var like=Request("video-liked","https://api.bilibili.com/x/web-interface/archive/has/like?bvid="+bvid,true,ct);
        var coins=Request("video-coins","https://api.bilibili.com/x/web-interface/archive/coins?bvid="+bvid,true,ct);
        var fav=Request("video-favorited","https://api.bilibili.com/x/v2/fav/video/favoured?aid="+aid,true,ct);
        await Task.WhenAll(like,coins,fav).ConfigureAwait(false);
        return new(like.Result.Num("data")==1,(int)(coins.Result["data"].Num("multiply")),fav.Result["data"].Flag("favoured"));
    }
    public async Task SetLike(string bvid,bool liked,CancellationToken ct=default)=>_ = await Post("video-like","/x/web-interface/archive/like",new(){{"bvid",bvid},{"like",liked?"1":"2"}},ct);
    public async Task AddCoins(string bvid,int amount,bool alsoLike,CancellationToken ct=default)
    {
        if(amount is <1 or >2)throw new ArgumentOutOfRangeException(nameof(amount));
        _=await Post("video-coin","/x/web-interface/coin/add",new(){{"bvid",bvid},{"multiply",amount.ToString()},{"select_like",alsoLike?"1":"0"}},ct);
    }
    public async Task<IReadOnlyList<FavoriteFolder>> GetFavoriteFolders(long mid,long aid=0,CancellationToken ct=default)
    {
        var r=await Request("favorite-folders",$"https://api.bilibili.com/x/v3/fav/folder/created/list-all?up_mid={mid}&type=2&rid={aid}",true,ct).ConfigureAwait(false);
        return (r["data"]?["list"]?.AsArray()??[]).Select(n=>new FavoriteFolder(n.Num("id"),n.Str("title"),n.Num("media_count"),n.Num("fav_state")==1,(n.Num("attr")&1)==0)).ToArray();
    }
    public async Task<FavoritePage> GetFavoriteVideos(long folder,int page=1,CancellationToken ct=default)
    {
        var r=await Request("favorite-videos",$"https://api.bilibili.com/x/v3/fav/resource/list?media_id={folder}&pn={page}&ps=20&order=mtime&type=0&tid=0",true,ct).ConfigureAwait(false);var d=r["data"];
        var cards=(d?["medias"]?.AsArray()??[]).Where(n=>!string.IsNullOrEmpty(n.Str("bvid"))).Select(n=>new VideoCard(n.Str("bvid"),n.Num("id"),n.Str("title"),ImageUrl(n.Str("cover")),n?["upper"].Str("name")??"",n?["upper"].Num("mid")??0,n?["cnt_info"].Num("play")??0,n?["cnt_info"].Num("danmaku")??0,n.Real("duration"),DateLabel(n.Num("pubtime")))).ToArray();
        return new(cards,d.Flag("has_more"),d?["info"].Num("media_count")??0);
    }
    public async Task ChangeFavorites(long aid,IEnumerable<long> add,IEnumerable<long> remove,CancellationToken ct=default)=>_ = await Post("favorite-change","/x/v3/fav/resource/deal",new(){{"rid",aid.ToString()},{"type","2"},{"add_media_ids",string.Join(',',add)},{"del_media_ids",string.Join(',',remove)}},ct);
    public async Task CreateFavoriteFolder(string title,bool isPublic=false,CancellationToken ct=default)=>_ = await Post("favorite-create","/x/v3/fav/folder/add",new(){{"title",title},{"privacy",isPublic?"0":"1"}},ct);
    public async Task RenameFavoriteFolder(long id,string title,bool isPublic,CancellationToken ct=default)
    {
        var info=(await Request("favorite-info","https://api.bilibili.com/x/v3/fav/folder/info?media_id="+id,true,ct))["data"];
        _=await Post("favorite-rename","/x/v3/fav/folder/edit",new(){{"media_id",id.ToString()},{"title",title},{"privacy",isPublic?"0":"1"},{"intro",info.Str("intro")},{"cover",info.Str("cover")}},ct);
    }
    public async Task DeleteFavoriteFolder(long id,CancellationToken ct=default)=>_ = await Post("favorite-delete","/x/v3/fav/folder/del",new(){{"media_ids",id.ToString()}},ct);
    public async Task SetFollow(long mid,bool follow,CancellationToken ct=default)=>_ = await Post("follow-change","/x/relation/modify",new(){{"fid",mid.ToString()},{"act",follow?"1":"2"},{"re_src","11"}},ct);
    public async Task<bool> IsFollowing(long mid,CancellationToken ct=default){if(!HasAccountCookie)return false;var r=await Request("follow-state","https://api.bilibili.com/x/relation?fid="+mid,true,ct);return r["data"].Num("attribute") is 2 or 6;}
    public async Task<CommentSubmission> SendComment(long aid,string message,long root=0,long parent=0,CancellationToken ct=default,string bvid="",CommentDraft? draft=null,int type=1)
    {
        if(string.IsNullOrWhiteSpace(message)&&draft?.ImagePaths?.Count is not >0)throw new ArgumentException("请填写评论或添加图片");
        if(draft?.ImagePaths?.Count>9)throw new ArgumentException("最多添加 9 张图片");
        if(!HasAccountCookie||string.IsNullOrEmpty(Csrf))throw new InvalidOperationException("请先扫码登录");
        if(message.Length>1000)throw new ArgumentException("评论不能超过 1000 字");
        var post=new CommentPost(aid,bvid,message,root,parent,draft,type);
        var response=await SubmitNativeComment(post,ct);
        if(response.Num("code") is 12015 or 12074 or 61001 or -352)return new(0,null,CommentPublication.VerificationRequired,"B站要求验证，本次发布未确认；评论草稿已保留",response["data"].Str("url"));
        if(response.Num("code")!=0)throw new BilibiliException((int)response.Num("code"),response.Str("message","评论发送失败"));
        return ParseCommentSubmission(response);
    }
    public async Task LikeComment(long aid,long rpid,bool liked,CancellationToken ct=default,int type=1)=>_ = await Post("comment-like","/x/v2/reply/action",new(){{"oid",aid.ToString()},{"type",type.ToString()},{"rpid",rpid.ToString()},{"action",liked?"1":"0"}},ct);
    public async Task<double> GetCoinBalance(CancellationToken ct=default)
    {
        var result=await Request("coin-balance","https://api.bilibili.com/x/web-interface/nav",true,ct);var data=result["data"];if(!data.Flag("isLogin"))throw new InvalidOperationException("请先登录");return data?["money"]?.GetValue<double>()??0;
    }
    public async Task<DanmakuItem> SendDanmaku(long aid,long cid,double time,string text,CancellationToken ct=default,int mode=1,int fontsize=25,uint color=0xFFFFFF)
    {
        if(aid<=0||cid<=0||!double.IsFinite(time)||time<0)throw new ArgumentException("无效的视频或弹幕时间");
        if(mode is not (1 or 4 or 5)||fontsize is <18 or >36||color>0xFFFFFF)throw new ArgumentException("无效的弹幕样式");
        if(string.IsNullOrWhiteSpace(text)||text.Length>100)throw new ArgumentException("弹幕长度应为 1–100 字");
        var response=await Post("danmaku-send","/x/v2/dm/post",new(){{"type","1"},{"oid",cid.ToString()},{"aid",aid.ToString()},{"msg",text},{"progress",((long)(time*1000)).ToString()},{"color",color.ToString()},{"fontsize",fontsize.ToString()},{"pool","0"},{"mode",mode.ToString()},{"rnd",(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()*1000).ToString()}},ct);
        var id=response["data"].Num("dmid");if(id<=0)id=response["data"].Num("dmid_str");
        if(id<=0)throw new InvalidDataException("弹幕发送状态暂无法确认，请勿重复发送");
        return new(id,time,mode,fontsize,color,text);
    }
    public async Task LikeDanmaku(long cid,long dmid,bool liked,CancellationToken ct=default)=>_ = await Post("danmaku-like","/x/v2/dm/thumbup/add",new(){{"oid",cid.ToString()},{"dmid",dmid.ToString()},{"op",liked?"1":"2"},{"platform","web_player"}},ct);
    public async Task ReportDanmaku(long cid,long dmid,int reason,string content,CancellationToken ct=default)
    {
        if(reason<1||reason>13)throw new ArgumentOutOfRangeException(nameof(reason));
        _=await Post("danmaku-report","/x/dm/report/add",new(){{"cid",cid.ToString()},{"dmid",dmid.ToString()},{"reason",reason.ToString()},{"content",content}},ct);
    }
    public async Task<FollowingPage> GetFollowing(string offset="",CancellationToken ct=default)
    {
        var r=await Request("following-feed","https://api.bilibili.com/x/polymer/web-dynamic/v1/feed/all?type=video&offset="+Uri.EscapeDataString(offset),true,ct);
        var d=r["data"];var items=(d?["items"]?.AsArray()??[]).Select(n=>{var a=n?["modules"]?["module_dynamic"]?["major"]?["archive"];var author=n?["modules"]?["module_author"];return new VideoCard(a.Str("bvid"),a.Num("aid"),a.Str("title"),ImageUrl(a.Str("cover")),author.Str("name"),author.Num("mid"),a?["stat"].Num("play")??0,a?["stat"].Num("danmaku")??0,ParseDuration(a.Str("duration_text")),DateLabel(author.Num("pub_ts")),PublishedAt:author.Num("pub_ts"),Followed:true);}).Where(v=>v.Bvid.Length>0).ToArray();
        return new(items,d.Str("offset"),d.Flag("has_more"));
    }
    public async Task<IReadOnlyList<DanmakuItem>> GetDanmaku(long cid,double duration,CancellationToken ct=default)
    {
        var result=new List<DanmakuItem>();
        for(int segment=1;segment<=Math.Max(1,(int)Math.Ceiling(duration/360));segment++)
        {
            var bytes=await RequestDanmakuSegmentBytes("danmaku-segment",$"https://api.bilibili.com/x/v2/dm/web/seg.so?type=1&oid={cid}&segment_index={segment}",true,ct).ConfigureAwait(false);
            result.AddRange(DanmakuProtocol.Parse(bytes));
        }
        return result.DistinctBy(d=>d.Id).OrderBy(d=>d.Time).ToArray();
    }
    private async Task<byte[]> RequestDanmakuSegmentBytes(string label,string url,bool useAccount,CancellationToken ct)
    {
        using var req=new HttpRequestMessage(HttpMethod.Get,url);var sw=captures.Enabled?Stopwatch.StartNew():null;
        using var res=await (useAccount?account:guest).SendAsync(req,ct).ConfigureAwait(false);var bytes=await res.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        if(sw!=null)
        {
            var wrapped=JsonSerializer.Serialize(new{format="protobuf",encoding="base64",data=Convert.ToBase64String(bytes)});
            await captures.RecordAsync(label,useAccount?"account":"guest",req,res,wrapped,sw.ElapsedMilliseconds,cookies:(useAccount?accountCookies:guestCookies).GetCookieHeader(req.RequestUri!)).ConfigureAwait(false);
        }
        res.EnsureSuccessStatusCode();return bytes;
    }
}






