using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using VideoDesk.Core;

namespace VideoDesk.App;

// Timed command cards share the existing player surface and video coordinates.
// A response from an old part cannot update the next part's cards.
internal sealed class PlayerInteractionLayer : Canvas,IDisposable
{
    private IReadOnlyList<VideoInteraction> items=[];
    private readonly Dictionary<string,VideoInteraction> results=[];
    private readonly HashSet<string> dismissed=[],submitting=[];
    private readonly Dictionary<string,string> errors=[];
    private Func<VideoInteraction,int,double,CancellationToken,Task<VideoInteraction>>? submit;
    private CancellationTokenSource session=new();
    private VideoInteraction? active;
    private Border? card;
    private double seconds,aspect=16d/9;
    private bool enabled=true,disposed;
    private int revision;
    internal PlayerInteractionLayer(){SizeChanged+=(_,_)=>Position();}

    internal void SetItems(IReadOnlyList<VideoInteraction> value,Func<VideoInteraction,int,double,CancellationToken,Task<VideoInteraction>>? send,CancellationToken ct=default)
    {
        revision++;session.Cancel();session.Dispose();session=CancellationTokenSource.CreateLinkedTokenSource(ct);
        items=value;submit=send;results.Clear();dismissed.Clear();submitting.Clear();errors.Clear();active=null;card=null;Children.Clear();
    }
    internal void Update(double position,double videoAspect,bool visible)
    {
        if(disposed)return;seconds=position;aspect=double.IsFinite(videoAspect)&&videoAspect>0?videoAspect:16d/9;enabled=visible;
        VideoInteraction? next=null;
        if(visible)foreach(var source in items){var item=results.GetValueOrDefault(source.CommandId,source);if(item.Contains(position)&&!dismissed.Contains(item.CommandId))next=item;}
        if(next!=active){active=next;Render();}else Position();
    }
    private static TextBlock Label(string text,double size=12)=>new(){Text=text,FontSize=size,Foreground=Brushes.White,TextTrimming=TextTrimming.CharacterEllipsis};
    private static Button Button(object content,double height=28)
    {
        var button=new Button{Content=content,Style=(Style)Application.Current.FindResource("PlainButton"),Foreground=Brushes.White,Height=height,Padding=new(8,0,8,0),Background=Brushes.Transparent};
        ButtonAssist.SetCornerRadius(button,new CornerRadius(5));return button;
    }
    private void Render()
    {
        Children.Clear();card=null;if(active is not {} item)return;
        var body=new StackPanel();var heading=new Grid();heading.ColumnDefinitions.Add(new());heading.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var title=Label(item.Title);title.TextWrapping=TextWrapping.Wrap;title.MaxHeight=36;title.FontWeight=FontWeights.SemiBold;title.Margin=new(0,3,4,3);heading.Children.Add(title);
        var close=Button(new PackIcon{Kind=PackIconKind.Close,Width=14,Height=14},24);close.Width=24;close.Padding=new(3);close.VerticalAlignment=VerticalAlignment.Top;close.ToolTip="关闭";
        close.Click+=(_,_)=>{dismissed.Add(item.CommandId);active=null;Render();};Grid.SetColumn(close,1);heading.Children.Add(close);body.Children.Add(heading);
        bool busy=submitting.Contains(item.CommandId),answered=item.Selection>0;
        if(item.Kind==VideoInteractionKind.Vote)
        {
            long total=Math.Max(item.Count,item.Options.Sum(o=>o.Count));
            foreach(var option in item.Options)
            {
                var row=new Grid{MinHeight=28};var fill=new Border{Background=new SolidColorBrush(Color.FromArgb(60,255,255,255)),HorizontalAlignment=HorizontalAlignment.Left,Width=0};row.Children.Add(fill);
                if(answered){double fraction=total>0?(double)option.Count/total:0;row.SizeChanged+=(_,e)=>fill.Width=Math.Clamp(fraction,0,1)*e.NewSize.Width;}
                var text=Label((answered&&option.Id==item.Selection?"✓ ":"")+option.Text);text.Margin=new(8,0,answered?40:8,0);text.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(text);
                if(answered){var percent=Label(total>0?((double)option.Count/total).ToString("P0"):"0%",11);percent.HorizontalAlignment=HorizontalAlignment.Right;percent.VerticalAlignment=VerticalAlignment.Center;percent.Margin=new(0,0,8,0);row.Children.Add(percent);}
                var choice=Button(row);choice.Padding=new(0);choice.HorizontalContentAlignment=HorizontalAlignment.Stretch;choice.Background=new SolidColorBrush(Color.FromArgb(42,255,255,255));choice.Margin=new(0,3,0,0);choice.IsEnabled=!busy;choice.IsHitTestVisible=!answered;choice.Focusable=!answered;choice.ToolTip=option.Text;
                choice.Click+=async(_,_)=>await Send(item,option.Id);body.Children.Add(choice);
            }
            if(answered){var count=Label(total+" 人参与",10);count.Foreground=Brushes.LightGray;count.Margin=new(1,6,0,0);body.Children.Add(count);}
        }
        else
        {
            var count=Label(item.Count+" 人参与"+(answered?" · 综合 "+item.Average.ToString("0.#")+" 分":""),10);count.Foreground=Brushes.LightGray;count.Margin=new(0,0,0,3);body.Children.Add(count);
            var stars=new System.Windows.Controls.Primitives.UniformGrid{Columns=5};var icons=new List<PackIcon>();
            void Highlight(int score){for(int i=0;i<icons.Count;i++)icons[i].Kind=i*2<score?PackIconKind.Star:PackIconKind.StarOutline;}
            for(int i=1;i<=5;i++)
            {
                int score=i*2;var icon=new PackIcon{Kind=i*2<=item.Selection?PackIconKind.Star:PackIconKind.StarOutline,Width=25,Height=25,Foreground=new SolidColorBrush(Color.FromRgb(255,177,18))};icons.Add(icon);
                var star=Button(icon,31);star.Padding=new(0);star.IsEnabled=!busy;star.IsHitTestVisible=!answered;star.Focusable=!answered;star.ToolTip=i+" 星";star.MouseEnter+=(_,_)=>{if(!answered)Highlight(score);};star.Click+=async(_,_)=>await Send(item,score);stars.Children.Add(star);
            }
            stars.MouseLeave+=(_,_)=>Highlight(item.Selection);body.Children.Add(stars);
            if(answered){var selected=Label("已评 "+(item.Selection/2d).ToString("0.#")+" 星",11);selected.Foreground=Brushes.LightGray;selected.Margin=new(0,4,0,0);body.Children.Add(selected);}
        }
        if(busy||errors.TryGetValue(item.CommandId,out _))
        {
            var status=Label(busy?"提交中…":errors[item.CommandId],11);status.TextWrapping=TextWrapping.Wrap;status.MaxHeight=34;status.Margin=new(0,6,0,0);status.Foreground=Brushes.LightGray;body.Children.Add(status);
        }
        card=new Border{Width=228,Padding=new(10,7,10,10),Background=new SolidColorBrush(Color.FromArgb(222,22,23,27)),CornerRadius=new(7),Child=body};Children.Add(card);Position();
    }
    private async Task Send(VideoInteraction item,int selection)
    {
        if(submit==null||submitting.Contains(item.CommandId)||item.Selection>0)return;
        int stamp=revision;var ct=session.Token;submitting.Add(item.CommandId);errors.Remove(item.CommandId);Render();
        try
        {
            var updated=await submit(item,selection,seconds,ct);ct.ThrowIfCancellationRequested();if(stamp!=revision||disposed)return;
            results[item.CommandId]=updated with{Duration=Math.Max(updated.Duration,seconds-updated.Start+updated.SummaryDuration)};
        }
        catch(OperationCanceledException){}
        catch(Exception error){if(stamp==revision&&!disposed)errors[item.CommandId]=error.Message;}
        finally
        {
            if(stamp==revision&&!disposed){submitting.Remove(item.CommandId);active=null;Update(seconds,aspect,enabled);}
        }
    }
    private void Position()
    {
        if(card==null||active==null||ActualWidth<1||ActualHeight<1)return;
        card.Width=Math.Max(120,Math.Min(228,ActualWidth-16));card.Measure(new Size(card.Width,double.PositiveInfinity));
        double width=ActualWidth,height=width/aspect,left=0,top=(ActualHeight-height)/2;
        if(height>ActualHeight){height=ActualHeight;width=height*aspect;left=(ActualWidth-width)/2;top=0;}
        var size=card.DesiredSize;
        SetLeft(card,Math.Clamp(left+active.X*width-size.Width/2,8,Math.Max(8,ActualWidth-size.Width-8)));
        SetTop(card,Math.Clamp(top+active.Y*height-size.Height/2,8,Math.Max(8,ActualHeight-size.Height-105)));
    }
    public void Dispose(){if(disposed)return;disposed=true;revision++;session.Cancel();session.Dispose();items=[];results.Clear();Children.Clear();}
}
