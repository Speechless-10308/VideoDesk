using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Diagnostics;
using System.Windows.Documents;
using MaterialDesignThemes.Wpf;

namespace VideoDesk.App;

internal sealed class SmoothScrollViewer : ScrollViewer
{
    private double target,offset,lastFrame;
    private readonly Stopwatch watch=Stopwatch.StartNew();
    private TimeSpan renderedAt=TimeSpan.MinValue;
    public bool IsWheelAnimating {get;private set;}
    public bool ShowBackToTop {get;set;}=true;
    private TopAdorner? top;
    private AdornerLayer? topLayer;
    public SmoothScrollViewer(){PanningMode=PanningMode.VerticalOnly;PanningDeceleration=.002;PanningRatio=1;Loaded+=(_,_)=>_ = Dispatcher.BeginInvoke(()=>{if(IsLoaded&&ShowBackToTop&&top==null&&AdornerLayer.GetAdornerLayer(this) is {} layer){top=new(this);topLayer=layer;layer.Add(top);UpdateBackToTop();}});IsVisibleChanged+=(_,_)=>{if(!IsVisible)Stop();UpdateBackToTop();};Unloaded+=(_,_)=>{Stop();if(top!=null){topLayer?.Remove(top);topLayer=null;top=null;}};ScrollChanged+=(_,_)=>UpdateBackToTop();PreviewMouseLeftButtonDown+=(_,_)=>Stop();}
    private void UpdateBackToTop(){if(top!=null)top.Button.Visibility=IsVisible&&ShowBackToTop&&VerticalOffset>350?Visibility.Visible:Visibility.Collapsed;}
    public new void ScrollToTop(){Stop();base.ScrollToTop();}
    internal void ShiftOffset(double delta){target+=delta;offset+=delta;ScrollToVerticalOffset(Math.Max(0,VerticalOffset+delta));}
    private void Stop(){CompositionTarget.Rendering-=Frame;IsWheelAnimating=false;}
    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        // Preview events reach the outer viewer first. Give an inner list or
        // viewer its wheel while it can scroll; pass through at its boundaries.
        for(var node=e.OriginalSource as DependencyObject;node!=null&&node!=this;node=node is Visual or System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))
            if(node is ScrollViewer inner&&inner.ScrollableHeight>0&&
               (e.Delta>0&&inner.VerticalOffset>0||e.Delta<0&&inner.VerticalOffset<inner.ScrollableHeight))
            {base.OnPreviewMouseWheel(e);return;}
        if(ScrollableHeight<=0||e.Delta>0&&VerticalOffset<=0||e.Delta<0&&VerticalOffset>=ScrollableHeight){base.OnPreviewMouseWheel(e);return;}
        e.Handled=true;
        if(!IsWheelAnimating){target=offset=VerticalOffset;lastFrame=watch.Elapsed.TotalSeconds;renderedAt=TimeSpan.MinValue;IsWheelAnimating=true;CompositionTarget.Rendering+=Frame;}
        target=Math.Clamp(target-e.Delta*1.15,0,ScrollableHeight);
    }
    private void Frame(object? sender,EventArgs e)
    {
        if(e is RenderingEventArgs rendered){if(rendered.RenderingTime==renderedAt)return;renderedAt=rendered.RenderingTime;}
        var now=watch.Elapsed.TotalSeconds;var dt=Math.Max(0,now-lastFrame);lastFrame=now;
        target=Math.Clamp(target,0,ScrollableHeight);offset+=(target-offset)*(1-Math.Exp(-dt/.035));
        if(Math.Abs(target-offset)<.1){ScrollToVerticalOffset(target);Stop();}else ScrollToVerticalOffset(offset);
    }
    private sealed class TopAdorner : Adorner
    {
        private readonly VisualCollection children;
        internal Button Button {get;}
        public TopAdorner(SmoothScrollViewer viewer):base(viewer)
        {
            children=new(this);Button=new Button{Style=(Style)Application.Current.FindResource("PlainButton"),Content=new PackIcon{Kind=PackIconKind.ArrowUp,Width=22,Height=22},Width=42,Height=42,Padding=new Thickness(0),Background=new SolidColorBrush(Color.FromRgb(255,237,244)),Foreground=new SolidColorBrush(Color.FromRgb(255,71,140)),ToolTip="返回顶部",Visibility=Visibility.Collapsed};Button.Click+=(_,_)=>viewer.ScrollToTop();children.Add(Button);
        }
        protected override int VisualChildrenCount=>children.Count;
        protected override Visual GetVisualChild(int index)=>children[index];
        protected override Size MeasureOverride(Size available){Button.Measure(new Size(42,42));return AdornedElement.RenderSize;}
        protected override Size ArrangeOverride(Size finalSize){Button.Arrange(new Rect(Math.Max(0,finalSize.Width-62),Math.Max(0,finalSize.Height-62),42,42));return finalSize;}
    }
}


