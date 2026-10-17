using System.Globalization;
using VideoDesk.Core;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private VideoPart? CommentTimePart(CommentTimestamp timestamp)
    {
        if(dynamicDetail!=null||detail==null||part==null)return null;
        var selected=timestamp.Page is {} page?detail.Parts.FirstOrDefault(p=>p.Page==page):part;
        return selected is {Duration:>0}&&timestamp.Seconds<=selected.Duration?selected:null;
    }
    private async Task SeekCommentTime(string bvid,CommentTimestamp timestamp)
    {
        // Old comment documents must never seek a different video after navigation.
        if(detail?.Video.Bvid!=bvid||CommentTimePart(timestamp) is not {} selected)return;
        await SeekCommentPosition(selected,timestamp.Seconds);
    }
    private async Task SeekCommentPosition(VideoPart selected,double seconds)
    {
        if(dynamicDetail!=null||part==null||seconds<0||!double.IsFinite(seconds)||seconds>selected.Duration)return;
        if(selected.Cid!=part.Cid){await ChangePart(selected,seconds);return;}
        if(player?.Ready!=true)return;
        ApplyCommentSeek(seconds);
    }
    private void ApplyCommentSeek(double seconds)
    {
        if(player?.CanInteract!=true||part==null)return;var duration=player.Number("duration");
        if(seconds<0||seconds>(duration>0?duration:part.Duration))return;
        player.Command("seek",seconds.ToString(CultureInfo.InvariantCulture),"absolute+exact");player.Set("pause","no");
        if(seek!=null)seek.Value=seconds;
        RequestDanmakuWindow(seconds);overlay?.Danmaku.Wake();overlay?.Wake();
    }
}
