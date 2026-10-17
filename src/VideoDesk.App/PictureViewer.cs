using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using VideoDesk.Core;

namespace VideoDesk.App;

internal sealed class PictureViewer : Window
{
    private readonly IReadOnlyList<CommentPicture> pictures;
    private readonly Func<string, CancellationToken, Task<ViewerPicture?>> fetch;
    private readonly Func<string, int, CancellationToken, Task<BitmapSource?>> thumbnail;
    private readonly Func<string, CancellationToken, Task<byte[]?>> original;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? pending;
    private readonly Brush ink, muted, accent, tonal, surface, stageBrush;
    private readonly Grid root = new(), stage = new();
    private readonly TextBlock count = new(), percent = new(), message = new();
    private readonly Button previous, next, fit, actual, download, retry;
    private readonly List<Button> imageActions = [];
    private readonly StackPanel strip = new() { Orientation = Orientation.Horizontal };
    private readonly ScrollViewer stripScroll;
    private readonly LoadingGlyph loading;
    private readonly DispatcherTimer noticeTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer loadingTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly Dictionary<int, Image> thumbnailImages = [];
    private readonly Dictionary<int, Border> thumbnailBorders = [];
    private CancellationTokenSource? thumbnailRequests;
    private int revision;
    private bool closed, saving;
    internal PictureViewport Viewport { get; } = new();
    internal int SelectedIndex { get; private set; }
    internal Task SelectionTask { get; private set; } = Task.CompletedTask;
    internal Grid Layout => root;
    internal bool IsClosed => closed;
    internal bool Matches(IReadOnlyList<CommentPicture> items) => pictures.Count == items.Count && pictures.Select(p => p.Url).SequenceEqual(items.Select(p => p.Url));

    internal PictureViewer(Window owner, IReadOnlyList<CommentPicture> pictures, int selected,
        Func<string, CancellationToken, Task<ViewerPicture?>> fetch,
        Func<string, int, CancellationToken, Task<BitmapSource?>> thumbnail,
        Func<string, CancellationToken, Task<byte[]?>> original, bool dark)
    {
        if (pictures.Count == 0) throw new ArgumentException("图片列表为空", nameof(pictures));
        this.pictures = pictures; this.fetch = fetch; this.thumbnail = thumbnail; this.original = original;
        SelectedIndex = Math.Clamp(selected, 0, pictures.Count - 1);
        static Brush ColorBrush(string color) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); b.Freeze(); return b; }
        ink = ColorBrush(dark ? "#F2E9F0" : "#27232A"); muted = ColorBrush(dark ? "#BAABB8" : "#8B8293");
        accent = ColorBrush("#FF478C"); tonal = ColorBrush(dark ? "#593143" : "#FFE5F0");
        surface = ColorBrush(dark ? "#30262E" : "#F6EFF5"); stageBrush = ColorBrush(dark ? "#1C171D" : "#FAF4F9");
        Owner = owner; Icon = owner.Icon; Title = "图片查看器";
        Width = Math.Min(1120, SystemParameters.WorkArea.Width - 60); Height = Math.Min(820, SystemParameters.WorkArea.Height - 60);
        MinWidth = 680; MinHeight = 420; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize; ShowInTaskbar = false;
        Background = ColorBrush(dark ? "#231D24" : "#FFFBFF"); Foreground = ink;
        root.Background = Background;
        FontFamily = (FontFamily)Application.Current.FindResource("AppFont"); UseLayoutRounding = true; SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0) });
        DesktopWindowBounds.Attach(this, () => false);
        root.RowDefinitions.Add(new() { Height = new GridLength(52) });
        root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.Children.Add(BuildCaption());

        var frame = new Border { Background = stageBrush, CornerRadius = new CornerRadius(24), Margin = new Thickness(16, 0, 16, 16), Child = stage };
        stage.SizeChanged += (_, e) => { var clip = new RectangleGeometry(new Rect(e.NewSize), 24, 24); clip.Freeze(); stage.Clip = clip; };
        Grid.SetRow(frame, 1); root.Children.Add(frame);
        Viewport.Margin = new Thickness(16, 16, 16, 88); stage.Children.Add(Viewport);
        previous = ActionButton(PackIconKind.ChevronLeft, "上一张 · ←", "previous", () => Move(-1));
        next = ActionButton(PackIconKind.ChevronRight, "下一张 · →", "next", () => Move(1));
        foreach (var b in new[] { previous, next }) { b.Width = b.Height = 48; b.Background = surface; b.VerticalAlignment = VerticalAlignment.Center; b.Margin = new Thickness(12, 0, 12, 72); stage.Children.Add(b); }
        previous.HorizontalAlignment = HorizontalAlignment.Left; next.HorizontalAlignment = HorizontalAlignment.Right;
        previous.Visibility = next.Visibility = pictures.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
        void ImageAction(Button b) { imageActions.Add(b); toolbar.Children.Add(b); }
        ImageAction(ActionButton(PackIconKind.MagnifyMinusOutline, "缩小 · −", "zoom-out", () => Viewport.ZoomBy(1 / 1.25)));
        percent.Width = 64; percent.FontSize = 14; percent.Foreground = ink; percent.TextAlignment = TextAlignment.Center; percent.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(percent, "缩放比例"); toolbar.Children.Add(percent);
        ImageAction(ActionButton(PackIconKind.MagnifyPlusOutline, "放大 · +", "zoom-in", () => Viewport.ZoomBy(1.25)));
        fit = ActionButton(PackIconKind.Fullscreen, "适应窗口 · 0", "fit", Viewport.Fit); fit.Margin = new Thickness(12, 0, 0, 0); ImageAction(fit);
        actual = TextButton("1:1", "原始大小 · 1", "actual", Viewport.OriginalSize); ImageAction(actual);
        ImageAction(ActionButton(PackIconKind.RotateLeft, "向左旋转 · Shift+R", "rotate-left", () => Viewport.Rotate(-1)));
        ImageAction(ActionButton(PackIconKind.RotateRight, "向右旋转 · R", "rotate-right", () => Viewport.Rotate(1)));
        ImageAction(ActionButton(PackIconKind.ContentCopy, "复制图片 · Ctrl+C", "copy", CopyPicture));
        download = TextButton("下载原图", "下载原图 · Ctrl+S", "download", () => _ = Download());
        var downloadContent = new StackPanel { Orientation = Orientation.Horizontal };
        downloadContent.Children.Add(new PackIcon { Kind = PackIconKind.Download, Width = 20, Height = 20, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        downloadContent.Children.Add(new TextBlock { Text = "下载原图", VerticalAlignment = VerticalAlignment.Center }); download.Content = downloadContent;
        download.Background = accent; download.Foreground = Brushes.White; download.Width = 132; download.Margin = new Thickness(12, 0, 0, 0);
        toolbar.Children.Add(download);
        var tools = new Border { Child = toolbar, Padding = new Thickness(10), CornerRadius = new CornerRadius(20), Background = Background, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 12, 16), Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = .10 } };
        stage.Children.Add(tools);

        loading = new LoadingGlyph(accent) { Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 72) };
        stage.Children.Add(loading);
        var feedback = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24, 0, 24, 72) };
        message.Foreground = muted; message.FontSize = 13; message.TextWrapping = TextWrapping.Wrap; message.TextAlignment = TextAlignment.Center;
        feedback.Children.Add(message); retry = TextButton("重试", "重新加载图片", "retry", () => Select(SelectedIndex)); retry.Width = 80; retry.Background = tonal; retry.Visibility = Visibility.Collapsed; retry.Margin = new Thickness(0, 12, 0, 0); feedback.Children.Add(retry); stage.Children.Add(feedback);

        stripScroll = new ScrollViewer { Content = strip, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 800, Margin = new Thickness(20, 0, 20, 16), Visibility = pictures.Count > 1 ? Visibility.Visible : Visibility.Collapsed };
        Grid.SetRow(stripScroll, 2); root.Children.Add(stripScroll);
        Content = new Border { Background = Background, BorderBrush = surface, BorderThickness = new Thickness(1), Child = root };
        Viewport.Changed += UpdateActions;
        loadingTimer.Tick += (_, _) => { loadingTimer.Stop(); if (Viewport.Picture == null && !closed) loading.Visibility = Visibility.Visible; };
        noticeTimer.Tick += (_, _) => { noticeTimer.Stop(); message.Text = ""; };
        PreviewKeyDown += HandleKey;
        DpiChanged += (_, _) => Viewport.RefreshSize();
        Loaded += (_, _) => { Select(SelectedIndex); Viewport.Focus(); };
        Closed += (_, _) => Release(); UpdateActions();
    }

    private Grid BuildCaption()
    {
        var caption = new Grid { Background = Brushes.Transparent };
        caption.ColumnDefinitions.Add(new()); caption.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        caption.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) ToggleMaximize(); else DragMove(); };
        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0), IsHitTestVisible = false };
        title.Children.Add(new Image { Source = Icon, Width = 22, Height = 22, Margin = new Thickness(0, 0, 10, 0) });
        title.Children.Add(new TextBlock { Text = Title, FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        count.FontSize = 12; count.Foreground = muted;
        title.Children.Add(new Border { Child = count, Background = surface, CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(14, 0, 0, 0) });
        caption.Children.Add(title);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        foreach (var (icon, label, action) in new[] { (PackIconKind.Minus, "最小化", (Action)(() => WindowState = WindowState.Minimized)), (PackIconKind.WindowMaximize, "最大化 / 还原", ToggleMaximize), (PackIconKind.Close, "关闭 · Esc", Close) })
        { var b = ActionButton(icon, label, "window-" + label, action); b.Background = Brushes.Transparent; b.Focusable = false; controls.Children.Add(b); }
        Grid.SetColumn(controls, 1); caption.Children.Add(controls); return caption;
    }
    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private Button TextButton(string text, string label, string tag, Action action)
    {
        var b = new Button { Content = text, Foreground = ink, Style = (Style)Application.Current.FindResource("PictureViewerButton"), Width = 44, ToolTip = label, Tag = tag, FontSize = 14, Margin = new Thickness(2, 0, 2, 0) };
        AutomationProperties.SetName(b, label); b.Click += (_, _) => action(); return b;
    }
    private Button ActionButton(PackIconKind kind, string label, string tag, Action action)
    { var b = TextButton("", label, tag, action); b.Content = new PackIcon { Kind = kind, Width = 22, Height = 22 }; b.Background = surface; return b; }

    internal void Move(int delta) => Select(Math.Clamp(SelectedIndex + delta, 0, pictures.Count - 1), true);
    internal void Select(int index, bool skipSame = false)
    {
        if (closed || index < 0 || index >= pictures.Count || skipSame && index == SelectedIndex) return;
        SelectedIndex = index; SelectionTask = Display();
    }
    private async Task Display()
    {
        var current = ++revision; var index = SelectedIndex;
        pending?.Cancel(); pending?.Dispose(); pending = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); var token = pending.Token;
        noticeTimer.Stop(); loadingTimer.Stop(); loading.Visibility = Visibility.Collapsed; retry.Visibility = Visibility.Collapsed; message.Text = "";
        Viewport.SetPicture(null); count.Text = $"{index + 1} / {pictures.Count}";
        previous.IsEnabled = index > 0; next.IsEnabled = index + 1 < pictures.Count; UpdateThumbnails(); loadingTimer.Start();
        try
        {
            var picture = await fetch(pictures[index].Url, token);
            if (closed || current != revision || token.IsCancellationRequested) return;
            if (picture == null) throw new IOException("图片不可用");
            Viewport.SetPicture(picture);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!closed && current == revision) { message.Text = "图片加载失败"; retry.Visibility = Visibility.Visible; } }
        finally { if (!closed && current == revision) { loadingTimer.Stop(); loading.Visibility = Visibility.Collapsed; UpdateActions(); } }
    }

    private void UpdateThumbnails()
    {
        if (pictures.Count == 1) return;
        // Only nearby small thumbnails are alive, even if a gallery grows large.
        thumbnailRequests?.Cancel(); thumbnailRequests?.Dispose(); thumbnailRequests = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = thumbnailRequests.Token; var wanted = Enumerable.Range(Math.Max(0, SelectedIndex - 4), Math.Min(pictures.Count, SelectedIndex + 5) - Math.Max(0, SelectedIndex - 4)).ToHashSet();
        foreach (var index in thumbnailImages.Keys.Where(i => !wanted.Contains(i)).ToArray()) { thumbnailImages[index].Source = null; strip.Children.Remove(thumbnailBorders[index]); thumbnailImages.Remove(index); thumbnailBorders.Remove(index); }
        foreach (int index in wanted.Order())
        {
            if (!thumbnailImages.ContainsKey(index))
            {
                var image = new Image { Stretch = Stretch.UniformToFill, Width = 78, Height = 58 };
                var crop = new Border { Child = image, CornerRadius = new CornerRadius(10), Clip = new RectangleGeometry(new Rect(0, 0, 78, 58), 10, 10) };
                var b = TextButton("", $"第 {index + 1} 张", "thumbnail-" + index, () => Select(index, true)); b.Content = crop; b.Width = 86; b.Height = 66; b.Padding = new Thickness(2); b.Background = Brushes.Transparent;
                var border = new Border { Child = b, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(2), Margin = new Thickness(3, 0, 3, 0) };
                thumbnailImages[index] = image; thumbnailBorders[index] = border;
                int position = thumbnailBorders.Keys.Count(i => i < index); strip.Children.Insert(position, border);
            }
            thumbnailBorders[index].BorderBrush = index == SelectedIndex ? accent : Brushes.Transparent;
            if (thumbnailImages[index].Source == null) _ = LoadThumbnail(index, thumbnailImages[index], token);
        }
        _ = Dispatcher.BeginInvoke(() => { if (!closed && thumbnailBorders.TryGetValue(SelectedIndex, out var selected)) selected.BringIntoView(); }, DispatcherPriority.Loaded);
    }
    private async Task LoadThumbnail(int index, Image image, CancellationToken token)
    {
        try { var bitmap = await thumbnail(pictures[index].Url, 160, token); if (!closed && !token.IsCancellationRequested && thumbnailImages.GetValueOrDefault(index) == image) image.Source = bitmap; }
        catch (OperationCanceledException) { } catch (Exception) { }
    }
    private void UpdateActions()
    {
        bool ready = Viewport.Picture != null;
        percent.Text = ready ? $"{Viewport.Zoom * 100:0.#}%" : "—";
        foreach (var b in imageActions) b.IsEnabled = ready;
        fit.Background = Viewport.Fitted ? tonal : surface; fit.Foreground = Viewport.Fitted ? accent : ink;
        actual.Background = !Viewport.Fitted && Math.Abs(Viewport.Zoom - 1) < .001 ? tonal : surface;
        download.IsEnabled = !saving && ready;
    }
    private void Notice(string text) { message.Text = text; noticeTimer.Stop(); noticeTimer.Start(); }
    private void CopyPicture()
    {
        if (Viewport.Picture is not { } p) return;
        try
        {
            Clipboard.SetImage(ClipboardBitmap()); Notice("已复制图片");
        }
        catch (ExternalException) { Notice("剪贴板暂不可用"); }
    }
    internal BitmapSource ClipboardBitmap()
    {
        var bitmap = Viewport.Picture?.Bitmap ?? throw new InvalidOperationException("图片不可用");
        if (Viewport.QuarterTurns == 0) return bitmap;
        var rotated = new TransformedBitmap(bitmap, new RotateTransform(Viewport.QuarterTurns * 90)); rotated.Freeze(); return rotated;
    }
    private async Task Download()
    {
        if (saving || Viewport.Picture == null) return;
        saving = true; UpdateActions();
        try
        {
            var index = SelectedIndex; var bytes = await original(pictures[index].Url, lifetime.Token);
            if (closed) return; if (bytes == null) throw new IOException();
            var extension = OriginalExtension(bytes);
            var dialog = new SaveFileDialog { Title = "保存原图", FileName = $"VideoDesk_{DateTime.Now:yyyyMMdd_HHmmss}_{index + 1}{extension}", DefaultExt = extension, Filter = $"图片文件 (*{extension})|*{extension}|所有文件 (*.*)|*.*", AddExtension = true, OverwritePrompt = true };
            if (dialog.ShowDialog(this) != true) return;
            await WriteOriginal(dialog.FileName, bytes, lifetime.Token); if (!closed) Notice("图片已保存");
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!closed) Notice("图片保存失败"); }
        finally { saving = false; if (!closed) UpdateActions(); }
    }
    internal async Task SaveOriginalTo(string path, CancellationToken token)
    {
        var bytes = await original(pictures[SelectedIndex].Url, token) ?? throw new IOException("图片不可用");
        await WriteOriginal(path, bytes, token);
    }
    private static async Task WriteOriginal(string path, byte[] bytes, CancellationToken token)
    {
        // Cancellation must not leave an existing user image truncated.
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllBytesAsync(temp, bytes, token); token.ThrowIfCancellationRequested(); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    internal static string OriginalExtension(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71 })) return ".png";
        if (bytes.AsSpan().StartsWith(new byte[] { 255, 216, 255 })) return ".jpg";
        if (bytes.AsSpan().StartsWith("GIF8"u8)) return ".gif";
        if (bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8)) return ".webp";
        if (bytes.AsSpan().StartsWith("BM"u8)) return ".bmp";
        return ".img";
    }
    private void HandleKey(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        e.Handled = true;
        switch (key)
        {
            case Key.Escape: Close(); break;
            case Key.Left: Move(-1); break;
            case Key.Right: Move(1); break;
            case Key.Home: Select(0, true); break;
            case Key.End: Select(pictures.Count - 1, true); break;
            case Key.Add: case Key.OemPlus: Viewport.ZoomBy(1.25); break;
            case Key.Subtract: case Key.OemMinus: Viewport.ZoomBy(1 / 1.25); break;
            case Key.D0: case Key.NumPad0: Viewport.Fit(); break;
            case Key.D1: case Key.NumPad1: Viewport.OriginalSize(); break;
            case Key.R: Viewport.Rotate((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1); break;
            case Key.S when control: _ = Download(); break;
            case Key.C when control: CopyPicture(); break;
            default: e.Handled = false; break;
        }
    }
    private void Release()
    {
        if (closed) return; closed = true; revision++;
        noticeTimer.Stop(); loadingTimer.Stop(); loading.Visibility = Visibility.Collapsed;
        lifetime.Cancel(); pending?.Cancel(); pending?.Dispose(); thumbnailRequests?.Cancel(); thumbnailRequests?.Dispose();
        Viewport.SetPicture(null); foreach (var image in thumbnailImages.Values) image.Source = null;
        thumbnailImages.Clear(); thumbnailBorders.Clear(); strip.Children.Clear(); lifetime.Dispose();
    }
}
