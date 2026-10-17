namespace VideoDesk.Core;

public sealed record VideoStoryboard(int Columns,int Rows,int TileWidth,int TileHeight,IReadOnlyList<string> Images,IReadOnlyList<double> Times)
{
    public int FrameAt(double seconds)
    {
        int low=0,high=Times.Count-1;
        while(low<=high){var middle=low+(high-low)/2;if(Times[middle]<=seconds)low=middle+1;else high=middle-1;}
        return Math.Clamp(high,0,Math.Max(0,Times.Count-1));
    }
}
public record WatchLaterItem(VideoCard Video,long Cid,double Progress,bool Unavailable=false,long AddedAt=0);
public record WatchLaterPage(IReadOnlyList<WatchLaterItem> Items,long Total,bool HasMore);
