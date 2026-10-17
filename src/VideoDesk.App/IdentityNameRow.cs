using System.Windows;
using System.Windows.Controls;

namespace VideoDesk.App;

// The name takes the remaining width before badges, so a long nickname cannot
// enlarge its parent into the follow button. Short names keep badges adjacent.
internal sealed class IdentityNameRow : Grid
{
    private readonly TextBlock name;
    private readonly StackPanel badges;
    internal IdentityNameRow(TextBlock name,FrameworkElement level,FrameworkElement vip)
    {
        this.name=name;
        HorizontalAlignment=HorizontalAlignment.Left;ClipToBounds=true;
        RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new(){Height=GridLength.Auto});
        ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        name.TextTrimming=TextTrimming.CharacterEllipsis;name.TextWrapping=TextWrapping.NoWrap;name.ToolTip??=name.Text;
        Children.Add(name);badges=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};badges.Children.Add(level);badges.Children.Add(vip);SetColumn(badges,1);Children.Add(badges);
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        badges.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
        var compact=double.IsFinite(availableSize.Width)&&availableSize.Width<60+badges.DesiredSize.Width;
        SetColumnSpan(name,compact?2:1);SetRow(badges,compact?1:0);SetColumn(badges,compact?0:1);SetColumnSpan(badges,compact?2:1);
        badges.Margin=compact?new Thickness(0,2,0,0):new Thickness(0);
        return base.MeasureOverride(availableSize);
    }
}
