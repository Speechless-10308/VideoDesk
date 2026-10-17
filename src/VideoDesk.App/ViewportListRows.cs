using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace VideoDesk.App;

// Variable-height companion to ViewportCardRows. Keep data and measured heights,
// realize only nearby rows, and preserve the visible row when heights change.
internal sealed class ViewportListRows<T> : Panel
{
    private readonly Func<ScrollViewer?> getScroll;
    private readonly Func<T, FrameworkElement> create;
    private readonly Func<T, object> key;
    private readonly double estimate;
    private T[] items = [];
    private double[] heights = [], starts = [0];
    private readonly Dictionary<int, FrameworkElement> live = [];
    private readonly HashSet<int> replacements=[];
    private ScrollViewer? scroll;
    private double width, anchorDelta;
    private int version;
    private bool queued, dirty, arranged, anchorQueued;
    internal Dictionary<object,(double Width,double Height)>? Measurements {get;init;}

    internal ViewportListRows(Func<ScrollViewer?> getScroll, Func<T, FrameworkElement> create, Func<T, object> key, double estimatedHeight = 180)
    {
        this.getScroll = getScroll; this.create = create; this.key = key; estimate = estimatedHeight;
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Suspend();
        IsVisibleChanged += (_, _) => { if (IsVisible && IsLoaded) Attach(); else Suspend(); };
    }

    internal void SetItems(IEnumerable<T> values)
    {
        var next = values.ToArray();
        var previous = items.Select((item, index) => (Key: key(item), Item: item, Height: heights[index])).ToDictionary(p => p.Key);
        foreach (var index in live.Keys.ToArray())
            if (index >= next.Length) DropRow(index);
            else if(!EqualityComparer<T>.Default.Equals(items[index], next[index]))replacements.Add(index);
        items = next;
        heights = next.Select(item => previous.TryGetValue(key(item), out var old) && EqualityComparer<T>.Default.Equals(item, old.Item) ? old.Height : estimate).ToArray();
        Reindex(); version++; InvalidateMeasure(); Queue();
    }

    private void Attach()
    {
        var owner = getScroll();
        if (!ReferenceEquals(scroll, owner)) { if (scroll != null) scroll.ScrollChanged -= Scrolled; scroll = owner; if (scroll != null) scroll.ScrollChanged += Scrolled; }
        arranged = false; InvalidateArrange(); Queue();
    }
    private void Suspend()
    {
        version++; arranged = false; anchorDelta = 0;
        if (scroll != null) scroll.ScrollChanged -= Scrolled;
        scroll = null;
        foreach (var index in live.Keys.ToArray()) DropRow(index);
    }
    private void Scrolled(object sender, ScrollChangedEventArgs e) { if (ReferenceEquals(e.OriginalSource, scroll)) Queue(); }
    private void DropRow(int index)
    {
        var child = live[index]; Children.Remove(child); ReleaseImages(child); live.Remove(index);replacements.Remove(index);
    }
    private static void ReleaseImages(DependencyObject node)
    {
        if (node is Image image) image.Source = null;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) ReleaseImages(VisualTreeHelper.GetChild(node, i));
    }
    private void Reindex()
    {
        starts = new double[heights.Length + 1];
        for (int i = 0; i < heights.Length; i++) starts[i + 1] = starts[i] + heights[i];
    }
    private int IndexAt(double y)
    {
        int low = 0, high = heights.Length;
        while (low < high) { int middle = (low + high) / 2; if (starts[middle + 1] <= y) low = middle + 1; else high = middle; }
        return Math.Min(Math.Max(0, heights.Length - 1), low);
    }
    private double PanelOffset() => scroll != null && scroll.IsAncestorOf(this) ? -TransformToAncestor(scroll).Transform(new Point()).Y : 0;
    private (int First, int Last) Range()
    {
        if (scroll == null || !IsVisible || items.Length == 0) return (0, -1);
        var top = PanelOffset(); var viewport = scroll.ViewportHeight > 0 ? scroll.ViewportHeight : scroll.ActualHeight;
        if (top + viewport < 0 || top > starts[^1]) return (0, -1);
        return (Math.Max(0, IndexAt(Math.Max(0, top)) - 1), Math.Min(items.Length - 1, IndexAt(top + viewport) + 1));
    }
    protected override Size MeasureOverride(Size available)
    {
        var nextWidth = double.IsFinite(available.Width) ? available.Width : Math.Max(1, getScroll()?.ViewportWidth ?? ActualWidth);
        var offset = PanelOffset(); var anchor = heights.Length > 0 ? IndexAt(Math.Max(0, offset)) : 0;
        var oldStart = starts.Length > anchor ? starts[anchor] : 0;
        if (Math.Abs(nextWidth - width) > 1)
        {
            width = nextWidth;
            for(int i=0;i<items.Length;i++)heights[i]=Measurements!=null&&Measurements.TryGetValue(key(items[i]),out var measured)&&Math.Abs(measured.Width-width)<1?measured.Height:estimate;
            version++; Reindex();
        }
        bool changed = false;
        foreach (var (index, child) in live)
        {
            child.Measure(new Size(width, double.PositiveInfinity));
            var height = Math.Max(1, child.DesiredSize.Height);
            if (Math.Abs(heights[index] - height) > .5) { heights[index] = height; changed = true; }
            if(Measurements!=null&&!replacements.Contains(index))Measurements[key(items[index])]=(width,height);
        }
        if (changed) Reindex();
        if (offset > 0 && anchor < heights.Length)
        {
            var delta = starts[anchor] - oldStart;
            if (Math.Abs(delta) > .5) AdjustAnchor(delta);
        }
        return new Size(width, starts[^1]);
    }
    private void AdjustAnchor(double delta)
    {
        anchorDelta += delta;
        if (anchorQueued) return;
        anchorQueued = true;
        _ = Dispatcher.BeginInvoke(() =>
        {
            anchorQueued = false; var shift = anchorDelta; anchorDelta = 0;
            if (!IsLoaded || !IsVisible || scroll == null || Math.Abs(shift) < .5) return;
            if (scroll is SmoothScrollViewer smooth) smooth.ShiftOffset(shift);
            else scroll.ScrollToVerticalOffset(scroll.VerticalOffset + shift);
        }, DispatcherPriority.Loaded);
    }
    protected override Size ArrangeOverride(Size final)
    {
        foreach (var (index, child) in live) child.Arrange(new Rect(0, starts[index], final.Width, heights[index]));
        arranged = true; Queue(); return final;
    }
    private void Queue()
    {
        dirty = true;
        if (queued || !arranged || !IsLoaded || !IsVisible) return;
        queued = true;
        _ = Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                while (dirty && arranged && IsLoaded && IsVisible)
                {
                    dirty = false; var stamp = version; var (first, last) = Range();
                    foreach (var index in live.Keys.Where(i => i < first || i > last).ToArray()) DropRow(index);
                    for (int index = first; index <= last; index++)
                    {
                        if (live.ContainsKey(index)&&!replacements.Contains(index)) continue;
                        if (stamp != version || !IsVisible) return;
                        var current = Range(); if (index < current.First || index > current.Last) { dirty = true; break; }
                        var child = create(items[index]);
                        if(live.ContainsKey(index))DropRow(index);
                        live[index] = child; Children.Add(child); InvalidateMeasure();
                        // One variable-height row may contain text and media. Let
                        // layout resolve it before deciding which row comes next.
                        await Dispatcher.Yield(DispatcherPriority.Background);
                    }
                }
            }
            finally { queued = false; if (dirty && IsLoaded && IsVisible) Queue(); }
        }), DispatcherPriority.Background);
    }
}
