using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Diagnostics;

namespace VideoDesk.Core;

public sealed partial class BilibiliClient
{
    private readonly SemaphoreSlim nativeSessionLock=new(1,1);
    private string activatedNativeIdentity="";
    private readonly Dictionary<string,JsonNode> uploadedCommentImages=[];
    private void NativeHeaders(HttpRequestMessage request,string bvid="")
    {
        request.Headers.UserAgent.Clear();request.Headers.UserAgent.ParseAdd("VideoDesk/0.4.6 (.NET; Windows x64)");
        request.Headers.Referrer=new Uri(bvid.Length>0?"https://www.bilibili.com/video/"+Uri.EscapeDataString(bvid)+"/":"https://www.bilibili.com/");
        request.Headers.TryAddWithoutValidation("env","prod");request.Headers.TryAddWithoutValidation("app-key","android64");request.Headers.TryAddWithoutValidation("x-bili-aurora-zone","sh001");
        var mid=accountCookies.GetCookies(new Uri("https://api.bilibili.com/")).Cast<Cookie>().FirstOrDefault(c=>c.Name=="DedeUserID")?.Value??"";
        if(mid.Length==0)return;request.Headers.TryAddWithoutValidation("x-bili-mid",mid);
        var bytes=Encoding.ASCII.GetBytes(mid);const string key="ad1va46a7lza";for(int i=0;i<bytes.Length;i++)bytes[i]^=(byte)key[i%key.Length];
        request.Headers.TryAddWithoutValidation("x-bili-aurora-eid",Convert.ToBase64String(bytes).TrimEnd('='));
    }
    private async Task PrepareNativeSession(CancellationToken ct)
    {
        if(!HasAccountCookie||Csrf.Length==0)throw new InvalidOperationException("请先扫码登录");
        await EnsureVisitor(true,ct);await nativeSessionLock.WaitAsync(ct);
        try
        {
            var cookies=accountCookies.GetCookies(new Uri("https://api.bilibili.com/")).Cast<Cookie>();
            var identity=string.Join("|",cookies.Where(c=>c.Name is "buvid3" or "DedeUserID").OrderBy(c=>c.Name).Select(c=>c.Value));
            if(identity==activatedNativeIdentity)return;
            // Native client initialization, matching the endpoint used by
            // PiliPlus. No website script, browser, or synthetic canvas is used.
            var payload=JsonSerializer.Serialize(new Dictionary<string,object>{{"3064",1},{"39c8","333.1387.fp.risk"},{"3c43",new Dictionary<string,string>{{"adca","Win32"}}}});
            using var req=new HttpRequestMessage(HttpMethod.Post,"https://api.bilibili.com/x/internal/gaia-gateway/ExClimbWuzhi"){Content=new StringContent(JsonSerializer.Serialize(new{payload}),Encoding.UTF8,"application/json")};NativeHeaders(req);
            var body=captures.Enabled?await req.Content.ReadAsStringAsync(ct):null;using var response=await account.SendAsync(req,ct);var text=await response.Content.ReadAsStringAsync(ct);
            if(captures.Enabled)await captures.RecordAsync("native-session-init","account",req,response,text,0,body,accountCookies.GetCookieHeader(req.RequestUri!));response.EnsureSuccessStatusCode();
            var result=JsonNode.Parse(text)??throw new InvalidDataException("会话初始化响应为空");if(result.Num("code")!=0)throw new BilibiliException((int)result.Num("code"),result.Str("message","B站会话初始化失败"));
            activatedNativeIdentity=identity;PersistDeviceCookies(true);
        }
        finally{nativeSessionLock.Release();}
    }
    private async Task<JsonNode> SubmitNativeComment(CommentPost post,CancellationToken ct)
    {
        await PrepareNativeSession(ct);
        var pictures=new List<JsonNode>();foreach(var path in post.Draft?.ImagePaths??[])pictures.Add(await UploadCommentImage(path,ct));
        return await Post("comment-send-native","/x/v2/reply/add",BuildCommentValues(post,pictures),ct,r=>NativeHeaders(r,post.Bvid),true);
    }
    public static Dictionary<string,string> BuildCommentValues(CommentPost post,IReadOnlyList<JsonNode>? pictures=null)
    {
        // PiliPlus's replyAdd is a native form request. In particular it doesn't
        // claim main_web or invoke the website's fingerprint signing pipeline.
        var values=new Dictionary<string,string>{{"type",post.Type.ToString()},{"oid",post.Aid.ToString()},{"message",post.Message}};
        if(post.Root>0){values["root"]=post.Root.ToString();values["parent"]=(post.Parent>0?post.Parent:post.Root).ToString();}
        var mentions=post.Draft?.Mentions?.Where(p=>post.Message.Contains("@"+p.Key,StringComparison.Ordinal)).ToDictionary(p=>p.Key,p=>p.Value);
        if(mentions?.Count>0)values["at_name_to_mid"]=JsonSerializer.Serialize(mentions);
        if(pictures?.Count>0)values["pictures"]=JsonSerializer.Serialize(pictures);
        if(post.Draft?.SyncToDynamic==true)values["sync_to_dynamic"]="1";
        return values;
    }
    private async Task<JsonNode> UploadCommentImage(string path,CancellationToken ct)
    {
        var info=new FileInfo(path);if(!info.Exists||info.Length>10*1024*1024)throw new ArgumentException("图片不存在或超过 10 MB，请重新选择");
        var key=info.FullName+"|"+info.Length+"|"+info.LastWriteTimeUtc.Ticks;
        if(uploadedCommentImages.TryGetValue(key,out var cached))return cached.DeepClone();
        var mime=info.Extension.ToLowerInvariant() switch{".png"=>"image/png",".jpg" or ".jpeg"=>"image/jpeg",".gif"=>"image/gif",_=>throw new ArgumentException("请选择 JPG、PNG 或 GIF 图片")};
        var bytes=await File.ReadAllBytesAsync(path,ct);using var form=new MultipartFormDataContent();var image=new ByteArrayContent(bytes);image.Headers.ContentType=new MediaTypeHeaderValue(mime);form.Add(image,"file_up",info.Name);form.Add(new StringContent("daily"),"category");form.Add(new StringContent("new_dyn"),"biz");form.Add(new StringContent(Csrf),"csrf");
        using var req=new HttpRequestMessage(HttpMethod.Post,"https://api.bilibili.com/x/dynamic/feed/draw/upload_bfs"){Content=form};NativeHeaders(req);var watch=captures.Enabled?Stopwatch.StartNew():null;using var response=await account.SendAsync(req,ct);var text=await response.Content.ReadAsStringAsync(ct);
        if(watch!=null)await captures.RecordAsync("comment-image-upload","account",req,response,text,watch.ElapsedMilliseconds,JsonSerializer.Serialize(new{format="multipart",file=info.Name,category="daily",biz="new_dyn",csrf=Csrf,imageBase64=Convert.ToBase64String(bytes)}),accountCookies.GetCookieHeader(req.RequestUri!));response.EnsureSuccessStatusCode();
        var result=JsonNode.Parse(text)??throw new InvalidDataException("图片上传响应为空");if(result.Num("code")!=0)throw new BilibiliException((int)result.Num("code"),result.Str("message","图片上传失败"));
        var d=result["data"];if(d.Str("image_url").Length==0)throw new InvalidDataException("没有收到图片地址，评论尚未发送");
        var picture=new JsonObject{["img_src"]=ImageUrl(d.Str("image_url")),["img_width"]=d.Num("image_width"),["img_height"]=d.Num("image_height"),["img_size"]=d.Real("img_size"),["ai_gen_pic"]=d?["ai_gen_pic"]?.DeepClone()??JsonValue.Create(0)};
        if(uploadedCommentImages.Count>=18)uploadedCommentImages.Clear();uploadedCommentImages[key]=picture;return picture.DeepClone();
    }
    public async Task<IReadOnlyList<EmojiPackage>> GetCommentEmojis(CancellationToken ct=default)
    {
        var r=await Request("comment-emoji-panel","https://api.bilibili.com/x/emote/user/panel/web?business=reply",true,ct);
        return (r["data"]?["packages"]?.AsArray()??[]).Select(p=>new EmojiPackage(p.Num("id"),p.Str("text"),ImageUrl(p.Str("url")),
            (p?["emote"]?.AsArray()??[]).Select(e=>new EmojiChoice(e.Str("text"),ImageUrl(e.Str("url")),(int)(e?["meta"].Num("size")??1),e?["flags"].Flag("no_access")!=true,e.Num("type")==4)).ToArray())).ToArray();
    }
    public async Task<IReadOnlyList<MentionUser>> SearchMentions(string keyword,CancellationToken ct=default)
    {
        var r=await Request("comment-mentions","https://api.bilibili.com/x/polymer/web-dynamic/v1/mention/search?keyword="+Uri.EscapeDataString(keyword),true,ct);
        return (r["data"]?["groups"]?.AsArray()??[]).SelectMany(g=>(g?["items"]?.AsArray()??[]).Select(u=>new MentionUser(u.Num("uid"),u.Str("name"),ImageUrl(u.Str("face")),g.Str("group_name")))).DistinctBy(u=>u.Mid).ToArray();
    }
}
