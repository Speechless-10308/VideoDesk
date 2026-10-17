using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using VideoDesk.Core;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private Button CardButton(FrameworkElement content,string title,Func<Task> open)
    {
        var card=Button("");card.Content=content;card.Height=double.NaN;card.Padding=new Thickness(0);card.Margin=new Thickness(7,0,7,20);card.HorizontalContentAlignment=HorizontalAlignment.Stretch;card.VerticalAlignment=VerticalAlignment.Top;card.VerticalContentAlignment=VerticalAlignment.Top;card.ToolTip=title;AutomationProperties.SetName(card,title);card.Click+=async(_,e)=>{if(ReferenceEquals(e.OriginalSource,card))await Run(open);};return card;
    }
    private Grid CardCover(string url,double duration,string counts,out Image image,double? position=null)
    {
        var cover=new Grid{Height=175,Background=Surface,ClipToBounds=true};image=new Image{Stretch=Stretch.UniformToFill};cover.Children.Add(image);LoadImage(image,url,480);
        var footer=new Grid{VerticalAlignment=VerticalAlignment.Bottom,Height=33,Background=new LinearGradientBrush(Color.FromArgb(0,0,0,0),Color.FromArgb(190,0,0,0),90)};footer.ColumnDefinitions.Add(new());footer.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var stats=Text(counts,12);stats.Foreground=Brushes.White;stats.Margin=new Thickness(9,9,0,0);footer.Children.Add(stats);
        var time=Text(position is {} watched?(duration>0&&watched>=duration-10?"已看完":BilibiliClient.DurationLabel(watched)+" / "+BilibiliClient.DurationLabel(duration)):BilibiliClient.DurationLabel(duration),12);time.Foreground=Brushes.White;time.Margin=new Thickness(4,9,9,0);Grid.SetColumn(time,1);footer.Children.Add(time);cover.Children.Add(footer);
        Border? progress=null;if(position!=null){progress=new Border{Height=3,Background=Pink,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Bottom};cover.Children.Add(progress);}
        cover.SizeChanged+=(_,e)=>{cover.Clip=new RectangleGeometry(new Rect(e.NewSize),12,12);if(progress!=null)progress.Width=e.NewSize.Width*Math.Clamp(position!.Value/Math.Max(1,duration),0,1);};return cover;
    }
    private static TextBlock CardTitle(string title)=>new(){Text=title,FontSize=15,FontWeight=FontWeights.Medium,Foreground=Ink,TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis,Height=44,LineHeight=22,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(8,10,8,0),ToolTip=title};
    private Grid CardAuthor(string creator,long mid,bool followed,out TextBlock author,string likeBadge="")
    {
        var row=new Grid();row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new());
        if(likeBadge.Length>0){var label=Text(likeBadge,11);label.Foreground=new SolidColorBrush(Color.FromRgb(255,114,50));row.Children.Add(new Border{Child=label,Background=new SolidColorBrush(Color.FromRgb(255,241,233)),BorderBrush=new SolidColorBrush(Color.FromRgb(255,202,169)),BorderThickness=new Thickness(1),Padding=new Thickness(3,0,3,0),CornerRadius=new CornerRadius(2),Margin=new Thickness(0,0,5,0),VerticalAlignment=VerticalAlignment.Center});}
        else if(followed){var label=Text("已关注",11);label.Foreground=new SolidColorBrush(Color.FromRgb(255,114,50));row.Children.Add(new Border{Child=label,Background=new SolidColorBrush(Color.FromRgb(255,241,233)),Padding=new Thickness(3,1,3,1),CornerRadius=new CornerRadius(2),Margin=new Thickness(0,0,5,0)});}
        else row.Children.Add(new Image{Source=OfficialIcons.Source("up",true),Width=14,Height=14,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,5,0)});
        author=Text(creator,13);author.Foreground=Muted;author.TextTrimming=TextTrimming.CharacterEllipsis;Grid.SetColumn(author,1);row.Children.Add(author);CreatorLink(row,mid);return row;
    }
}
