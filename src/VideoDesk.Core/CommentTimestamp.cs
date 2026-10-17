using System.Globalization;
using System.Text.RegularExpressions;

namespace VideoDesk.Core;

public readonly record struct CommentTimestamp(int Index,int Length,long Seconds,int? Page);

public static partial class CommentTimestamps
{
    [GeneratedRegex(@"(?<![0-9])(?<![0-9][:：#])(?:[0-9]+#)?(?:[0-9]+[:：])?[0-9]+[:：][0-9]+(?![0-9:：#])",RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
    [GeneratedRegex(@"^(?:(?<h>[0-9]+)h)?(?:(?<m>[0-9]+)m)?(?:(?<s>[0-9]+(?:\.[0-9]+)?)s)?$",RegexOptions.CultureInvariant|RegexOptions.IgnoreCase)]
    private static partial Regex QueryPattern();

    public static bool TryParseQuery(string text,out double seconds)
    {
        if(double.TryParse(text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out seconds))return double.IsFinite(seconds)&&seconds>=0;
        if(TryParse(text,out var value,out var page)&&page==null){seconds=value;return true;}
        var match=QueryPattern().Match(text);seconds=0;if(!match.Success||text.Length==0)return false;
        foreach(var (key,multiplier) in new[]{("h",3600), ("m",60), ("s",1)})
            if(match.Groups[key].Success){if(!double.TryParse(match.Groups[key].Value,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var component))return false;seconds+=component*multiplier;}
        return double.IsFinite(seconds);
    }

    public static IEnumerable<CommentTimestamp> Find(string text)
    {
        foreach(Match match in Pattern().Matches(text))
            if(TryParse(match.Value,out var seconds,out var page))yield return new(match.Index,match.Length,seconds,page);
    }

    public static bool TryParse(string text,out long seconds,out int? page)
    {
        seconds=0;page=null;var hash=text.IndexOf('#');
        if(hash>=0){if(!int.TryParse(text.AsSpan(0,hash),NumberStyles.None,CultureInfo.InvariantCulture,out var p)||p<1)return false;page=p;text=text[(hash+1)..];}
        var pieces=text.Split([':', '：']);if(pieces.Length is not (2 or 3))return false;
        var values=new long[pieces.Length];for(int i=0;i<pieces.Length;i++)if(!long.TryParse(pieces[i],NumberStyles.None,CultureInfo.InvariantCulture,out values[i]))return false;
        if(values[^1]>=60||pieces.Length==3&&values[1]>=60)return false;
        try{checked{foreach(var value in values)seconds=seconds*60+value;}}catch(OverflowException){seconds=0;return false;}
        return true;
    }
}
