using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using VideoDesk.Core;

namespace VideoDesk.App;

// Geometry is retained between frames; position and real cache ranges only repaint small primitives.
internal sealed class PlaybackTimeline : Slider
{
    private VideoHeatmap? heatmap;
    private StreamGeometry? shape;
    private IReadOnlyList<(double Start,double End)> ranges=[];
    private double[] chapterBoundaries=[];
    private double? hoverX;
    private System.Windows.Shapes.Path? hoverMarker;
    private static Brush FrozenBrush(Color color){var brush=new SolidColorBrush(color);brush.Freeze();return brush;}
    private static readonly Brush BaseBrush=FrozenBrush(Color.FromArgb(75,255,255,255));
    private static readonly Brush BufferBrush=FrozenBrush(Color.FromArgb(210,225,230,239));
    private static readonly Brush HeatBrush=FrozenBrush(Color.FromArgb(110,255,255,255));
    public VideoHeatmap? Heatmap=>heatmap;
    public bool IsScrubbing {get;private set;}
    public event Action<double>? SeekRequested;
    internal Brush TrackBackground {get;set;}=BaseBrush;
    public IReadOnlyList<(double Start,double End)> BufferedRanges=>ranges;
    public PlaybackTimeline()
    {
        Style=null;Height=40;Background=Brushes.Transparent;SmallChange=5;LargeChange=10;
        Template=(ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Slider">
          <Grid Background="Transparent">
            <Track x:Name="PART_Track" Height="16" VerticalAlignment="Bottom" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" Value="{TemplateBinding Value}">
              <Track.DecreaseRepeatButton><RepeatButton Command="Slider.DecreaseLarge" Opacity="0" Focusable="False"/></Track.DecreaseRepeatButton>
              <Track.IncreaseRepeatButton><RepeatButton Command="Slider.IncreaseLarge" Opacity="0" Focusable="False"/></Track.IncreaseRepeatButton>
              <Track.Thumb><Thumb Width="14" Height="14"><Thumb.Template><ControlTemplate TargetType="Thumb"><Ellipse Fill="#FFFF408F"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
            </Track>
            <Canvas IsHitTestVisible="False"><Path x:Name="HoverMarker" Fill="{TemplateBinding Foreground}" /></Canvas>
          </Grid>
        </ControlTemplate>
        """);ValueChanged+=(_,_)=>InvalidateVisual();SizeChanged+=(_,_)=>Rebuild();
        Unloaded+=(_,_)=>CancelScrub();
        IsEnabledChanged+=(_,_)=>{if(!IsEnabled)CancelScrub();};
        IsVisibleChanged+=(_,_)=>{if(!IsVisible)CancelScrub();};
    }
    // Own the whole gesture. Slider's move-to-point class handler consumes the
    // down event before ordinary handlers can protect the value from polling.
    public void SetPlaybackPosition(double position){if(!IsScrubbing)Value=position;}
    public void CancelScrub()
    {
        IsScrubbing=false;
        if(IsMouseCaptured)ReleaseMouseCapture();
    }
    private void MoveTo(Point point)
    {
        // Use the drawn track's inset, independent of a pending thumb layout.
        var fraction=Math.Clamp((point.X-7)/Math.Max(1,ActualWidth-14),0,1);
        SetCurrentValue(ValueProperty,Minimum+fraction*(Maximum-Minimum));
    }
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if(!IsEnabled)return;
        Focus();IsScrubbing=true;
        if(!CaptureMouse()){IsScrubbing=false;return;}
        MoveTo(e.GetPosition(this));e.Handled=true;
    }
    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        if(IsScrubbing&&e.LeftButton==MouseButtonState.Pressed)MoveTo(e.GetPosition(this));
        base.OnPreviewMouseMove(e);
    }
    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if(!IsScrubbing){base.OnPreviewMouseLeftButtonUp(e);return;}
        MoveTo(e.GetPosition(this));var target=Value;CancelScrub();
        e.Handled=true;SeekRequested?.Invoke(target);
    }
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        var commit=IsScrubbing;IsScrubbing=false;base.OnLostMouseCapture(e);
        if(commit&&IsEnabled&&IsVisible)SeekRequested?.Invoke(Value);
    }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var target=e.Key switch
        {
            Key.Left or Key.Down=>Value-SmallChange,
            Key.Right or Key.Up=>Value+SmallChange,
            Key.PageDown=>Value-LargeChange,
            Key.PageUp=>Value+LargeChange,
            Key.Home=>Minimum,
            Key.End=>Maximum,
            _=>double.NaN
        };
        if(!IsEnabled||!double.IsFinite(target)){base.OnPreviewKeyDown(e);return;}
        SetCurrentValue(ValueProperty,Math.Clamp(target,Minimum,Maximum));
        e.Handled=true;SeekRequested?.Invoke(Value);
    }
    public void SetHeatmap(VideoHeatmap? value){heatmap=value;Rebuild();}
    public void SetBuffered(IReadOnlyList<(double Start,double End)> value){ranges=value;InvalidateVisual();}
    public void SetChapters(IReadOnlyList<VideoChapter> value){chapterBoundaries=value.Select(c=>c.Start).Where(t=>double.IsFinite(t)&&t>0).Distinct().Order().ToArray();InvalidateVisual();}
    public void SetHoverPosition(double? value){if(hoverX==value)return;hoverX=value;UpdateHoverMarker();}
    public override void OnApplyTemplate(){base.OnApplyTemplate();hoverMarker=GetTemplateChild("HoverMarker") as System.Windows.Shapes.Path;UpdateHoverMarker();}
    private void UpdateHoverMarker()
    {
        if(hoverMarker==null)return;
        if(hoverX is not {} hover){hoverMarker.Data=null;return;}
        var y=ActualHeight-8;var triangles=new StreamGeometry();using(var path=triangles.Open())
        {
            path.BeginFigure(new(hover-3,y-8),true,true);path.LineTo(new(hover+3,y-8),true,false);path.LineTo(new(hover,y-4),true,false);
            path.BeginFigure(new(hover-3,y+8),true,true);path.LineTo(new(hover+3,y+8),true,false);path.LineTo(new(hover,y+4),true,false);
        }triangles.Freeze();hoverMarker.Data=triangles;
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e){base.OnPropertyChanged(e);if(e.Property==MaximumProperty&&heatmap!=null)Rebuild();}
    private void Rebuild()
    {
        shape=null;if(heatmap is {Values.Count:>1} map&&map.Values.Max()>0&&Maximum>0)
        {
            var geometry=new StreamGeometry();using(var path=geometry.Open())
            {
                var width=Math.Max(1,ActualWidth-14);var baseline=ActualHeight-10;path.BeginFigure(new(7,baseline),true,true);var max=map.Values.Max();
                for(int i=0;i<map.Values.Count;i++)path.LineTo(new(7+Math.Clamp(i*map.StepSeconds/Maximum,0,1)*width,baseline-3-map.Values[i]/max*22),true,false);
                path.LineTo(new(Math.Min(ActualWidth-7,7+map.Values.Count*map.StepSeconds/Maximum*width),baseline),true,false);
            }
            geometry.Freeze();shape=geometry;
        }UpdateHoverMarker();InvalidateVisual();
    }
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);if(ActualWidth<15||Maximum<=0)return;var width=ActualWidth-14;var y=ActualHeight-8;
        if(shape!=null)drawing.DrawGeometry(HeatBrush,null,shape);
        for(int i=0;i<=chapterBoundaries.Length;i++)
        {
            double start=i==0?0:chapterBoundaries[i-1],end=i<chapterBoundaries.Length?Math.Min(Maximum,chapterBoundaries[i]):Maximum;
            double left=7+start/Maximum*width+(i>0?1.5:0),right=7+end/Maximum*width-(end<Maximum?1.5:0);
            if(right<=left)continue;
            var segment=new Rect(left,y-2,right-left,4);drawing.DrawRoundedRectangle(TrackBackground,null,segment,1,1);
            drawing.PushClip(new RectangleGeometry(segment));
            foreach(var (bufferStart,bufferEnd) in ranges){var x=7+Math.Clamp(bufferStart/Maximum,0,1)*width;var endX=7+Math.Clamp(bufferEnd/Maximum,0,1)*width;drawing.DrawRectangle(BufferBrush,null,new(x,y-2,Math.Max(0,endX-x),4));}
            drawing.DrawRectangle(Foreground,null,new(7,y-2,Math.Clamp(Value/Maximum,0,1)*width,4));drawing.Pop();
        }
    }
}
