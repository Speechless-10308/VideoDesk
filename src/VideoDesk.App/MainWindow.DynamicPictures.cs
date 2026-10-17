using System.Windows;
using VideoDesk.Core;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    // State contains only selection and rotation; recycled cards retain no bitmap.
    private readonly Dictionary<string, DynamicPictureState> dynamicPictureStates = [];
    private FrameworkElement DynamicPictures(CreatorDynamic item)
    {
        var key = item.Id;
        if (!dynamicPictureStates.Remove(key, out var state)) state = new();
        dynamicPictureStates[key] = state;
        while (dynamicPictureStates.Count > 24) dynamicPictureStates.Remove(dynamicPictureStates.Keys.First());
        var pictures = item.Pictures.Select(url => new CommentPicture(url, 0, 0)).ToArray();
        return new InlineDynamicPictures(pictures, state,
            (image, url, width) => LoadImage(image, url, width),
            (url, width, ct) => images.Get(url, width, ct),
            () => navigation.Token, index => ShowCommentPictures(pictures, index),
            Ink, Muted, Surface, Pink);
    }
}
