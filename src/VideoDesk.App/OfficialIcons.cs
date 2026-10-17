using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;

namespace VideoDesk.App;

internal static class OfficialIcons
{
    private static readonly Dictionary<string,ImageSource> cache=[];
    public static ImageSource Source(string name,bool monochrome=false)
    {
        var key=name+(monochrome?"-muted":"");if(cache.TryGetValue(key,out var source))return source;
        using var stream=Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Official/"+name+".json"))!.Stream;var config=JsonNode.Parse(stream)!;var paths=config["default"]!.AsArray();var colors=config["colors"]?.AsArray();var group=new DrawingGroup();
        for(int i=0;i<paths.Count;i++)
        {
            var geometry=Geometry.Parse(paths[i]!.ToString());geometry.Freeze();var color=monochrome?Color.FromRgb(148,153,160):ReadColor(colors?.ElementAtOrDefault(i)?.ToString()??"#000000ff");var brush=new SolidColorBrush(color);brush.Freeze();group.Children.Add(new GeometryDrawing(brush,null,geometry));
        }
        group.Freeze();var image=new DrawingImage(group);image.Freeze();cache[key]=image;return image;
    }
    private static Color ReadColor(string text)
    {
        if(text.Length==9&&text[0]=='#')return Color.FromArgb(byte.Parse(text[7..9],NumberStyles.HexNumber),byte.Parse(text[1..3],NumberStyles.HexNumber),byte.Parse(text[3..5],NumberStyles.HexNumber),byte.Parse(text[5..7],NumberStyles.HexNumber));return (Color)ColorConverter.ConvertFromString(text);
    }
    public static Image Level(int level,bool senior=false,double height=14)
    {
        var image=new Image{Source=Source(senior&&level==6?"lv6-flash":"lv"+level),Height=height,Width=senior&&level==6?height*2.55:height*2,Stretch=Stretch.Uniform,VerticalAlignment=VerticalAlignment.Center,Tag="official-level",ToolTip=senior?"LV6 · 硬核会员":"LV"+level};AutomationProperties.SetName(image,"哔哩哔哩等级 LV"+level+(senior?" 硬核会员":""));return image;
    }
}
