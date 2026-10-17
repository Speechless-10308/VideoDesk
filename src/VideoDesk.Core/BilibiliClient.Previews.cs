using System.Text.Json.Nodes;
using System.Buffers.Binary;
using System.Text.Json;

namespace VideoDesk.Core;

public sealed partial class BilibiliClient
{
    private readonly object storyboardGate=new();
    private readonly WeightedLruCache<(string Bvid,long Cid),VideoStoryboard> storyboards=new(12,2*1048576);
    public async Task<VideoStoryboard?> GetVideoStoryboard(string bvid,long cid,CancellationToken ct=default)
    {
        if(string.IsNullOrWhiteSpace(bvid)||cid<=0)return null;
        ct.ThrowIfCancellationRequested();lock(storyboardGate)if(storyboards.TryGet((bvid,cid),out var cached))return cached;
        var response=await Request("video-storyboard",$"https://api.bilibili.com/x/player/videoshot?bvid={Uri.EscapeDataString(bvid)}&cid={cid}&index=1",true,ct,true,referer:"https://www.bilibili.com/video/"+bvid).ConfigureAwait(false);
        // Videos without generated storyboards commonly return -404.
        if(response.Num("code") is -404 or 62002)return null;
        if(response.Num("code")!=0)throw new BilibiliException((int)response.Num("code"),response.Str("message"));
        var result=ParseVideoStoryboard(response);
        if(result==null&&response["data"] is JsonObject data&&Uri.TryCreate(ImageUrl(data.Str("pvdata")),UriKind.Absolute,out var indexUrl)&&indexUrl.Scheme=="https")
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,indexUrl);request.Headers.Referrer=new Uri("https://www.bilibili.com/video/"+bvid);
            using var indexResponse=await guest.SendAsync(request,ct).ConfigureAwait(false);indexResponse.EnsureSuccessStatusCode();
            var bytes=await indexResponse.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            data["index"]=JsonSerializer.SerializeToNode(ParseStoryboardIndex(bytes));result=ParseVideoStoryboard(response);
        }
        if(result!=null)lock(storyboardGate)storyboards.Put((bvid,cid),result,result.Times.Count*8L+result.Images.Sum(u=>u.Length*2L));
        return result;
    }
    public static IReadOnlyList<double> ParseStoryboardIndex(byte[] bytes)
    {
        if(bytes.Length<4||bytes.Length%2!=0||bytes.Length>1048576)throw new InvalidDataException("预览索引长度无效");
        var times=new double[bytes.Length/2];for(int i=0;i<times.Length;i++){times[i]=BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(i*2,2));if(i>0&&times[i]<times[i-1])throw new InvalidDataException("预览索引顺序无效");}return times;
    }
    public static VideoStoryboard? ParseVideoStoryboard(JsonNode response)
    {
        var data=response["data"];int columns=(int)data.Num("img_x_len"),rows=(int)data.Num("img_y_len"),width=(int)data.Num("img_x_size"),height=(int)data.Num("img_y_size");
        if(columns is <1 or >20||rows is <1 or >20||width is <1 or >2048||height is <1 or >2048)return null;
        var urls=(data?["image"]?.AsArray()??[]).Select(n=>ImageUrl(n?.ToString()??"")).Where(u=>Uri.TryCreate(u,UriKind.Absolute,out var uri)&&uri.Scheme=="https").Take(256).ToArray();
        var times=(data?["index"]?.AsArray()??[]).Select(n=>double.TryParse(n?.ToString(),System.Globalization.NumberStyles.Any,System.Globalization.CultureInfo.InvariantCulture,out var t)?t:-1).ToArray();
        if(urls.Length==0||times.Length<2||times.Any(t=>t<0||!double.IsFinite(t))||!times.SequenceEqual(times.Order()))return null;
        // Official index starts with a sentinel, followed by the timestamp of each tile.
        return new(columns,rows,width,height,urls,times.Skip(1).Take(urls.Length*columns*rows).ToArray());
    }
    public async Task<WatchLaterPage> GetWatchLater(int page=1,CancellationToken ct=default)
    {
        if(!HasAccountCookie)throw new InvalidOperationException("请先扫码登录");
        var query=await Signed(new Dictionary<string,string>(){{"pn",Math.Max(1,page).ToString()},{"ps","20"},{"viewed","0"},{"key",""},{"asc","false"},{"need_split","true"},{"web_location","333.881"}},ct).ConfigureAwait(false);
        return ParseWatchLater(await Request("watch-later","https://api.bilibili.com/x/v2/history/toview/web?"+query,true,ct).ConfigureAwait(false),page);
    }
    public static WatchLaterPage ParseWatchLater(JsonNode response,int page=1)
    {
        var data=response["data"];var items=(data?["list"]?.AsArray()??[]).Where(n=>n!=null).Select(n=>new WatchLaterItem(Card(n),n.Num("cid"),n.Real("progress"),n.Num("state")<0||n.Str("bvid").Length==0||n.Num("cid")<=0,n.Num("add_at"))).ToArray();
        var total=data.Num("count");return new(items,total,items.Length>0&&page*20<total);
    }
    public async Task AddWatchLater(string bvid,long aid=0,CancellationToken ct=default)
    {
        if(string.IsNullOrWhiteSpace(bvid)&&aid<=0)throw new ArgumentException("缺少视频编号");
        var values=new Dictionary<string,string>();if(aid>0)values["aid"]=aid.ToString();else values["bvid"]=bvid;
        _=await Post("watch-later-add","/x/v2/history/toview/add",values,ct).ConfigureAwait(false);
    }
    public async Task RemoveWatchLater(long aid,CancellationToken ct=default)
    {
        if(aid<=0)throw new ArgumentOutOfRangeException(nameof(aid));
        _=await Post("watch-later-remove","/x/v2/history/toview/v2/dels",new(){{"resources",aid.ToString()}},ct).ConfigureAwait(false);
    }
}
