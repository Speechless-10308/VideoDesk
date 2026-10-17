namespace VideoDesk.Core;

public sealed partial class BilibiliClient
{
    public async Task DeleteComment(long aid,long rpid,CancellationToken ct=default,int type=1)
        => _=await Post("comment-delete","/x/v2/reply/del",new(){{"oid",aid.ToString()},{"type",type.ToString()},{"rpid",rpid.ToString()}},ct);
    public async Task PinComment(long aid,long rpid,bool pinned,CancellationToken ct=default,int type=1)
        => _=await Post("comment-pin","/x/v2/reply/top",new(){{"oid",aid.ToString()},{"type",type.ToString()},{"rpid",rpid.ToString()},{"action",pinned?"1":"0"}},ct);
    public async Task HateComment(long aid,long rpid,bool hated,CancellationToken ct=default,int type=1)
        => _=await Post("comment-hate","/x/v2/reply/hate",new(){{"oid",aid.ToString()},{"type",type.ToString()},{"rpid",rpid.ToString()},{"action",hated?"1":"0"},{"statistics","{\"appId\":100,\"platform\":5}"}},ct);
    public async Task<CommentReportMetadata> GetCommentReportMetadata(long aid,CancellationToken ct=default,int type=1)
    {
        var r=await Request("comment-report-metadata",$"https://api.bilibili.com/x/v2/reply/report/metadata?oid={aid}&type={type}",true,ct);return ParseCommentReportMetadata(r);
    }
    public static CommentReportMetadata ParseCommentReportMetadata(System.Text.Json.Nodes.JsonNode r)
    {
        var d=r["data"];var reasons=new List<CommentReportReason>();
        foreach(var n in d?["reason_list"]?.AsArray()??[])reasons.Add(new((int)n.Num("tag_id"),n.Str("tag_name"),n.Flag("content_required")));
        if(reasons.Count==0)foreach(var group in d?["report_groups"]?.AsArray()??[])foreach(var n in group?["report_options"]?.AsArray()??[])reasons.Add(new((int)n.Num("value"),n.Str("label"),n.Flag("detailIsRequired"),group.Str("name")));
        if(reasons.Count==0)throw new InvalidDataException("没有读取到官网举报选项，请稍后重试");
        return new(reasons,d.Flag("can_delete"));
    }
    public async Task ReportComment(long aid,long rpid,int reason,string content,bool blacklist=false,bool delete=false,bool newest=false,CancellationToken ct=default,int type=1)
        => _=await Post("comment-report","/x/v2/reply/report",new(){{"oid",aid.ToString()},{"type",type.ToString()},{"rpid",rpid.ToString()},{"reason",reason.ToString()},{"content",content},{"add_blacklist",blacklist?"true":"false"},{"delete",delete?"true":"false"},{"ordering",newest?"time":"heat"},{"gaia_source","main_web"},{"statistics","{\"appId\":100,\"platform\":5}"}},ct);
    public async Task<bool> IsBlacklisted(long mid,CancellationToken ct=default)
    {
        var r=await Request("comment-blacklist-state",$"https://api.bilibili.com/x/relation?fid={mid}",true,ct);return r["data"].Num("attribute")==128;
    }
    public async Task SetBlacklist(long mid,bool blocked,CancellationToken ct=default)
        => _=await Post("comment-blacklist","/x/relation/modify?statistics=%7B%22appId%22%3A100%2C%22platform%22%3A5%7D",new(){{"fid",mid.ToString()},{"act",blocked?"5":"6"},{"re_src","15"},{"gaia_source","main_web"}},ct);
    public async Task<string> GetCommentFilterLink(long aid,CancellationToken ct=default,int type=1)
    {
        var r=await Request("comment-filter-link",$"https://api.bilibili.com/x/v2/reply/subject/description?oid={aid}&type={type}",true,ct);var link=ImageUrl(r["data"]?["base"].Str("web_up_filter_link")??"");
        if(!Uri.TryCreate(link,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!uri.Host.EndsWith(".bilibili.com",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("官网没有提供当前账号的评论屏蔽词设置入口");return link;
    }
}


