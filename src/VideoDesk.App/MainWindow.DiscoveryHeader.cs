using System.Windows;
using System.Windows.Controls;
using VideoDesk.Core;
using MaterialDesignThemes.Wpf;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private FrameworkElement DiscoveryHeader(Func<Task> refresh,bool showMode)
    {
        var header=new Grid{Height=58,Margin=new Thickness(4,0,0,8),Tag="discovery-header"};header.ColumnDefinitions.Add(new());header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        AttachHomeTabs(header);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Tag="discovery-actions"};
        if(showMode)actions.Children.Add(DiscoveryModeControl());
        var reload=IconButton(PackIconKind.Refresh,"刷新",44);reload.Margin=new Thickness(16,0,0,0);reload.Click+=async(_,_)=>await Run(refresh);actions.Children.Add(reload);Grid.SetColumn(actions,1);header.Children.Add(actions);return header;
    }
    private void AttachHomeTabs(Grid header)
    {
        homeTabs??=new UnderlinedTabs(new[]{("推荐",0),("热门",1),("关注",2)}.Select(t=>(t.Item1,t.Item2==0,(Func<Task>)(async()=>{popular=t.Item2==1;following=t.Item2==2;await Run(ShowHome);}))));
        if(!ReferenceEquals(homeTabs.Parent,header)){if(homeTabs.Parent is Panel parent)parent.Children.Remove(homeTabs);header.Children.Add(homeTabs);}
        homeTabs.HorizontalAlignment=HorizontalAlignment.Left;homeTabs.Select(following?2:popular?1:0);
        if(!following&&header.Children.OfType<StackPanel>().FirstOrDefault(p=>p.Tag as string=="discovery-actions") is {} actions){var mode=DiscoveryModeControl();actions.Children.Insert(0,mode);}
    }
    private FrameworkElement DiscoveryModeControl()=>ModeSwitch(homeMode,async mode=>{var direction=mode.CompareTo(homeMode);homeMode=mode;await Run(ShowHome);if(homeMode==mode&&!following&&!searching)UiMotion.Enter(popular?popularBody:feedPanel,direction);},"discovery");
}
