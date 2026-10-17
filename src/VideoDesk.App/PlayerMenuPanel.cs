using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VideoDesk.App;

// A child of the player surface, so its bounds cannot escape into the desktop.
internal sealed class PlayerMenuPanel : Grid
{
    private readonly Border card;
    private readonly double desiredWidth,desiredHeight,controlsHeight;
    private readonly Func<Rect> getAnchor;
    private readonly FrameworkElement body;
    private readonly bool nestedScroll;
    public Rect CardBounds=>new(card.Margin.Left,card.Margin.Top,card.ActualWidth,card.ActualHeight);
    public PlayerMenuPanel(FrameworkElement body,Brush background,Brush outline,Point anchor,double controlsHeight,Action close)
        :this(body,background,outline,()=>new Rect(anchor,new Size(1,1)),controlsHeight,close){}
    public PlayerMenuPanel(FrameworkElement body,Brush background,Brush outline,Func<Rect> getAnchor,double controlsHeight,Action close)
    {
        this.getAnchor=getAnchor;this.controlsHeight=controlsHeight;this.body=body;nestedScroll=body is DockPanel or ScrollViewer;
        desiredWidth=double.IsNaN(body.Width)?330:body.Width;body.Measure(new Size(desiredWidth,double.PositiveInfinity));desiredHeight=double.IsNaN(body.Height)?Math.Min(580,body.DesiredSize.Height):body.Height;
        body.Width=double.NaN;body.Height=double.NaN;
        Background=Brushes.Transparent;
        card=new Border{Child=nestedScroll?body:new SmoothScrollViewer{ShowBackToTop=false,Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled},Background=background,BorderBrush=outline,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(16),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,ClipToBounds=true};
        Children.Add(card);Loaded+=(_,_)=>Fit();SizeChanged+=(_,_)=>Fit();LayoutUpdated+=(_,_)=>Fit();PreviewMouseLeftButtonDown+=(_,e)=>{if(ReferenceEquals(e.OriginalSource,this)){e.Handled=true;close();}};
    }
    private void Fit()
    {
        if(!IsLoaded||ActualWidth<1||ActualHeight<1)return;
        var width=Math.Max(1,Math.Min(desiredWidth+34,ActualWidth-24));
        var anchor=getAnchor();const double gap=8,padding=12;double height,y;
        if(anchor.Top>=padding&&anchor.Bottom<=ActualHeight&&anchor.Width>0)
        {
            var above=Math.Max(1,anchor.Top-gap-padding);var below=Math.Max(1,ActualHeight-anchor.Bottom-gap-padding);
            var useAbove=above>=desiredHeight+34||above>=below;height=Math.Max(1,Math.Min(desiredHeight+34,useAbove?above:below));
            y=useAbove?anchor.Top-gap-height:anchor.Bottom+gap;
        }
        else{height=Math.Max(1,Math.Min(desiredHeight+34,ActualHeight-controlsHeight-24));y=Math.Max(12,ActualHeight-controlsHeight-height-12);}
        card.Width=width;card.Height=height;
        if(nestedScroll){body.Width=Math.Max(1,width-34);body.Height=Math.Max(1,height-34);}
        card.Margin=new Thickness(Math.Clamp(anchor.Left+(anchor.Width-width)/2,12,Math.Max(12,ActualWidth-width-12)),y,0,0);
    }
}
