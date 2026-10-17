using System.Windows;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;

namespace VideoDesk.App;

// MaterialDesign's ripple animates only scale and opacity. Give it the same
// rounded bounds as the existing buttons, without adding layout animations.
public sealed class ButtonRipple : Ripple
{
    public static readonly DependencyProperty ClipRadiusProperty=DependencyProperty.Register(nameof(ClipRadius),typeof(double),typeof(ButtonRipple),new FrameworkPropertyMetadata(8d));
    public double ClipRadius {get=>(double)GetValue(ClipRadiusProperty);set=>SetValue(ClipRadiusProperty,value);}
    public static readonly DependencyProperty CornerRadiusProperty=DependencyProperty.Register(nameof(CornerRadius),typeof(CornerRadius),typeof(ButtonRipple),new FrameworkPropertyMetadata(new CornerRadius(0)));
    public CornerRadius CornerRadius {get=>(CornerRadius)GetValue(CornerRadiusProperty);set=>SetValue(CornerRadiusProperty,value);}
    public ButtonRipple(){Focusable=false;IsTabStop=false;}
    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);var radius=CornerRadius.TopLeft>0?CornerRadius.TopLeft:ClipRadius;var clip=new RectangleGeometry(new Rect(info.NewSize),radius,radius);clip.Freeze();Clip=clip;
    }
}
