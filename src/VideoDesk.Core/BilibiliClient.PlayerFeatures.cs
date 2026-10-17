using System.Globalization;
using System.Text.Json.Nodes;

namespace VideoDesk.Core;

public sealed partial class BilibiliClient
{
    public async Task<IReadOnlyList<VideoChapter>> GetVideoChapters(string bvid,long cid,CancellationToken ct=default)
    {
        var response=await Request("video-chapters",$"https://api.bilibili.com/x/player/v2?bvid={Uri.EscapeDataString(bvid)}&cid={cid}",true,ct,referer:"https://www.bilibili.com/video/"+bvid).ConfigureAwait(false);
        return ParseVideoChapters(response);
    }
    public static IReadOnlyList<VideoChapter> ParseVideoChapters(JsonNode response)
        => (response["data"]?["view_points"]?.AsArray()??[]).Where(p=>p is JsonObject&&p.Num("type")==2)
            .Select(p=>new VideoChapter(p.Real("from"),p.Real("to"),p.Str("content"),ImageUrl(p.Str("imgUrl"))))
            .Where(p=>double.IsFinite(p.Start)&&double.IsFinite(p.End)&&p.Start>=0&&p.End>p.Start&&p.Title.Length>0)
            .OrderBy(p=>p.Start).DistinctBy(p=>p.Start).Take(500).ToArray();

    public async Task<IReadOnlyList<VideoInteraction>> GetVideoInteractions(long aid,long cid,CancellationToken ct=default)
    {
        var bytes=await RequestDanmakuSegmentBytes("player-interactions",$"https://api.bilibili.com/x/v2/dm/web/view?type=1&oid={cid}&pid={aid}",true,ct).ConfigureAwait(false);
        return VideoPlayerFeatures.ParseInteractions(bytes);
    }

    public async Task SubmitVideoInteraction(long aid,long cid,VideoInteraction item,int selection,double seconds,CancellationToken ct=default)
    {
        if(aid<=0||cid<=0||item.Id<=0||!double.IsFinite(seconds)||seconds<0||seconds>604800)throw new ArgumentException("互动弹幕参数无效");
        var values=new Dictionary<string,string>{{"aid",aid.ToString()},{"cid",cid.ToString()},{"progress",((long)(seconds*1000)).ToString(CultureInfo.InvariantCulture)},
            {"cmd_id_str",item.CommandId},{"polaris_app_id","100"},{"polaris_platform","5"},{"spmid","333.788.0.0"},{"from_spmid",""}};
        if(item.Kind==VideoInteractionKind.Vote)
        {
            var option=item.Options.FirstOrDefault(o=>o.Id==selection)??throw new ArgumentOutOfRangeException(nameof(selection));
            values["vote_id"]=item.Id.ToString();values["vote_type"]=item.VoteType.ToString(CultureInfo.InvariantCulture);values["option_id"]=selection.ToString();values["has_self_def"]=option.HasSelfDefinition?"1":"0";
        }
        else
        {
            if(selection is <2 or >10||selection%2!=0)throw new ArgumentOutOfRangeException(nameof(selection));
            values["grade_id"]=item.Id.ToString();values["grade_score"]=selection.ToString();
        }
        _=await Post("player-interaction-submit","/x/v2/dm/command/"+(item.Kind==VideoInteractionKind.Vote?"vote":"grade")+"/post",values,ct).ConfigureAwait(false);
    }
}
