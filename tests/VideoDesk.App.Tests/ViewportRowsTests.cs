using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VideoDesk.App.Tests.Support;

namespace VideoDesk.App.Tests;

[Collection("Native player")]
public sealed class ViewportRowsTests
{
    [Fact]
    public Task ResizeAfterFirstRowLoads_DiscardsOldRowRange() => PlayerTestWindow.Run(async () =>
    {
        using var view = new ViewportWindow(); var resized = false;
        var rows = new ViewportCardRows<int>(() => view.Scroll, width => width >= 800 ? 4 : 1, item =>
        {
            var card = Card(item);
            card.Loaded += (_, _) =>
            {
                if (resized) return;
                resized = true; view.Window.Width = 1000; view.Window.UpdateLayout();
            };
            return card;
        }) { RowHeightProvider = (_, _) => 100 };
        rows.SetItems(Enumerable.Range(0, 4)); view.Show(rows);
        await Drain();
        Assert.True(resized); Assert.Empty(view.Errors);
        Assert.Equal(new[] {0, 1, 2, 3}, Values(rows));
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task DataReplacedInsideFactory_DoesNotPublishOldControls(bool cards) => PlayerTestWindow.Run(async () =>
    {
        using var view = new ViewportWindow(); var replaced = false;
        Action replace = null!;
        FrameworkElement Create(int item)
        {
            if (!replaced) { replaced = true; replace(); }
            return Card(item);
        }
        Panel panel;
        if (cards)
        {
            var rows = new ViewportCardRows<int>(() => view.Scroll, _ => 1, Create);
            rows.SetItems([0, 1, 2]); replace = () => rows.SetItems([42]); panel = rows;
        }
        else
        {
            var rows = new ViewportListRows<int>(() => view.Scroll, Create, item => item, 100);
            rows.SetItems([0, 1, 2]); replace = () => rows.SetItems([42]); panel = rows;
        }
        view.Show(panel); await Drain();
        Assert.Empty(view.Errors); Assert.Equal(new[] {42}, Values(panel));
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task ReplacedBuild_ReleasesImageBeforeTemplateWasLoaded(bool cards) => PlayerTestWindow.Run(async () =>
    {
        using var view = new ViewportWindow(); Action replace = null!; Image? discarded = null;
        var bitmap=BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,new byte[]{255,255,255,255},4);bitmap.Freeze();
        FrameworkElement Create(int item)
        {
            var image=new Image{Source=bitmap};var content=new StackPanel();content.Children.Add(Card(item));content.Children.Add(image);
            if(item==0){discarded=image;replace();}
            return new Button{Content=content};
        }
        Panel panel;
        if(cards){var rows=new ViewportCardRows<int>(()=>view.Scroll,_=>1,Create);rows.SetItems([0]);replace=()=>rows.SetItems([42]);panel=rows;}
        else{var rows=new ViewportListRows<int>(()=>view.Scroll,Create,item=>item,100);rows.SetItems([0]);replace=()=>rows.SetItems([42]);panel=rows;}
        view.Show(panel);await Drain();
        Assert.NotNull(discarded);Assert.Null(discarded.Source);Assert.Equal(new[]{42},Values(panel));Assert.Empty(view.Errors);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task RepeatedResizeRefreshAndHide_UsesOnlyLatestSnapshot(bool cards) => PlayerTestWindow.Run(async () =>
    {
        using var view=new ViewportWindow();Action<int[]> set;Panel panel;
        if(cards){var rows=new ViewportCardRows<int>(()=>view.Scroll,w=>w>=800?4:1,Card){RowHeightProvider=(_,_)=>100};set=rows.SetItems;panel=rows;}
        else{var rows=new ViewportListRows<int>(()=>view.Scroll,Card,item=>item,100);set=rows.SetItems;panel=rows;}
        set(Enumerable.Range(0,20).ToArray());view.Show(panel);
        for(var i=0;i<40;i++)
        {
            view.Window.Width=i%2==0?1000:320;set(Enumerable.Range(i*100,i%20+1).ToArray());
            if(i%7==0){view.Window.Visibility=Visibility.Collapsed;view.Window.Visibility=Visibility.Visible;}
            view.Window.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.Background);
        }
        set([9001,9002]);await Drain();Assert.Equal(new[]{9001,9002},Values(panel));Assert.Empty(view.Errors);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task LargeList_ScrollsAndRecyclesInsteadOfCreatingAllCards(bool cards) => PlayerTestWindow.Run(async () =>
    {
        using var view = new ViewportWindow(); var created = 0;
        FrameworkElement Create(int item) { created++; return Card(item); }
        Panel panel;
        if (cards)
        {
            var rows = new ViewportCardRows<int>(() => view.Scroll, _ => 3, Create) { RowHeightProvider = (_, _) => 100 };
            rows.SetItems(Enumerable.Range(0, 10000)); panel = rows;
        }
        else
        {
            var rows = new ViewportListRows<int>(() => view.Scroll, Create, item => item, 100);
            rows.SetItems(Enumerable.Range(0, 10000)); panel = rows;
        }
        view.Show(panel); await Drain();
        Assert.InRange(created, 1, 40);
        view.Scroll.ScrollToVerticalOffset(5000); view.Window.UpdateLayout(); await Drain();
        Assert.InRange(Values(panel).Length, 1, 40); Assert.DoesNotContain(0, Values(panel)); Assert.Empty(view.Errors);
        view.Window.Visibility = Visibility.Collapsed; await Drain(); Assert.Empty(Values(panel));
    });

    [Fact]
    public Task UnchangedRecords_StillRefreshTimeDependentGroupLabels() => PlayerTestWindow.Run(async () =>
    {
        using var view=new ViewportWindow();var label="今天";
        var rows=new ViewportCardRows<int>(()=>view.Scroll,_=>1,Card,_=>label,(text,_,cards)=>
        {
            var frame=new StackPanel();frame.Children.Add(new TextBlock{Text=text,Tag="group-label"});frame.Children.Add(cards);return frame;
        });
        rows.SetItems([1]);view.Show(rows);await Drain();label="昨天";rows.SetItems([1]);await Drain();
        Assert.Equal("昨天",VisualTree.Descendants(rows).OfType<TextBlock>().Single(item=>item.Tag as string=="group-label").Text);Assert.Empty(view.Errors);
    });

    [Fact]
    public Task RepeatedIdenticalSnapshots_ReportAllocationCost() => PlayerTestWindow.Run(async () =>
    {
        using var view = new ViewportWindow(); var values = Enumerable.Range(0, 5000).ToArray();
        var cards = new ViewportCardRows<int>(() => view.Scroll, _ => 3, Card) { RowHeightProvider = (_, _) => 100 };
        cards.SetItems(values); view.Show(cards); await Drain();
        var visible = Values(cards); var before = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
        for (var i = 0; i < 100; i++) cards.SetItems(values);
        TestContext.Current.TestOutputHelper?.WriteLine($"Card snapshots: {watch.Elapsed.TotalMilliseconds:F2} ms; {GC.GetAllocatedBytesForCurrentThread()-before} bytes for 100 x 5000 records");
        await Drain(); Assert.Equal(visible, Values(cards));
        var list = new ViewportListRows<int>(() => view.Scroll, Card, item => item, 100);
        list.SetItems(values); view.Scroll.Content = list; await Drain();
        visible = Values(list); before = GC.GetAllocatedBytesForCurrentThread(); watch.Restart();
        for (var i = 0; i < 100; i++) list.SetItems(values);
        TestContext.Current.TestOutputHelper?.WriteLine($"List snapshots: {watch.Elapsed.TotalMilliseconds:F2} ms; {GC.GetAllocatedBytesForCurrentThread()-before} bytes for 100 x 5000 records");
        await Drain(); Assert.Equal(visible, Values(list)); Assert.Empty(view.Errors);
    });

    private static FrameworkElement Card(int item) => new TextBlock { Text = item.ToString(), Tag = item, Height = 100 };
    private static int[] Values(DependencyObject panel) => VisualTree.Descendants(panel).OfType<TextBlock>().Where(item => item.Tag is int).Select(item => (int)item.Tag).Order().ToArray();
    private static async Task Drain() { await Dispatcher.Yield(DispatcherPriority.SystemIdle); await Dispatcher.Yield(DispatcherPriority.SystemIdle); }

    private sealed class ViewportWindow : IDisposable
    {
        public ScrollViewer Scroll { get; } = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        public Window Window { get; }
        public List<Exception> Errors { get; } = [];
        public ViewportWindow()
        {
            Window = new Window { Width = 320, Height = 500, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Content = Scroll };
            Window.Dispatcher.UnhandledException += Unhandled;
        }
        private void Unhandled(object sender, DispatcherUnhandledExceptionEventArgs e) { Errors.Add(e.Exception); e.Handled = true; }
        public void Show(UIElement panel) { Scroll.Content = panel; Window.Show(); }
        public void Dispose() { Window.Close(); Window.Dispatcher.UnhandledException -= Unhandled; }
    }
}
