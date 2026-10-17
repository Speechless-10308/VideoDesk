using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace VideoDesk.App;

internal static class VisualTree
{
    internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        if (root is not (Visual or Visual3D)) yield break;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i)))
                yield return child;
    }
}
