using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private static readonly Brush BackdropBrush = new SolidColorBrush(Color.FromRgb(250, 244, 249));
    private static readonly Brush OutlineBrush = new SolidColorBrush(Color.FromRgb(235, 226, 235));
    private static readonly Brush RaisedBrush = new SolidColorBrush(Colors.White);
    private readonly Border pageSurface = new();

    private void SetPageSurface(bool settings = false, bool playback = false)
    {
        pageSurface.Margin = new Thickness(18, 18, 18, 14);
        pageSurface.Padding = settings ? new Thickness(4, 6, 4, 0) : playback ? new Thickness(0) : new Thickness(16, 6, 16, 12);
        pageSurface.Background = settings || playback ? Brushes.Transparent : CanvasBrush;
        pageSurface.CornerRadius = new CornerRadius(24);
        content.Margin = new Thickness(0);
    }

    private static void RoundClip(FrameworkElement element, double radius)
    {
        element.SizeChanged += (_, e) =>
        {
            var clip = new RectangleGeometry(new Rect(e.NewSize), radius, radius);
            clip.Freeze(); element.Clip = clip;
        };
    }

    private static Button BackButton(string label = "返回上一页 · Alt+←")
    {
        var button = IconButton(PackIconKind.ChevronLeft, label, 40);
        button.Height = 40; button.Background = RaisedBrush; button.BorderBrush = OutlineBrush; button.BorderThickness = new Thickness(1);
        ButtonAssist.SetCornerRadius(button, new CornerRadius(20));
        return button;
    }

    private void SetPlayerSurface(bool full)
    {
        if(laterFab!=null)laterFab.Visibility=full?Visibility.Collapsed:Visibility.Visible;
        if (full)
        {
            pageSurface.Margin = pageSurface.Padding = new Thickness(0);
            pageSurface.Background = Brushes.Black;
            playerFrame!.Margin = new Thickness(0);
            leftPlayer!.Background = Brushes.Black;
        }
        else
        {
            SetPageSurface(playback: true);
            leftPlayer!.Background = CanvasBrush;
            leftPlayer.Margin = new Thickness(0, 0, 14, 0);
            playerFrame!.Margin = new Thickness(16, 0, 16, 0);
        }
    }

    private void UpdatePlayerHeight()
    {
        if(fullscreen||leftPlayer==null||playerFrame==null||playerTitle==null||leftPlayer.ActualHeight<=0)return;
        var reserve=leftPlayer.ActualHeight*.30+(danmakuRow?.Margin.Top??12)+(danmakuRow?.Margin.Bottom??12);
        var titleHeight=playerTitle.ActualHeight+playerTitle.Margin.Top+playerTitle.Margin.Bottom;
        var available=Math.Max(1,leftPlayer.ActualHeight-titleHeight-reserve);
        var height=Math.Min(Math.Max(1,leftPlayer.ActualWidth-32)*9/16,available);
        if(double.IsNaN(playerFrame.Height)||Math.Abs(playerFrame.Height-height)>.5)playerFrame.Height=height;
    }
}
