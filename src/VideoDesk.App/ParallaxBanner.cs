using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Diagnostics;

namespace VideoDesk.App;

internal sealed class ParallaxBanner : Border
{
    private readonly ScrollViewer scroll;
    private readonly TranslateTransform shift = new();
    private const double Depth = .65;
    private long lastFrame;
    private TimeSpan renderedAt = TimeSpan.MinValue;
    internal double Offset => shift.Y;
    internal bool IsAnimating { get; private set; }

    internal ParallaxBanner(Image image, ScrollViewer scroll, Brush background)
    {
        this.scroll = scroll;
        Height = 180; Background = background; CornerRadius = new(20); ClipToBounds = true;
        Child = image; image.RenderTransform = shift;
        SizeChanged += (_, e) => { var clip = new RectangleGeometry(new Rect(e.NewSize), 20, 20); clip.Freeze(); Clip = clip; };
        Loaded += (_, _) => { scroll.ScrollChanged += Scrolled; Update(false); };
        Unloaded += (_, _) => { scroll.ScrollChanged -= Scrolled; Stop(); shift.Y = 0; };
        IsVisibleChanged += (_, _) => { if (!IsVisible) Stop(); else if (IsLoaded) Update(false); };
    }

    private void Scrolled(object sender, ScrollChangedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, scroll) && (e.VerticalChange != 0 || e.ViewportHeightChange != 0)) Update();
    }

    private double Target => UiMotion.Enabled ? Math.Clamp(scroll.VerticalOffset, 0, Height) * Depth : 0;

    private void Update(bool animate = true)
    {
        if (!animate || !UiMotion.Enabled || scroll.VerticalOffset >= Height || !IsVisible)
        { Stop(); shift.Y = Target; return; }
        if (IsAnimating || Math.Abs(shift.Y - Target) < .1) return;
        lastFrame = Stopwatch.GetTimestamp(); renderedAt = TimeSpan.MinValue;
        IsAnimating = true; CompositionTarget.Rendering += Frame;
    }

    private void Frame(object? sender, EventArgs e)
    {
        if (e is RenderingEventArgs frame) { if (frame.RenderingTime == renderedAt) return; renderedAt = frame.RenderingTime; }
        var now = Stopwatch.GetTimestamp(); var elapsed = Stopwatch.GetElapsedTime(lastFrame, now).TotalSeconds; lastFrame = now;
        var target = Target;
        // Time-based following works at any refresh rate. Clamp the return trip
        // so the image's top edge cannot expose a gap inside the scroll viewport.
        shift.Y = Math.Clamp(target + (shift.Y - target) * Math.Exp(-elapsed / .08), 0, Math.Min(scroll.VerticalOffset, Height * Depth));
        if (Math.Abs(target - shift.Y) < .1) { shift.Y = target; Stop(); }
    }

    private void Stop()
    {
        CompositionTarget.Rendering -= Frame; IsAnimating = false;
    }
}
