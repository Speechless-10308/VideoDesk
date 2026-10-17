using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using VideoDesk.Core;

namespace VideoDesk.App;

internal sealed class DynamicPictureState
{
    internal int SelectedIndex = -1;
    internal int QuarterTurns;
}

// A dynamic's full image stays in its card. Only the selected image is loaded;
// unloading or collapsing cancels it and releases the larger bitmap.
internal sealed class InlineDynamicPictures : StackPanel
{
    private readonly IReadOnlyList<CommentPicture> pictures;
    private readonly DynamicPictureState state;
    private readonly Func<string, int, CancellationToken, Task<BitmapSource?>> fetch;
    private readonly Func<CancellationToken> navigationToken;
    private readonly Action<int> openViewer;
    private readonly WrapPanel thumbnails = new();
    private readonly List<Button> thumbnailButtons = [];
    private readonly List<Image> thumbnailImages = [];
    private readonly Border toolbar;
    private readonly Grid stage;
    private readonly Image largeImage = new() { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button largeButton;
    private readonly SectionLoadState failure;
    private readonly LoadingGlyph loading;
    private readonly DispatcherTimer reveal = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(550) };
    private readonly Brush accent;
    private CancellationTokenSource? pending;
    private int revision;
    private double availableWidth = 600;

    internal bool Expanded => state.SelectedIndex >= 0;
    internal int SelectedIndex => state.SelectedIndex;
    internal int QuarterTurns => state.QuarterTurns;
    internal Image ExpandedImage => largeImage;

    internal InlineDynamicPictures(
        IReadOnlyList<CommentPicture> pictures,
        DynamicPictureState state,
        Action<Image, string, int> loadThumbnail,
        Func<string, int, CancellationToken, Task<BitmapSource?>> fetch,
        Func<CancellationToken> navigationToken,
        Action<int> openViewer,
        Brush ink, Brush muted, Brush surface, Brush accent)
    {
        this.pictures = pictures;
        this.state = state;
        this.fetch = fetch;
        this.navigationToken = navigationToken;
        this.openViewer = openViewer;
        this.accent = accent;
        if (state.SelectedIndex >= pictures.Count) state.SelectedIndex = -1;

        Tag = "dynamic-inline-pictures";
        var tools = new WrapPanel();
        tools.Children.Add(Tool("收起", PackIconKind.MinusCircleOutline, "dynamic-picture-collapse", Collapse, muted));
        tools.Children.Add(Tool("查看大图", PackIconKind.MagnifyPlusOutline, "dynamic-picture-viewer", OpenViewer, muted));
        tools.Children.Add(Tool("向左旋转", PackIconKind.RotateLeft, "dynamic-picture-rotate-left", () => Rotate(-1), muted));
        tools.Children.Add(Tool("向右旋转", PackIconKind.RotateRight, "dynamic-picture-rotate-right", () => Rotate(1), muted));
        toolbar = new Border { Child = tools, Background = surface, CornerRadius = new(6), Padding = new(4, 2, 4, 2), Margin = new(0, 0, 0, 10) };
        Children.Add(toolbar);

        largeButton = new Button
        {
            Style = (Style)Application.Current.FindResource("PlainButton"), Content = largeImage,
            Padding = new(0), Height = double.NaN, HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Top,
            ToolTip = "查看大图", Tag = "dynamic-picture-expanded"
        };
        largeButton.Click += (_, _) => OpenViewer();
        stage = new Grid { Margin = new(0, 0, 0, 10), HorizontalAlignment = HorizontalAlignment.Stretch };
        stage.Children.Add(largeButton);
        loading = new(accent) { Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(12) };
        stage.Children.Add(loading);
        failure = new(ink, muted, surface, accent);
        stage.Children.Add(failure);
        Children.Add(stage);
        Children.Add(thumbnails);

        for (int i = 0; i < pictures.Count; i++)
        {
            var index = i;
            var image = new Image { Stretch = pictures.Count == 1 ? Stretch.Uniform : Stretch.UniformToFill };
            loadThumbnail(image, pictures[i].Url, 640);
            var border = new Border { Child = image, Background = surface, CornerRadius = new(6), ClipToBounds = true, BorderThickness = new(1), BorderBrush = Brushes.Transparent };
            border.SizeChanged += (_, e) => border.Clip = new RectangleGeometry(new Rect(e.NewSize), 6, 6);
            var button = new Button
            {
                Style = (Style)Application.Current.FindResource("PlainButton"), Content = border,
                Padding = new(0), Height = double.NaN, Margin = new(0, 0, 8, 8),
                ToolTip = "展开图片", Tag = "dynamic-picture-thumbnail", Cursor = Cursors.Hand
            };
            AutomationProperties.SetName(button, $"展开第 {i + 1} 张动态图片");
            button.Click += (_, _) => Expand(index);
            thumbnailButtons.Add(button);
            thumbnailImages.Add(image);
            thumbnails.Children.Add(button);
        }
        reveal.Tick += (_, _) => { reveal.Stop(); if (pending != null && IsLoaded && IsVisible) loading.Visibility = Visibility.Visible; };
        Loaded += (_, _) => Resume();
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, _) => { if (!IsVisible) Stop(); else Resume(); };
        SizeChanged += (_, e) => { if (e.WidthChanged) { availableWidth = Math.Max(1, e.NewSize.Width); FitImage(); } };
        UpdateLayoutState();
    }

    private static Button Tool(string text, PackIconKind kind, string tag, Action action, Brush foreground)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new PackIcon { Kind = kind, Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = text, Margin = new(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Style = (Style)Application.Current.FindResource("PlainButton"), Content = content, Foreground = foreground, Height = 30, FontSize = 12, Padding = new(8, 0, 8, 0), Tag = tag };
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => action();
        return button;
    }

    private void Expand(int index)
    {
        if (state.SelectedIndex != index) state.QuarterTurns = 0;
        state.SelectedIndex = index;
        UpdateLayoutState();
        if (IsLoaded && IsVisible) _ = DisplayAsync();
        // The focused thumbnail moves below the large image. Keep the top of
        // that image in view instead of following the thumbnail to the bottom.
        _ = Dispatcher.BeginInvoke(() => { if (Expanded && SelectedIndex == index && IsLoaded && IsVisible) toolbar.BringIntoView(); }, DispatcherPriority.Loaded);
    }

    private void Collapse()
    {
        state.SelectedIndex = -1;
        state.QuarterTurns = 0;
        Stop();
        UpdateLayoutState();
        _ = Dispatcher.BeginInvoke(() => { if (!Expanded && IsLoaded && IsVisible) thumbnails.BringIntoView(); }, DispatcherPriority.Loaded);
    }

    private void OpenViewer() { if (Expanded) openViewer(state.SelectedIndex); }
    private void Resume() { if (Expanded && IsLoaded && IsVisible && pending == null && largeImage.Source == null) _ = DisplayAsync(); }
    private void Rotate(int delta)
    {
        state.QuarterTurns = (state.QuarterTurns + delta + 4) % 4;
        FitImage();
    }

    private void UpdateLayoutState()
    {
        toolbar.Visibility = stage.Visibility = Expanded ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < thumbnailButtons.Count; i++)
        {
            var image = thumbnailImages[i];
            image.Width = Expanded ? 64 : pictures.Count == 1 ? 320 : 160;
            image.Height = Expanded ? 56 : pictures.Count == 1 ? 230 : 160;
            thumbnailButtons[i].ToolTip = Expanded ? "切换图片" : "展开图片";
            ((Border)thumbnailButtons[i].Content).BorderBrush = Expanded && i == state.SelectedIndex ? accent : Brushes.Transparent;
        }
        FitImage();
    }

    private void FitImage()
    {
        if (!Expanded) return;
        var width = Math.Max(1, Math.Min(availableWidth, 900));
        var rotated = state.QuarterTurns % 2 != 0;
        var bitmap = largeImage.Source as BitmapSource;
        largeImage.Width = rotated && bitmap != null ? width * bitmap.PixelWidth / Math.Max(1d, bitmap.PixelHeight) : width;
        largeImage.LayoutTransform = state.QuarterTurns == 0 ? Transform.Identity : new RotateTransform(state.QuarterTurns * 90);
        largeButton.MinHeight = bitmap == null ? 120 : 0;
    }

    private async Task DisplayAsync()
    {
        Stop();
        if (!Expanded || !IsLoaded || !IsVisible) return;
        var index = state.SelectedIndex;
        var current = revision;
        var request = pending = CancellationTokenSource.CreateLinkedTokenSource(navigationToken());
        var token = request.Token;
        largeImage.Source = thumbnailImages[index].Source;
        failure.Clear();
        AutomationProperties.SetName(largeButton, $"查看第 {index + 1} 张动态大图");
        FitImage();
        reveal.Start();
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var pixels = Math.Clamp((int)Math.Ceiling(Math.Min(availableWidth, 900) * dpi), 320, 1800);
            var bitmap = await fetch(pictures[index].Url, pixels, token);
            if (token.IsCancellationRequested || current != revision || !IsLoaded || !IsVisible || !Expanded) return;
            if (bitmap == null) failure.Failed("", "图片加载失败", DisplayAsync);
            else { largeImage.Source = bitmap; FitImage(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested && current == revision && IsLoaded && IsVisible) failure.Failed("", "图片加载失败：" + error.Message, DisplayAsync);
        }
        finally
        {
            if (ReferenceEquals(pending, request)) { pending = null; reveal.Stop(); loading.Visibility = Visibility.Collapsed; }
            request.Dispose();
        }
    }

    private void Stop()
    {
        revision++;
        pending?.Cancel();
        pending = null;
        reveal.Stop();
        loading.Visibility = Visibility.Collapsed;
        largeImage.Source = null;
    }
}
