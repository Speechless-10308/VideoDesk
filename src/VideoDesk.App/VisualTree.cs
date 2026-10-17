using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace VideoDesk.App;

internal static class VisualTree
{
    internal static void ReleaseImages(DependencyObject root)
    {
        if(root is Image image)image.Source=null;
        if(root is not (Visual or Visual3D))return;
        var count=VisualTreeHelper.GetChildrenCount(root);
        for(var i=0;i<count;i++)ReleaseImages(VisualTreeHelper.GetChild(root,i));
        // A discarded control may never have had its template applied.
        if(count==0&&root is ContentControl{Content:DependencyObject content})ReleaseImages(content);
    }
    internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        if (root is not (Visual or Visual3D)) yield break;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i)))
                yield return child;
    }
}
