using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer feedbackTimer=new(){Interval=TimeSpan.FromSeconds(4)};
    private readonly DispatcherTimer busyReveal=new(){Interval=TimeSpan.FromMilliseconds(550)};
    private int activeOperations;
    private void BuildFeedback()
    {
        var row=new DockPanel();var close=IconButton(MaterialDesignThemes.Wpf.PackIconKind.Close,"关闭提示",28);close.Height=28;
        close.Margin=new(12,0,0,0);close.Click+=(_,_)=>DismissFeedback();DockPanel.SetDock(close,Dock.Right);row.Children.Add(close);
        status.TextWrapping=TextWrapping.Wrap;status.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(status);
        statusBar.Child=row;statusBar.Padding=new(14,10,10,10);statusBar.Background=PalePink;statusBar.CornerRadius=new(8);
        statusBar.MaxWidth=520;statusBar.Margin=new(16);statusBar.HorizontalAlignment=HorizontalAlignment.Right;statusBar.VerticalAlignment=VerticalAlignment.Bottom;
        statusBar.Visibility=Visibility.Collapsed;Grid.SetRow(statusBar,1);Grid.SetColumn(statusBar,1);Panel.SetZIndex(statusBar,80);shell.Children.Add(statusBar);
        AutomationProperties.SetLiveSetting(statusBar,AutomationLiveSetting.Polite);
        feedbackTimer.Tick+=(_,_)=>DismissFeedback();
        busyReveal.Tick+=(_,_)=>{busyReveal.Stop();if(activeOperations>0)loading.Visibility=Visibility.Visible;};
        Closed+=(_,_)=>{feedbackTimer.Stop();busyReveal.Stop();};
    }
    private void DismissFeedback(){feedbackTimer.Stop();statusBar.Visibility=Visibility.Collapsed;}
    private void Notify(string message)
    {
        feedbackTimer.Stop();status.Text=message;
        if(fullscreen){statusBar.Visibility=Visibility.Collapsed;player?.Command("show-text",message,"3000");}
        else{statusBar.Visibility=Visibility.Visible;feedbackTimer.Start();}
    }
    private async Task Run(Func<Task> operation)
    {
        if(activeOperations++==0)busyReveal.Start();
        try{await operation();}catch(OperationCanceledException){}catch(Exception error){Notify(error.Message);}
        finally{if(--activeOperations==0){busyReveal.Stop();loading.Visibility=Visibility.Collapsed;}}
    }
    private SectionLoadState SectionState(Panel panel)
    {
        var state=panel.Children.OfType<SectionLoadState>().FirstOrDefault();
        if(state==null){state=new(Ink,Muted,Surface,Pink);panel.Children.Add(state);}return state;
    }
    private void RemoveSectionState(Panel panel){foreach(var state in panel.Children.OfType<SectionLoadState>().ToArray())panel.Children.Remove(state);}
    private async Task<bool> ReadSection(Panel panel,Func<Task> read,string label,CancellationToken ct,Func<Task>? retry=null)
    {
        RemoveSectionState(panel);var feedback=SectionState(panel);panel.Children.Remove(feedback);panel.Children.Insert(0,feedback);DockPanel.SetDock(feedback,Dock.Top);feedback.Loading(label);
        try{await read();ct.ThrowIfCancellationRequested();return true;}
        catch(Exception error) when(error is not OperationCanceledException)
        {
            if(!ct.IsCancellationRequested)feedback.Failed(label+"加载失败",error.Message,async()=>{ct.ThrowIfCancellationRequested();await Run(retry??(()=>ReadSection(panel,read,label,ct)));});
            return false;
        }
        finally{if(feedback.Status==SectionStatus.Loading||ct.IsCancellationRequested)panel.Children.Remove(feedback);}
    }
}
