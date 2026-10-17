using System.Text.Json.Nodes;

namespace VideoDesk.Core;

public enum DiscoveryMode { Regular, Roaming }
public record VideoCard(string Bvid, long Aid, string Title, string Cover, string Creator, long Mid, long Views, long Danmaku, double Duration, string Published = "",int Level=-1,long PublishedAt=0,bool Followed=false,string LikeBadge="");
public record VideoPart(long Cid, int Page, string Title, double Duration);
public record VideoDetail(VideoCard Video, string Description, string Avatar, long Likes, long Coins, long Favorites, IReadOnlyList<VideoPart> Parts, int Copyright=1,IReadOnlyList<VideoContributor>? Contributors=null,VideoCollectionContext? Collection=null);
public record Quality(int Id, string Label);
public record PlayStream(string VideoUrl, string? AudioUrl, int Quality, IReadOnlyList<Quality> Qualities, string Codec, double Duration);
public record AccountInfo(bool LoggedIn, string Name, string Avatar, long Mid, bool Vip,int Level=-1,bool Senior=false,UserIdentity? Identity=null);
public record QrSession(string Url, string Key);
public record QrPoll(int Code, string Message);
public record CommentItem(string Name, string Message, long Likes, long Id=0, string Avatar="", long CreatedAt=0, long Mid=0, long ReplyCount=0, IReadOnlyList<CommentItem>? Replies=null, bool Liked=false,IReadOnlyDictionary<string,CommentEmote>? Emotes=null,int Level=-1,bool Senior=false,int State=0,IReadOnlyList<CommentPicture>? Pictures=null,IReadOnlyDictionary<string,CommentLink>? Links=null,CommentNote? Note=null,bool UpLiked=false,bool Pinned=false,bool Hated=false,UserIdentity? Identity=null);
public record CommentReportReason(int Id,string Name,bool DetailRequired=false,string Group="");
public record CommentReportMetadata(IReadOnlyList<CommentReportReason> Reasons,bool CanDelete);
public record CommentPicture(string Url,int Width,int Height,string Badge="");
public record CommentLink(string Title,string Url,string Icon="",bool MatchOnce=false);
public record CommentNote(string Url,string Kind);
public record VideoTag(long Id,string Name,string Kind="");
public record CommentEmote(string Url,int Size=1);
public record UserIdentity(int OfficialType=-1,string OfficialTitle="",string OfficialDescription="",bool IsVip=false,string VipLabel="",string VipTextColor="",string VipBackground="",string VipImage="",string NicknameColor="");
public record CreatorProfile(long Mid,string Name,string Avatar,string Banner,string Signature,long Followers,long Following,long Likes,bool Followed,int Level=-1,bool Senior=false,UserIdentity? Identity=null);
public record CreatorSummary(long Mid,string Name,string Avatar,long Followers,long Videos,int Level,bool Senior=false,UserIdentity? Identity=null);
public record VideoContributor(long Mid,string Name,string Avatar,string Role,int Level=-1);
public record VideoCollectionContext(long Id,long Mid,string Title,IReadOnlyList<VideoCard> Videos,long Total=0,bool Series=false);
public record VideoHeatmap(double StepSeconds,IReadOnlyList<double> Values);
public record OnlineViewers(string Count,string Total,bool Show);
public enum CommentPublication { Submitted,AwaitingReview,Visible,VerificationRequired }
public record CommentSubmission(long Id,CommentItem? Reply,CommentPublication Publication,string Message,string VerificationUrl="");
public record CommentDraft(IReadOnlyDictionary<string,long>? Mentions=null,IReadOnlyList<string>? ImagePaths=null,bool SyncToDynamic=false);
public record CommentPost(long Aid,string Bvid,string Message,long Root,long Parent,CommentDraft? Draft=null,int Type=1);
public record EmojiChoice(string Text,string Url,int Size,bool Accessible=true,bool IsText=false);
public record EmojiPackage(long Id,string Name,string Icon,IReadOnlyList<EmojiChoice> Items);
public record MentionUser(long Mid,string Name,string Avatar,string Group);
public record CreatorVideoPage(IReadOnlyList<VideoCard> Items,long Total,bool HasMore);
public record DynamicAttachment(string Title,string Description,string Cover,string Url,string Label="");
public record CreatorDynamic(string Id,string Text,string Date,IReadOnlyList<string> Pictures,VideoCard? Video=null,
    long Mid=0,string Name="",string Avatar="",string Action="",IReadOnlyDictionary<string,CommentEmote>? Emotes=null,
    IReadOnlyDictionary<string,CommentLink>? Links=null,CreatorDynamic? Original=null,string VideoDescription="",
    long Likes=0,long Comments=0,long Reposts=0,bool Liked=false,string Topic="",string TopicUrl="",DynamicAttachment? Attachment=null,bool Deleted=false,
    long CommentOid=0,int CommentType=17,bool CommentForbidden=false,bool ForwardForbidden=false,bool LikeForbidden=false,UserIdentity? Identity=null);
public record CreatorDynamicPage(IReadOnlyList<CreatorDynamic> Items,string Offset,bool HasMore);
public record CreatorCollection(long Id,string Title,string Cover,long Count,bool Series=false);
public record CommentPage(IReadOnlyList<CommentItem> Items, long Total, int Page, bool HasMore,long Next=0);
public record WatchProgress(string Bvid, long Cid, double Position, double Duration, DateTimeOffset WatchedAt, string Title, string Cover="", string Creator="", string Published="", bool Cloud=false, long Aid=0,long Mid=0,int Level=-1);
public record FollowingPage(IReadOnlyList<VideoCard> Items,string Offset,bool HasMore);
public record HistoryPage(IReadOnlyList<WatchProgress> Items, long Max, long ViewedAt, string Business, bool HasMore);
public record SearchTopic(string Keyword, string Icon="");
public record SearchPage(IReadOnlyList<VideoCard> Items,long Total,bool HasMore);
public record FavoriteFolder(long Id, string Title, long Count, bool Contains=false, bool Public=false);
public record FavoritePage(IReadOnlyList<VideoCard> Items, bool HasMore, long Total);
public record VideoAccountState(bool Liked, int Coins, bool Favorited);
public record DanmakuItem(long Id, double Time, int Mode, int FontSize, uint Color, string Text,int Weight=0,int Pool=0,uint Attributes=0,string SenderHash="");
public record DanmakuLikeState(bool Liked,long Likes);
public record DanmakuFilter(int Type,string Filter);
public record DanmakuMaskInfo(string Url,int Fps);
public record DanmakuMaskIndex(double Time,long Offset);
public record DanmakuMaskFrame(double Time,string Svg);

public sealed class BilibiliException(int code, string message) : Exception($"{message}（{code}）") { public int Code { get; } = code; }

internal static class JsonFields
{
    public static string Str(this JsonNode? n, string key, string fallback = "") => n?[key]?.ToString() ?? fallback;
    public static long Num(this JsonNode? n, string key) => long.TryParse(n?[key]?.ToString(), out var v) ? v : 0;
    public static double Real(this JsonNode? n, string key) => double.TryParse(n?[key]?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
    public static bool Flag(this JsonNode? n, string key) => n?[key]?.ToString() is "true" or "True" or "1";
}

