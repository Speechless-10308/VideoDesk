using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private static Button ClearInputButton(TextBox input,string label="清空输入")
    {
        var clear=IconButton(PackIconKind.CloseCircle,label,28);clear.Content=Icon(PackIconKind.CloseCircle,15);clear.Height=28;clear.Foreground=Muted;clear.Opacity=.65;clear.VerticalAlignment=VerticalAlignment.Center;clear.Focusable=false;
        void Update()=>clear.Visibility=input.Text.Length>0?Visibility.Visible:Visibility.Collapsed;Update();input.TextChanged+=(_,_)=>Update();clear.Click+=(_,e)=>{e.Handled=true;input.Clear();input.Focus();};return clear;
    }
    private static void NativeInputTemplate(TextBox input)
    {
        input.Foreground=Ink;input.CaretBrush=Ink;
        var host=new FrameworkElementFactory(typeof(ScrollViewer));host.Name="PART_ContentHost";host.SetValue(FrameworkElement.MarginProperty,new TemplateBindingExtension(Control.PaddingProperty));host.SetValue(FrameworkElement.VerticalAlignmentProperty,new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty,ScrollBarVisibility.Hidden);host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty,ScrollBarVisibility.Hidden);input.Template=new ControlTemplate(typeof(TextBox)){VisualTree=host};
    }
    private static Border InputSurface(TextBox input,string hint)
    {
        input.Background=Brushes.Transparent;var margin=input.Margin;input.Margin=new Thickness(0);var width=input.Width;input.ClearValue(WidthProperty);NativeInputTemplate(input);
        var grid=new Grid{Width=width,Height=input.Height};grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});grid.Children.Add(input);
        var placeholder=Text(hint,input.FontSize);placeholder.Foreground=Muted;placeholder.Margin=new Thickness(input.Padding.Left,0,8,0);placeholder.IsHitTestVisible=false;placeholder.TextTrimming=TextTrimming.CharacterEllipsis;placeholder.Visibility=input.Text.Length==0?Visibility.Visible:Visibility.Collapsed;grid.Children.Add(placeholder);
        var clear=ClearInputButton(input,"清空"+hint);clear.Margin=new Thickness(0,0,5,0);Grid.SetColumn(clear,1);grid.Children.Add(clear);input.TextChanged+=(_,_)=>placeholder.Visibility=input.Text.Length==0?Visibility.Visible:Visibility.Collapsed;
        return new Border{Child=grid,Background=Surface,CornerRadius=new CornerRadius(16),Margin=margin};
    }
}
