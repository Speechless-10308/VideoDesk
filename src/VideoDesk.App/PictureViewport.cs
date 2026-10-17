using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VideoDesk.App;

// Zoom and pan only redraw one bitmap; they never resize the window's layout or
// allocate a new rotated image. 100% means one source pixel per screen pixel.
internal sealed class PictureViewport : FrameworkElement
{
    internal ViewerPicture? Picture { get; private set; }
    internal double Zoom { get; private set; } = 1;
    internal int QuarterTurns { get; private set; }
    internal bool Fitted { get; private set; } = true;
    internal Vector Pan { get; private set; }
    internal event Action? Changed;
    private Point? dragStart;
    private Vector dragOrigin;
    internal double FitZoom => Picture is not { } p ? 1 : Math.Min(1, Math.Min(ActualWidth * Dpi / RotatedSize.Width, ActualHeight * Dpi / RotatedSize.Height));
    private double Dpi => VisualTreeHelper.GetDpi(this).DpiScaleX;
    private Size RotatedSize => Picture is not { } p ? new(1, 1) : QuarterTurns % 2 == 0 ? new(p.Width, p.Height) : new(p.Height, p.Width);
    internal Rect ImageBounds
    {
        get
        {
            if (Picture == null) return Rect.Empty;
            var size = RotatedSize;
            return new((ActualWidth - size.Width * Zoom / Dpi) / 2 + Pan.X,
                (ActualHeight - size.Height * Zoom / Dpi) / 2 + Pan.Y, size.Width * Zoom / Dpi, size.Height * Zoom / Dpi);
        }
    }

    internal PictureViewport()
    {
        Focusable = true; ClipToBounds = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        System.Windows.Automation.AutomationProperties.SetName(this, "图片");
        SizeChanged += (_, _) => RefreshSize();
    }

    internal void SetPicture(ViewerPicture? picture)
    {
        EndDrag(); Picture = picture; QuarterTurns = 0; Fit();
    }
    internal void Fit() { Fitted = true; Zoom = Math.Max(.000001, FitZoom); Pan = default; Update(); }
    internal void OriginalSize() { Fitted = false; Zoom = 1; Pan = default; Update(); }
    internal void RefreshSize() { if (Fitted) Fit(); else Update(); }
    internal void Rotate(int delta)
    {
        if (Picture == null) return;
        QuarterTurns = ((QuarterTurns + delta) % 4 + 4) % 4;
        Pan = default; if (Fitted) Fit(); else Update();
    }
    internal void ZoomBy(double factor, Point? anchor = null)
    {
        if (Picture == null || !double.IsFinite(factor) || factor <= 0) return;
        var value = Math.Clamp(Zoom * factor, Math.Min(.05, Math.Max(.000001, FitZoom)), 8);
        var point = anchor ?? new Point(ActualWidth / 2, ActualHeight / 2);
        var relative = point - new Point(ActualWidth / 2, ActualHeight / 2);
        Pan = relative - (relative - Pan) * (value / Zoom);
        Zoom = value; Fitted = false; Update();
    }
    internal void PanBy(Vector delta) { Pan += delta; Update(); }
    private void Update()
    {
        var size = RotatedSize;
        double x = Math.Max(0, (size.Width * Zoom / Dpi - ActualWidth) / 2);
        double y = Math.Max(0, (size.Height * Zoom / Dpi - ActualHeight) / 2);
        Pan = new(Math.Clamp(Pan.X, -x, x), Math.Clamp(Pan.Y, -y, y));
        Cursor = dragStart != null ? Cursors.SizeAll : Picture == null ? Cursors.Arrow : Cursors.Hand;
        InvalidateVisual(); Changed?.Invoke();
    }
    protected override void OnRender(DrawingContext drawing)
    {
        drawing.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (Picture is not { } picture) return;
        var matrix = Matrix.Identity;
        matrix.Rotate(QuarterTurns * 90); matrix.Scale(Zoom / Dpi, Zoom / Dpi);
        matrix.Translate(ActualWidth / 2 + Pan.X, ActualHeight / 2 + Pan.Y);
        drawing.PushTransform(new MatrixTransform(matrix));
        drawing.DrawImage(picture.Bitmap, new Rect(-picture.Width / 2d, -picture.Height / 2d, picture.Width, picture.Height));
        drawing.Pop();
    }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e); if (Picture == null) return;
        ZoomBy(Math.Pow(1.15, e.Delta / 120d), e.GetPosition(this)); e.Handled = true;
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e); if (Picture == null) return;
        Focus(); e.Handled = true;
        if (e.ClickCount == 2) { EndDrag(); if (Fitted) OriginalSize(); else Fit(); return; }
        dragStart = e.GetPosition(this); dragOrigin = Pan; CaptureMouse(); Update();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragStart is { } start && e.LeftButton == MouseButtonState.Pressed)
        { Pan = dragOrigin + (e.GetPosition(this) - start); Update(); }
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { base.OnMouseLeftButtonUp(e); EndDrag(); }
    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); dragStart = null; Update(); }
    private void EndDrag() { dragStart = null; if (IsMouseCaptured) ReleaseMouseCapture(); }
}
