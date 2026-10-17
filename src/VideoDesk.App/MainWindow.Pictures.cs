using System.Windows.Media;
using VideoDesk.Core;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private PictureViewer? pictureViewerWindow;
    private void ShowCommentPictures(IReadOnlyList<CommentPicture> pictures, int index)
    {
        if (pictures.Count == 0) return;
        index = Math.Clamp(index, 0, pictures.Count - 1);
        if(pictureViewerWindow is {IsClosed:false} current&&current.Matches(pictures))
        {
            current.Select(index,true);if(current.WindowState==System.Windows.WindowState.Minimized)current.WindowState=System.Windows.WindowState.Normal;
            current.Activate();return;
        }
        var dialog = new PictureViewer(this, pictures, index, images.GetViewerPicture,
            images.GetThumbnail, images.GetOriginalBytes,
            ((SolidColorBrush)CanvasBrush).Color.R < 50);
        PresentPictureViewer(dialog);
    }
    private void PresentPictureViewer(PictureViewer viewer)
    {
        pictureViewerWindow?.Close();pictureViewerWindow=viewer;
        viewer.Closed+=(_,_)=>{if(ReferenceEquals(pictureViewerWindow,viewer))pictureViewerWindow=null;};
        viewer.Show();
    }
}
