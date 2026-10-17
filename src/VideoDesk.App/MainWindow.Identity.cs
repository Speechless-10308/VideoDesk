using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using VideoDesk.Core;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private static Brush IdentityColor(string value,Brush fallback)
    {
        try{if(value.StartsWith('#')){var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));brush.Freeze();return brush;}}catch(FormatException){}return fallback;
    }
    private FrameworkElement? OfficialBadge(UserIdentity? identity,double size)
    {
        if(identity?.OfficialType is not (0 or 1))return null;
        var title=identity.OfficialTitle.Length>0?identity.OfficialTitle:identity.OfficialType==0?"个人认证":"机构认证";var description=title+(identity.OfficialDescription.Length>0&&identity.OfficialDescription!=title?"\n"+identity.OfficialDescription:"");
        var symbol=Icon(PackIconKind.LightningBolt,size*.7);symbol.Foreground=Brushes.White;symbol.HorizontalAlignment=HorizontalAlignment.Center;symbol.VerticalAlignment=VerticalAlignment.Center;
        var badge=new Border{Width=size,Height=size,CornerRadius=new CornerRadius(size/2),Background=new SolidColorBrush(identity.OfficialType==0?Color.FromRgb(255,193,7):Color.FromRgb(0,174,236)),BorderBrush=CanvasBrush,BorderThickness=new Thickness(1.5),Child=symbol,ToolTip=description};AutomationProperties.SetName(badge,(identity.OfficialType==0?"个人认证：":"机构认证：")+title);return badge;
    }
    private FrameworkElement? VipBadge(UserIdentity? identity)
    {
        if(identity?.IsVip!=true)return null;var label=Text(identity.VipLabel.Length>0?identity.VipLabel:"大会员",11,FontWeights.Medium);label.Foreground=IdentityColor(identity.VipTextColor,Brushes.White);
        var badge=new Border{Child=label,Padding=new Thickness(5,1,5,1),CornerRadius=new CornerRadius(8),Background=IdentityColor(identity.VipBackground,Pink),ToolTip=label.Text,VerticalAlignment=VerticalAlignment.Center};AutomationProperties.SetName(badge,label.Text);
        if(identity.VipImage.Length==0)return badge;var root=new Grid{VerticalAlignment=VerticalAlignment.Center};root.Children.Add(badge);var image=new Image{Height=18,Stretch=Stretch.Uniform,Opacity=0,ToolTip=label.Text};AutomationProperties.SetName(image,label.Text);root.Children.Add(image);
        LoadImage(image,identity.VipImage,180,navigation.Token,()=>{image.Opacity=1;badge.Visibility=Visibility.Collapsed;});return root;
    }
    private Grid UserAvatar(string url,double size,UserIdentity? identity=null,long mid=0)
    {
        var root=new Grid{Width=size,Height=size};root.Children.Add(AvatarImage(url,size));var slot=new ContentControl{HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,-2,-2)};root.Children.Add(slot);
        void Apply(UserIdentity? value)=>slot.Content=OfficialBadge(value,Math.Clamp(size*.3,14,24));Apply(identity);
        if(identity==null&&mid>0){var ct=navigation.Token;root.Loaded+=async(_,_)=>{try{var summary=await CreatorSummaryCached(mid,ct);if(!ct.IsCancellationRequested&&root.IsLoaded)Apply(summary.Identity);}catch(OperationCanceledException){}catch(Exception error){System.Diagnostics.Debug.WriteLine(error.Message);}};}
        return root;
    }
}
