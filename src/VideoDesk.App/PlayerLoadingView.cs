using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace VideoDesk.App;

internal sealed class PlayerLoadingView : Border
{
    private static readonly Brush Backdrop = new SolidColorBrush(Color.FromRgb(20, 21, 26));
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(255, 71, 140));
    private readonly LoadingGlyph spinner = new(Accent) { Margin = new(0, 0, 0, 18) };
    private readonly TextBlock caption = new()
    {
        Foreground = Brushes.White, FontSize = 14, TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center, MaxWidth = 340
    };
    private readonly Button retry = new()
    {
        Content = "重试", Height = 36, MinWidth = 88, Margin = new(0, 16, 0, 0),
        Padding = new(20, 0, 20, 0), Foreground = Brushes.White, Background = Accent,
        Style = (Style)Application.Current.FindResource("PlainButton")
    };
    private Func<Task>? retryAction;

    public PlayerLoadingView()
    {
        Background = Backdrop;
        var body = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new(24) };
        body.Children.Add(spinner); body.Children.Add(caption); body.Children.Add(retry); Child = body;
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        retry.Click += async (_, _) =>
        {
            var action = retryAction;
            if (action == null) return;
            retry.IsEnabled = false;
            try { await action(); }
            catch (OperationCanceledException) { }
            catch (Exception error) { Failed("视频暂时无法播放", error.Message, action); }
            finally { retry.IsEnabled = true; }
        };
        Loading();
    }

    public void Loading(string label = "正在加载视频", bool buffering = false)
    {
        Background = buffering ? Brushes.Transparent : Backdrop;
        IsHitTestVisible = !buffering;
        spinner.Visibility = Visibility.Visible; caption.Text = label;
        retry.Visibility = Visibility.Collapsed; retryAction = null; ToolTip = null;
        Visibility = Visibility.Visible;
    }

    public void Failed(string label, string details, Func<Task> action)
    {
        Background = Backdrop; IsHitTestVisible = true;
        spinner.Visibility = Visibility.Collapsed; caption.Text = string.IsNullOrEmpty(label) ? "视频暂时无法播放" : label;
        ToolTip = details; retryAction = action; retry.Visibility = Visibility.Visible; Visibility = Visibility.Visible;
    }

    public void Clear() { retryAction = null; Visibility = Visibility.Collapsed; }
}
