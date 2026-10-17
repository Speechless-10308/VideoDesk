using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using VideoDesk.Core;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private Grid? dynamicHost;
    private Border? dynamicDrawer;
    private CancellationTokenSource? dynamicCommentsCancellation;
    private CancellationToken CommentCancellation=>dynamicCommentsCancellation?.Token??navigation.Token;
    private sealed record DynamicDraft(ComposerSnapshot Composer,bool Newest,double Offset);
    private readonly Dictionary<string,DynamicDraft> dynamicDrafts=[];
    private bool dynamicDrawerClosing;
    private int dynamicDrawerRevision;
    private bool DynamicDrawerOpen=>dynamicDrawer?.Visibility==Visibility.Visible&&!dynamicDrawerClosing;

    private Grid WithDynamicSidebar(FrameworkElement page)
    {
        var host=new Grid();host.ColumnDefinitions.Add(new());host.ColumnDefinitions.Add(new(){Width=new GridLength(0)});host.Children.Add(page);
        var drawer=new Border{Visibility=Visibility.Collapsed,Background=CanvasBrush,CornerRadius=new CornerRadius(20),Padding=new Thickness(18,16,16,12),Margin=new Thickness(16,ReferenceEquals(page,creatorScroll)?0:64,0,0)};
        Grid.SetColumn(drawer,1);Panel.SetZIndex(drawer,5);host.Children.Add(drawer);dynamicHost=host;dynamicDrawer=drawer;
        void Resize()
        {
            if(drawer.Visibility!=Visibility.Visible)return;
            var width=Math.Min(420,Math.Max(320,host.ActualWidth*.32));var overlay=host.ActualWidth<1000;
            host.ColumnDefinitions[1].Width=new GridLength(overlay?0:width);Grid.SetColumn(drawer,overlay?0:1);
            drawer.HorizontalAlignment=overlay?HorizontalAlignment.Right:HorizontalAlignment.Stretch;drawer.Width=overlay?width:double.NaN;
        }
        host.SizeChanged+=(_,_)=>Resize();drawer.IsVisibleChanged+=(_,_)=>Resize();return host;
    }
    private void SaveDynamicDraft()
    {
        if(!DynamicDrawerOpen||dynamicDetail==null)return;
        dynamicDrafts.Remove(dynamicDetail.Id);dynamicDrafts[dynamicDetail.Id]=new(CaptureComposer(),commentsNewest,commentScroll?.VerticalOffset??0);
        while(dynamicDrafts.Count>24)dynamicDrafts.Remove(dynamicDrafts.Keys.First());
    }
    private void CloseDynamicSidebar(bool leaving=false,bool animate=true)
    {
        if(dynamicDrawerClosing&&!leaving&&animate)return;
        if(commentScroll!=null)commentScroll.ScrollChanged-=OnCommentScroll;
        SaveDynamicDraft();dynamicCommentsCancellation?.Cancel();dynamicCommentsCancellation?.Dispose();dynamicCommentsCancellation=null;commentVersion++;commentsLoading=false;
        var drawer=dynamicDrawer;var host=dynamicHost;var revision=++dynamicDrawerRevision;
        if(drawer?.IsKeyboardFocusWithin==true){Keyboard.ClearFocus();FocusManager.SetFocusedElement(this,null);}
        void Collapse()
        {
            if(revision!=dynamicDrawerRevision)return;
            if(drawer!=null){UiMotion.Reset(drawer);drawer.Child=null;drawer.Visibility=Visibility.Collapsed;drawer.IsHitTestVisible=true;}
            if(host!=null)host.ColumnDefinitions[1].Width=new GridLength(0);dynamicDrawerClosing=false;
        }
        dynamicDetail=null;commentInput=null;commentComposer=null;commentScroll=null;commentSidebar=null;commentHeader=null;commentSort=null;
        if(composerPopup!=null)composerPopup.IsOpen=false;if(commentMenu!=null)commentMenu.IsOpen=false;
        if(!leaving&&animate&&drawer?.Visibility==Visibility.Visible){dynamicDrawerClosing=true;drawer.IsHitTestVisible=false;UiMotion.Exit(drawer,Collapse);}else Collapse();
        if(leaving){dynamicHost=null;dynamicDrawer=null;}
    }
    private async Task OpenDynamicSidebar(CreatorDynamic item)
    {
        if(dynamicDrawer==null)return;
        if(DynamicDrawerOpen&&dynamicDetail?.Id==item.Id){commentInput?.Focus();return;}
        var opening=!DynamicDrawerOpen;
        CloseDynamicSidebar(animate:false);dynamicDetail=item;dynamicCommentsCancellation=CancellationTokenSource.CreateLinkedTokenSource(navigation.Token);var ct=CommentCancellation;
        dynamicDrafts.TryGetValue(item.Id,out var saved);commentsNewest=saved?.Newest??false;
        var layout=new DockPanel();var heading=new DockPanel{Margin=new Thickness(0,0,0,12)};
        var close=IconButton(PackIconKind.Close,"关闭动态评论",32);close.Click+=(_,_)=>CloseDynamicSidebar();DockPanel.SetDock(close,Dock.Right);heading.Children.Add(close);
        var name=Text(item.Name+"的动态",14,FontWeights.Medium);name.Foreground=Muted;name.TextTrimming=TextTrimming.CharacterEllipsis;heading.Children.Add(name);DockPanel.SetDock(heading,Dock.Top);layout.Children.Add(heading);
        commentComposer=BuildCommentComposer();DockPanel.SetDock(commentComposer,Dock.Bottom);layout.Children.Add(commentComposer);
        commentHeader=new Border{Margin=new Thickness(0,0,0,18)};DockPanel.SetDock(commentHeader,Dock.Top);layout.Children.Add(commentHeader);
        commentSidebar=new StackPanel();commentScroll=new SmoothScrollViewer{Content=commentSidebar,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};layout.Children.Add(commentScroll);
        dynamicDrawer.Child=layout;dynamicDrawer.Visibility=Visibility.Visible;
        if(opening)UiMotion.Enter(dynamicDrawer,1,32,.35);
        if(saved!=null)RestoreComposer(saved.Composer);
        if(item.CommentForbidden||item.CommentOid<=0){SectionState(commentSidebar).Empty("这条动态暂不支持评论");commentComposer.Visibility=Visibility.Collapsed;return;}
        await LoadComments();ct.ThrowIfCancellationRequested();
        if(saved!=null)await Dispatcher.InvokeAsync(()=>commentScroll?.ScrollToVerticalOffset(saved.Offset),DispatcherPriority.Loaded);
    }
}
