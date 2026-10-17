using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace VideoDesk.App;

internal enum SectionStatus { Ready, Loading, Empty, Failed }

internal sealed class SectionLoadState : Border
{
    private readonly TextBlock title = new() { FontSize=14, TextWrapping=TextWrapping.Wrap };
    private readonly TextBlock reason = new() { FontSize=12, TextWrapping=TextWrapping.Wrap, Margin=new(0,6,0,0) };
    private readonly StackPanel actions = new() { Orientation=Orientation.Horizontal, Margin=new(0,8,0,0) };
    private readonly Button retry = new() { Content="重试", Height=32, Style=(Style)Application.Current.FindResource("PlainButton") };
    private Func<Task>? retryAction;
    private bool retrying;
    public SectionStatus Status { get; private set; }
    public string Message => reason.Text;
    public SectionLoadState(Brush ink, Brush muted, Brush surface, Brush accent)
    {
        Background=surface; CornerRadius=new(8); Padding=new(14,12,14,12); Margin=new(0,8,0,12);
        title.Foreground=ink; reason.Foreground=muted; retry.Foreground=accent;
        var body=new StackPanel();body.Children.Add(title);body.Children.Add(reason);body.Children.Add(actions);Child=body;
        actions.Children.Add(retry);retry.Click+=async(_,_)=>await RetryAsync();
        AutomationProperties.SetLiveSetting(this,AutomationLiveSetting.Polite); Clear();
    }
    public void Loading(string label)
    {
        Status=SectionStatus.Loading;Visibility=Visibility.Collapsed;title.Text="";reason.Text="";
        reason.Visibility=actions.Visibility=Visibility.Collapsed;
    }
    public void Empty(string label)
    {
        Status=SectionStatus.Empty;Visibility=Visibility.Visible;title.Visibility=Visibility.Visible;title.Text=label;reason.Text="";
        reason.Visibility=actions.Visibility=retry.Visibility=Visibility.Collapsed;retryAction=null;
    }
    public void Failed(string label,string message,Func<Task> action)
    {
        Status=SectionStatus.Failed;Visibility=Visibility.Visible;title.Text=label;title.Visibility=label.Length==0?Visibility.Collapsed:Visibility.Visible;reason.Text=message;
        reason.Visibility=actions.Visibility=retry.Visibility=Visibility.Visible;retryAction=action;
    }
    public void AddAction(Button button){actions.Children.Add(button);actions.Visibility=Visibility.Visible;}
    public void Clear(){Status=SectionStatus.Ready;Visibility=Visibility.Collapsed;retryAction=null;}
    public async Task RetryAsync()
    {
        if(retrying||retryAction==null)return;var action=retryAction;retrying=true;retry.IsEnabled=false;
        try{await action();}catch(OperationCanceledException){}
        catch(Exception error){Failed("加载失败",error.Message,action);}
        finally{retrying=false;retry.IsEnabled=true;}
    }
}
