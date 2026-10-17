using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Runtime.CompilerServices;

namespace VideoDesk.App;

internal static class UiMotion
{
    internal const int DurationMs = 200;
    internal static bool Enabled => SystemParameters.ClientAreaAnimation;
    private static readonly ConditionalWeakTable<Animatable,Dictionary<DependencyProperty,int>> revisions = new();

    // M3 standard easing: cubic-bezier(0.2, 0, 0, 1).
    internal static void To(Animatable target, DependencyProperty property, double value, bool animate)
    {
        var from = (double)target.GetValue(property);
        var states=revisions.GetOrCreateValue(target);var revision=states.GetValueOrDefault(property)+1;states[property]=revision;
        target.BeginAnimation(property, null);
        if (!animate || !Enabled || Math.Abs(from - value) < .01) { target.SetValue(property,value);states.Remove(property);return; }
        // Keep the current value as the base until the new clock has ticked.
        // Otherwise a rapid reversal briefly exposes the new end position.
        target.SetValue(property,from);
        var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(DurationMs), FillBehavior = FillBehavior.HoldEnd };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(DurationMs)), new KeySpline(.2, 0, 0, 1)));
        animation.Completed+=(_,_)=>{if(states.GetValueOrDefault(property)!=revision)return;target.SetValue(property,value);target.BeginAnimation(property,null);states.Remove(property);};
        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    internal static void Reset(FrameworkElement element)
    {
        if(element.RenderTransform is TranslateTransform move)To(move,TranslateTransform.XProperty,0,false);
        element.BeginAnimation(UIElement.OpacityProperty,null);
        element.Opacity=1;
    }

    internal static void Exit(FrameworkElement element, Action completed)
    {
        if(!Enabled||!element.IsLoaded){completed();return;}
        var move=element.RenderTransform as TranslateTransform??new TranslateTransform();element.RenderTransform=move;
        To(move,TranslateTransform.XProperty,32,true);
        var opacity=element.Opacity;element.BeginAnimation(UIElement.OpacityProperty,null);element.Opacity=opacity;
        var fade=new DoubleAnimationUsingKeyFrames{Duration=TimeSpan.FromMilliseconds(DurationMs),FillBehavior=FillBehavior.HoldEnd};
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(opacity,KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new SplineDoubleKeyFrame(0,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(DurationMs)),new KeySpline(.4,0,1,1)));
        fade.Completed+=(_,_)=>completed();element.BeginAnimation(UIElement.OpacityProperty,fade,HandoffBehavior.SnapshotAndReplace);
    }

    internal static void Enter(FrameworkElement? element, int direction, double distance = 12, double opacity = .85)
    {
        if (element == null || direction == 0 || !Enabled) return;
        Reset(element);
        var move = new TranslateTransform(direction > 0 ? distance : -distance, 0);
        element.RenderTransform = move;
        To(move, TranslateTransform.XProperty, 0, true);
        // Keep the first frame readable; never replace existing content with a blank fade.
        var fade=new DoubleAnimation(opacity,1,TimeSpan.FromMilliseconds(DurationMs)){FillBehavior=FillBehavior.Stop};
        fade.Completed+=(_,_)=>{if(ReferenceEquals(element.RenderTransform,move))element.BeginAnimation(UIElement.OpacityProperty,null);};
        element.BeginAnimation(UIElement.OpacityProperty,fade,HandoffBehavior.SnapshotAndReplace);
    }
}
