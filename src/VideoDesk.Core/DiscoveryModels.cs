namespace VideoDesk.Core;

public enum PopularCategory { General, Weekly, Precious, Ranking, Music }
public sealed record PopularPeriod(int Id,string Label,string Subject="",long PublishedAt=0);
public sealed record PopularEntry(VideoCard Video,string HotComment="",string Recommendation="",int Rank=0,string Url="",string MusicTitle="",long Heat=0);
public sealed record PopularPage(IReadOnlyList<PopularEntry> Items,bool HasMore=false,string Description="");
