using System.Windows.Media.Imaging;

namespace VideoDesk.App;

internal sealed record ViewerPicture(BitmapSource Bitmap, int Width, int Height)
{
    internal bool Reduced => Bitmap.PixelWidth < Width || Bitmap.PixelHeight < Height;
}
