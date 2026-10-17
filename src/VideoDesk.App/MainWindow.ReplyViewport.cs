using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using VideoDesk.Core;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private sealed class ReplyViewportState
    {
        internal bool Expanded, Ended, Loading;
        internal int Page;
        internal readonly List<CommentItem> Items=[];
        internal event Action? Changed;
        internal void Notify()=>Changed?.Invoke();
    }
    private readonly Dictionary<long,ReplyViewportState> commentReplyStates=[];
    private void RefreshReplyViewports(Func<CommentItem,CommentItem>? update=null)
    {
        foreach(var (id,state) in commentReplyStates)
        {
            var next=commentSession.MergeReplies(CommentOid,id,state.Items);
            if(update!=null)next=TransformComments(next,update);
            if(next.SequenceEqual(state.Items))continue;
            state.Items.Clear();state.Items.AddRange(next);state.Notify();
        }
    }

    private FrameworkElement RetainedReplyPreview(CommentItem item)
    {
        if(!commentReplyStates.TryGetValue(item.Id,out var state))
        {
            if(commentReplyStates.Count>=128)commentReplyStates.Remove(commentReplyStates.Keys.First());
            commentReplyStates[item.Id]=state=new();
        }
        if(state.Expanded){var merged=commentSession.MergeReplies(CommentOid,item.Id,state.Items);state.Items.Clear();state.Items.AddRange(merged);}
        var box=new StackPanel();var replies=new StackPanel();var total=Math.Max(item.ReplyCount,item.Replies?.Count??0);
        var more=Button("");more.Foreground=new SolidColorBrush(Color.FromRgb(0,161,214));more.FontSize=13;more.Height=28;more.Padding=new Thickness(0,3,0,3);more.HorizontalContentAlignment=HorizontalAlignment.Left;
        var collapse=Button("收起回复");collapse.FontSize=12;collapse.Foreground=Muted;collapse.Height=28;collapse.Padding=new Thickness(0);collapse.HorizontalContentAlignment=HorizontalAlignment.Left;
        int rendered=-1;CommentItem[] drawn=[];
        void Render()
        {
            more.IsEnabled=!state.Loading;
            if(state.Expanded)
            {
                if(rendered<0||rendered>state.Items.Count||!state.Items.Take(rendered).SequenceEqual(drawn)){replies.Children.Clear();rendered=0;}
                foreach(var child in state.Items.Skip(rendered))replies.Children.Add(RenderComment(child,true,item.Id));
                rendered=state.Items.Count;drawn=state.Items.ToArray();more.Content="加载更多回复";more.Visibility=state.Ended?Visibility.Collapsed:Visibility.Visible;collapse.Visibility=Visibility.Visible;
            }
            else
            {
                replies.Children.Clear();rendered=-1;
                foreach(var child in (item.Replies??[]).Take(2))
                {
                    var message=RichComment(child,13,timeLinks:dynamicDetail==null&&CommentType==1);var prefix=new Run(child.Name+"：");
                    if(message.Inlines.FirstInline is {} first)message.Inlines.InsertBefore(first,prefix);else message.Inlines.Add(prefix);
                    message.TextWrapping=TextWrapping.Wrap;message.LineHeight=22;message.MaxHeight=44;message.TextTrimming=TextTrimming.CharacterEllipsis;message.Foreground=Muted;message.Margin=new Thickness(0,0,0,7);replies.Children.Add(SelectableRichText(message,"回复"));
                }
                more.Content="共 "+total+" 条回复  ›";more.Visibility=Visibility.Visible;collapse.Visibility=Visibility.Collapsed;
            }
        }
        void Changed(){if(box.IsLoaded&&box.IsVisible&&PresentationSource.FromVisual(box)!=null)Render();}
        box.Loaded+=(_,_)=>{state.Changed+=Changed;Render();};box.Unloaded+=(_,_)=>state.Changed-=Changed;
        Render();
        more.Click+=async(_,_)=>await Run(async()=>
        {
            if(state.Loading)return;
            if(!state.Expanded&&state.Items.Count>0){state.Expanded=true;state.Notify();return;}
            var ct=CommentCancellation;var oid=CommentOid;var type=CommentType;state.Loading=true;state.Notify();
            try
            {
                var result=await api.GetReplies(oid,item.Id,state.Page+1,ct,type);ct.ThrowIfCancellationRequested();
                var known=state.Items.Select(c=>c.Id).ToHashSet();state.Items.AddRange(commentSession.MergeReplies(oid,item.Id,result.Items).Where(c=>known.Add(c.Id)));
                state.Page++;state.Expanded=true;state.Ended=!result.HasMore;
            }
            finally{state.Loading=false;state.Notify();}
        });
        collapse.Click+=(_,_)=>{state.Expanded=false;state.Notify();};
        box.Children.Add(replies);box.Children.Add(more);box.Children.Add(collapse);
        return new Border{Child=box,Background=Surface,CornerRadius=new(10),Margin=new(0,9,0,0),Padding=new(12,10,12,8)};
    }
}
