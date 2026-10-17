using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace VideoDesk.App;

// Keep models for the full list, but create controls only around the viewport.
// Control creation stays on its owning Dispatcher and yields between small slices.
internal sealed class ViewportCardRows<T> : Panel
{
    private sealed record Row(T[] Items,string Label,bool First);
    private readonly Func<ScrollViewer?> getScroll;
    private readonly Func<double,int> getColumns;
    private readonly Func<T,FrameworkElement> createCard;
    private readonly Func<T,string>? group;
    private readonly Func<string,bool,UniformGrid,FrameworkElement>? frame;
    private readonly double gutter;
    internal double ExtraRowHeight {get;init;}
    internal Func<double,int,double>? RowHeightProvider {get;init;}
    private T[] items=[];private Row[] rows=[];
    private readonly Dictionary<int,FrameworkElement> live=[];
    private ScrollViewer? scroll;private double width,rowHeight;
    private int columns,rowColumns,version;private bool queued,dirty,awaitingArrange=true;
    internal bool RetainOnSuspend {get;init;}
    internal ViewportCardRows(Func<ScrollViewer?> getScroll,Func<double,int> getColumns,Func<T,FrameworkElement> createCard,Func<T,string>? group=null,Func<string,bool,UniformGrid,FrameworkElement>? frame=null,double gutter=0)
    {
        this.getScroll=getScroll;this.getColumns=getColumns;this.createCard=createCard;this.group=group;this.frame=frame;this.gutter=gutter;
        Loaded+=(_,_)=>Attach();Unloaded+=(_,_)=>Suspend();IsVisibleChanged+=(_,_)=>{if(IsVisible&&IsLoaded)Attach();else Suspend();};
    }
    internal void SetItems(IEnumerable<T> values){items=values.ToArray();Reflow();InvalidateMeasure();}
    internal void Append(IEnumerable<T> values){SetItems(items.Concat(values));}
    internal async Task PrepareFirstViewportAsync(double availableWidth,double viewportHeight,CancellationToken ct)
    {
        // Prepare a small detached viewport while the current page stays visible.
        // Offscreen rows remain data only and use the normal lazy path later.
        Measure(new Size(availableWidth,double.PositiveInfinity));var stamp=version;var count=Math.Min(rows.Length,(int)Math.Ceiling(viewportHeight/rowHeight)+1);var slice=Stopwatch.StartNew();
        for(int index=0;index<count;index++)
        {
            ct.ThrowIfCancellationRequested();if(stamp!=version)return;if(live.ContainsKey(index))continue;var row=rows[index];var cards=new UniformGrid{Columns=columns,VerticalAlignment=VerticalAlignment.Top};
            foreach(var item in row.Items){ct.ThrowIfCancellationRequested();cards.Children.Add(createCard(item));if(slice.Elapsed.TotalMilliseconds>=3){await Dispatcher.Yield(DispatcherPriority.Background);slice.Restart();}}
            if(stamp!=version)return;var element=frame?.Invoke(row.Label,row.First,cards)??cards;live[index]=element;Children.Add(element);element.Measure(new Size(availableWidth,rowHeight));
            if(slice.Elapsed.TotalMilliseconds>=3){await Dispatcher.Yield(DispatcherPriority.Background);slice.Restart();}
        }
        ct.ThrowIfCancellationRequested();
    }
    private void Attach(){var owner=getScroll();if(!ReferenceEquals(scroll,owner)){if(scroll!=null)scroll.ScrollChanged-=Scrolled;scroll=owner;if(scroll!=null)scroll.ScrollChanged+=Scrolled;}awaitingArrange=true;InvalidateArrange();Queue();}
    private void Suspend(){version++;awaitingArrange=true;if(scroll!=null)scroll.ScrollChanged-=Scrolled;scroll=null;if(!RetainOnSuspend)ReleaseRetainedRows();}
    internal void ReleaseRetainedRows(){foreach(var index in live.Keys.ToArray())DropRow(index);}
    private void DropRow(int index){var element=live[index];Children.Remove(element);ReleaseImages(element);live.Remove(index);}
    private static void ReleaseImages(DependencyObject element){if(element is Image image)image.Source=null;for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(element);i++)ReleaseImages(System.Windows.Media.VisualTreeHelper.GetChild(element,i));}
    private void Scrolled(object sender,ScrollChangedEventArgs e){if(ReferenceEquals(e.OriginalSource,scroll))Queue();}
    private void Reflow()
    {
        version++;var result=new List<Row>();var count=Math.Max(1,columns);
        foreach(var section in group==null?new[]{items.AsEnumerable()}:items.GroupBy(group).Select(g=>g.AsEnumerable()))
        {
            var first=true;foreach(var chunk in section.Chunk(count)){result.Add(new(chunk,group?.Invoke(chunk[0])??"",first));first=false;}
        }
        var next=result.ToArray();foreach(var index in live.Keys.ToArray())
        {
            if(rowColumns==columns&&index<next.Length&&index<rows.Length&&rows[index].Label==next[index].Label&&rows[index].First==next[index].First&&rows[index].Items.SequenceEqual(next[index].Items))continue;
            DropRow(index);
        }
        rowColumns=columns;rows=next;Queue();
    }
    protected override Size MeasureOverride(Size available)
    {
        var nextWidth=double.IsFinite(available.Width)?available.Width:Math.Max(1,getScroll()?.ViewportWidth??ActualWidth);
        var nextColumns=Math.Max(1,getColumns(Math.Max(0,nextWidth-gutter)));
        if(nextColumns!=columns){columns=nextColumns;width=nextWidth;Reflow();}
        if(Math.Abs(width-nextWidth)>.1){width=nextWidth;Queue();}
        rowHeight=RowHeightProvider?.Invoke(width,columns)??(Math.Max(100,Math.Max(0,(width-gutter)/columns-14)*9/16)+110+ExtraRowHeight);
        foreach(var child in live.Values)child.Measure(new Size(width,rowHeight));
        return new Size(width,rows.Length*rowHeight);
    }
    protected override Size ArrangeOverride(Size final)
    {
        foreach(var (index,child) in live)child.Arrange(new Rect(0,index*rowHeight,final.Width,rowHeight));awaitingArrange=false;Queue();return final;
    }
    private (int First,int Last) Range()
    {
        if(scroll==null||!IsVisible||rowHeight<=0)return (0,-1);
        var y=TransformToAncestor(scroll).Transform(new Point()).Y;
        var viewport=scroll.ViewportHeight>0?scroll.ViewportHeight:scroll.ActualHeight;
        return (Math.Max(0,(int)Math.Floor((-y-rowHeight)/rowHeight)),Math.Min(rows.Length-1,(int)Math.Ceiling((-y+viewport+rowHeight)/rowHeight)-1));
    }
    private void Queue()
    {
        dirty=true;if(queued||awaitingArrange||!IsLoaded||!IsVisible)return;queued=true;
        _=Dispatcher.BeginInvoke(new Action(async()=>
        {
            try
            {
                while(dirty&&!awaitingArrange&&IsLoaded&&IsVisible)
                {
                    dirty=false;var stamp=version;var (first,last)=Range();
                    foreach(var index in live.Keys.Where(i=>i<first||i>last).ToArray())DropRow(index);
                    for(int index=first;index<=last;index++)
                    {
                        if(live.ContainsKey(index))continue;var row=rows[index];var cards=new UniformGrid{Columns=columns,VerticalAlignment=VerticalAlignment.Top};var slice=Stopwatch.StartNew();
                        foreach(var item in row.Items)
                        {
                            if(stamp!=version||!IsVisible)return;cards.Children.Add(createCard(item));
                            if(slice.Elapsed.TotalMilliseconds>=3){await Dispatcher.Yield(DispatcherPriority.Background);slice.Restart();}
                        }
                        if(stamp!=version||!IsVisible)return;var current=Range();if(index<current.First||index>current.Last)continue;
                        var element=frame?.Invoke(row.Label,row.First,cards)??cards;live[index]=element;Children.Add(element);
                        // Give layout, render and input a turn before publishing the next row.
                        await Dispatcher.Yield(DispatcherPriority.Background);
                    }
                }
            }
            finally{queued=false;if(dirty&&IsLoaded&&IsVisible)Queue();}
        }),DispatcherPriority.Background);
    }
}
