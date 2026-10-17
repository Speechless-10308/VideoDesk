using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;
using VideoDesk.Core;

namespace VideoDesk.App;

internal sealed class DanmakuMaskController : IDisposable
{
    private readonly BilibiliClient api;private readonly CancellationTokenSource lifetime;private DanmakuMaskInfo? info;private IReadOnlyList<DanmakuMaskIndex> index=[];private readonly Dictionary<int,IReadOnlyList<MaskFrame>> segments=[];private bool loading,disposed;private string? error;
    public bool Available=>info!=null&&index.Count>0;public string? Error=>error;
    public bool Initializing {get;private set;}=true;
    public event Action? StatusChanged;
    internal sealed record MaskFrame(double Time,Geometry Visible,double Width,double Height);
    public DanmakuMaskController(BilibiliClient api,CancellationToken ct){this.api=api;lifetime=CancellationTokenSource.CreateLinkedTokenSource(ct);}
    public async Task Initialize(long aid,long cid)
    {
        try{info=await api.GetDanmakuMaskInfo(aid,cid,lifetime.Token);if(info==null)return;var count=DanmakuMaskProtocol.EntryCount(await api.GetDanmakuMaskRange(info.Url,0,15,lifetime.Token));index=DanmakuMaskProtocol.ParseIndex(await api.GetDanmakuMaskRange(info.Url,16,16+count*16-1,lifetime.Token),count);}
        catch(OperationCanceledException){}catch(Exception e){error=e.Message;info=null;}finally{Initializing=false;StatusChanged?.Invoke();}
    }
    public MaskFrame? At(double time)
    {
        if(!Available||disposed)return null;int segment=0;for(int i=1;i<index.Count&&index[i].Time<=time;i++)segment=i;
        if(!segments.TryGetValue(segment,out var frames)){if(!loading)_=Load(segment);return null;}
        int low=0,high=frames.Count;while(low<high){int mid=(low+high)/2;if(frames[mid].Time<=time)low=mid+1;else high=mid;}return low>0?frames[low-1]:null;
    }
    private async Task Load(int segment)
    {
        loading=true;try{var bytes=await api.GetDanmakuMaskRange(info!.Url,index[segment].Offset,segment+1<index.Count?index[segment+1].Offset-1:null,lifetime.Token);var parsed=await Task.Run(()=>DanmakuMaskProtocol.ParseFrames(bytes).Select(f=>DecodeSvg(f.Svg,f.Time)).ToArray(),lifetime.Token);if(disposed)return;if(segments.Count>=2)segments.Remove(segments.Keys.First());segments[segment]=parsed;}
        catch(OperationCanceledException){}catch(Exception e){error=e.Message;info=null;StatusChanged?.Invoke();}finally{loading=false;}
    }
    internal static MaskFrame DecodeSvg(string svg,double time)
    {
        using var reader=XmlReader.Create(new StringReader(svg),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=1048576});var root=XDocument.Load(reader).Root??throw new InvalidDataException("空白遮罩 SVG");
        var view=Numbers(root.Attribute("viewBox")?.Value??"");double width=view.Length==4?view[2]:Number(root.Attribute("width")?.Value,320),height=view.Length==4?view[3]:Number(root.Attribute("height")?.Value,180);if(width<=0||height<=0)throw new InvalidDataException("遮罩尺寸无效");
        var visible=new GeometryGroup{FillRule=FillRule.Nonzero};
        foreach(var path in root.Descendants().Where(e=>e.Name.LocalName=="path"))
        {
            var data=path.Attribute("d")?.Value;if(string.IsNullOrWhiteSpace(data)||path.AncestorsAndSelf().Select(e=>e.Attribute("fill")?.Value).FirstOrDefault(v=>v!=null)=="none")continue;
            var shape=Geometry.Parse(data).Clone();if(path.AncestorsAndSelf().Select(e=>e.Attribute("fill-rule")?.Value).FirstOrDefault(v=>v!=null)!="evenodd"&&shape is PathGeometry pg)pg.FillRule=FillRule.Nonzero;
            var matrix=Matrix.Identity;
            foreach(var node in path.AncestorsAndSelf())
            {
                var nodeMatrix=Matrix.Identity;
                foreach(Match match in Regex.Matches(node.Attribute("transform")?.Value??"",@"(matrix|translate|scale|rotate)\s*\(([^)]+)\)"))
                {
                    var values=Numbers(match.Groups[2].Value);var next=Matrix.Identity;
                    switch(match.Groups[1].Value){case "matrix" when values.Length==6:next=new(values[0],values[1],values[2],values[3],values[4],values[5]);break;case "translate" when values.Length>=1:next.Translate(values[0],values.Length>1?values[1]:0);break;case "scale" when values.Length>=1:next.Scale(values[0],values.Length>1?values[1]:values[0]);break;case "rotate" when values.Length>=3:next.RotateAt(values[0],values[1],values[2]);break;case "rotate" when values.Length>=1:next.Rotate(values[0]);break;}
                    // SVG's transform list uses column vectors; WPF uses row vectors.
                    nodeMatrix.Prepend(next);
                }
                matrix.Append(nodeMatrix);
            }
            if(view.Length==4)matrix.Translate(-view[0],-view[1]);shape.Transform=new MatrixTransform(matrix);visible.Children.Add(shape);
        }
        visible.Freeze();return new(time,visible,width,height);
    }
    private static double[] Numbers(string s)=>Regex.Matches(s,@"[-+]?(?:\d*\.\d+|\d+)(?:[eE][-+]?\d+)?").Select(m=>double.Parse(m.Value,CultureInfo.InvariantCulture)).ToArray();
    private static double Number(string? s,double fallback){var numbers=Numbers(s??"");return numbers.Length>0?numbers[0]:fallback;}
    public void Dispose(){disposed=true;lifetime.Cancel();lifetime.Dispose();segments.Clear();}
}
