using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace VideoDesk.App;

// A small, original flower spinner inspired by expressive loading shapes.
// Only render transforms animate; it never re-measures a list or waits for a full cycle.
internal sealed class LoadingGlyph : Grid
{
    private readonly RotateTransform rotation=new();
    private readonly ScaleTransform breathing=new(1,1);
    public bool Animating {get;private set;}
    public LoadingGlyph(Brush color)
    {
        Width=32;Height=32;IsHitTestVisible=false;
        var flower=new Grid{Width=32,Height=32,RenderTransformOrigin=new(.5,.5)};
        for(int i=0;i<5;i++)
        {
            var petal=new Ellipse{Width=13,Height=19,Fill=color,Margin=new(0,0,0,10),VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Center,RenderTransformOrigin=new(.5,16d/19),RenderTransform=new RotateTransform(i*72)};
            flower.Children.Add(petal);
        }
        var transforms=new TransformGroup();transforms.Children.Add(rotation);transforms.Children.Add(breathing);flower.RenderTransform=transforms;Children.Add(flower);
        IsVisibleChanged+=(_,_)=>UpdateAnimation();Loaded+=(_,_)=>UpdateAnimation();Unloaded+=(_,_)=>Stop();
        System.Windows.Automation.AutomationProperties.SetName(this,"加载");
    }
    private void UpdateAnimation()
    {
        if(!IsVisible||!IsLoaded){Stop();return;}if(Animating||!SystemParameters.ClientAreaAnimation)return;Animating=true;
        rotation.BeginAnimation(RotateTransform.AngleProperty,new DoubleAnimation(0,360,TimeSpan.FromSeconds(2.4)){RepeatBehavior=RepeatBehavior.Forever});
        var pulse=new DoubleAnimation(.9,1.04,TimeSpan.FromSeconds(.8)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}};
        breathing.BeginAnimation(ScaleTransform.ScaleXProperty,pulse);breathing.BeginAnimation(ScaleTransform.ScaleYProperty,pulse);
    }
    private void Stop(){Animating=false;rotation.BeginAnimation(RotateTransform.AngleProperty,null);breathing.BeginAnimation(ScaleTransform.ScaleXProperty,null);breathing.BeginAnimation(ScaleTransform.ScaleYProperty,null);}
}

