using System.Text.Json.Nodes;

namespace VideoDesk.Core;

public sealed partial class BilibiliClient
{
    public static UserIdentity ParseUserIdentity(JsonNode? user)
    {
        var official=user?["official"]??user?["official_verify"];var type=official?["type"] is {} number&&int.TryParse(number.ToString(),out var parsed)?parsed:-1;
        if(type is not (0 or 1))type=-1;var vip=user?["vip"];var label=vip?["label"];var active=vip.Flag("status")||vip.Flag("vipStatus");
        var text=label.Str("text");if(active&&text.Length==0)text=vip.Num("type")==2||vip.Num("vipType")==2?"年度大会员":"大会员";
        return new(type,official.Str("title",official.Str("desc")),official.Str("desc"),active,text,label.Str("text_color"),label.Str("bg_color"),ImageUrl(label.Str("img_label_uri_hans_static")),vip.Str("nickname_color"));
    }
}
