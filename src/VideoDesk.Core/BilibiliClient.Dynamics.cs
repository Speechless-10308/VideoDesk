using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VideoDesk.Core;

public sealed partial class BilibiliClient
{
    public async Task<CreatorDynamic> GetDynamicDetail(string id,CancellationToken ct=default)
    {
        if(!long.TryParse(id,out var value)||value<=0)throw new InvalidDataException("动态 ID 无效");
        var result=await Request("dynamic-detail","https://api.bilibili.com/x/polymer/web-dynamic/v1/detail?id="+id+"&timezone_offset=-480&features=itemOpusStyle,opusBigCover,ugcDelete,commentsNewVersion",true,ct);
        return ParseCreatorDynamic(result["data"]?["item"]??throw new InvalidDataException("动态不存在或已删除"));
    }
    private async Task<JsonNode> NativeJsonPost(string label,string path,object data,CancellationToken ct)
    {
        if(!HasAccountCookie||Csrf.Length==0)throw new InvalidOperationException("请先扫码登录");await mutationLock.WaitAsync(ct);
        try
        {
            var body=JsonSerializer.Serialize(data);var separator=path.Contains('?')?'&':'?';using var req=new HttpRequestMessage(HttpMethod.Post,"https://api.bilibili.com"+path+separator+"csrf="+Uri.EscapeDataString(Csrf)){Content=new StringContent(body,Encoding.UTF8,"application/json")};NativeHeaders(req);
            using var response=await account.SendAsync(req,ct);var text=await response.Content.ReadAsStringAsync(ct);if(captures.Enabled)await captures.RecordAsync(label,"account",req,response,text,0,body,accountCookies.GetCookieHeader(req.RequestUri!));response.EnsureSuccessStatusCode();var result=JsonNode.Parse(text)??throw new InvalidDataException("响应为空");if(result.Num("code")!=0)throw new BilibiliException((int)result.Num("code"),result.Str("message"));return result;
        }
        catch(TaskCanceledException) when(!ct.IsCancellationRequested){throw new TimeoutException("请求未确认，请刷新状态后再决定是否重试");}
        finally{mutationLock.Release();}
    }
    public async Task LikeDynamic(string id,bool liked,CancellationToken ct=default)=>_ = await NativeJsonPost("dynamic-like","/x/dynamic/feed/dyn/thumb",new{dyn_id_str=id,up=liked?1:2,spmid="333.1369.0.0",from_spmid="333.999.0.0"},ct);
    public async Task RepostDynamic(string id,long mid,string message,CancellationToken ct=default)
    {
        await PrepareNativeSession(ct);
        _ = await NativeJsonPost("dynamic-repost","/x/dynamic/feed/create/dyn?platform=web&x-bili-device-req-json="+Uri.EscapeDataString("{\"platform\":\"web\",\"device\":\"pc\"}")+"&x-bili-web-req-json="+Uri.EscapeDataString("{\"spm_id\":\"333.999\"}"),
            new{dyn_req=new{content=new{contents=new[]{new{raw_text=message,type=1,biz_id=""}}},scene=4,upload_id=$"{mid}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{Random.Shared.Next(1000,10000)}",meta=new{app_meta=new{from="create.dynamic.web",mobi_app="web"}}},web_repost_src=new{dyn_id_str=id}},ct);
    }
}
