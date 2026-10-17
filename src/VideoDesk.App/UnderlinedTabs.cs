using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace VideoDesk.App;

internal sealed class UnderlinedTabs : Grid
{
    private readonly Border line=new(){Height=3,Width=30,CornerRadius=new CornerRadius(1.5),Background=new SolidColorBrush(Color.FromRgb(255,71,140)),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Bottom};
    private readonly TranslateTransform position=new();
    private readonly ScaleTransform scale=new(1,1);
    private readonly StackPanel row=new(){Orientation=Orientation.Horizontal};
    private readonly List<Button> buttons=[];
    private readonly Brush accent=new SolidColorBrush(Color.FromRgb(255,71,140));
    private int selected;
    private double lastX=-1,lastWidth=-1;
    public void Select(int index){if(index<0||index>=buttons.Count)return;var changed=selected!=index;selected=index;Move(changed);}
    public UnderlinedTabs(IEnumerable<(string Label,bool Selected,Func<Task> Choose)> items,double fontSize=19)
    {
        Height=46;VerticalAlignment=VerticalAlignment.Center;Children.Add(row);var transform=new TransformGroup();transform.Children.Add(scale);transform.Children.Add(position);line.RenderTransform=transform;Children.Add(line);int index=0;
        foreach(var (label,isSelected,choose) in items)
        {
            var current=index++;if(isSelected)selected=current;
            var b=new Button{Content=label,Style=(Style)Application.Current.FindResource("PlainButton"),FontSize=fontSize,FontWeight=FontWeights.SemiBold,Height=43,Padding=new Thickness(12,0,12,0),Margin=new Thickness(0,0,5,0)};
            b.Click+=async(_,_)=>{selected=current;Move(true);await choose();};buttons.Add(b);row.Children.Add(b);
        }
        Loaded+=(_,_)=>Move(false);SizeChanged+=(_,_)=>Move(false);
    }
    private void Move(bool animate)
    {
        if(buttons.Count==0)return;
        for(int i=0;i<buttons.Count;i++)buttons[i].Foreground=i==selected?accent:(Brush)Application.Current.FindResource("Ink");
        var button=buttons[selected];var x=button.TranslatePoint(new Point(12,0),this).X;var width=Math.Max(12,button.ActualWidth-24);
        if(!animate&&Math.Abs(lastX-x)<.2&&Math.Abs(lastWidth-width)<.2)return;lastX=x;lastWidth=width;
        var visibleWidth=line.ActualWidth>0?line.ActualWidth*scale.ScaleX:width;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);line.Width=width;scale.ScaleX=visibleWidth/width;
        UiMotion.To(position,TranslateTransform.XProperty,x,animate);
        UiMotion.To(scale,ScaleTransform.ScaleXProperty,1,animate);
    }
}
