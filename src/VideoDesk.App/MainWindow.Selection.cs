using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private TextBox SelectableText(TextBlock source,string label,bool wrap=true)
    {
        var host=new FrameworkElementFactory(typeof(ScrollViewer));host.Name="PART_ContentHost";host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty,ScrollBarVisibility.Disabled);host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty,ScrollBarVisibility.Disabled);
        var text=new TextBox{Text=source.Text,IsReadOnly=true,IsReadOnlyCaretVisible=false,Style=null,Template=new ControlTemplate(typeof(TextBox)){VisualTree=host},BorderThickness=new Thickness(0),Padding=new Thickness(0),Background=Brushes.Transparent,Foreground=source.Foreground,FontFamily=source.FontFamily,FontSize=source.FontSize,FontWeight=source.FontWeight,TextWrapping=wrap?TextWrapping.Wrap:TextWrapping.NoWrap,VerticalContentAlignment=VerticalAlignment.Center,VerticalAlignment=source.VerticalAlignment,Margin=source.Margin,Height=source.Height,MaxHeight=source.MaxHeight,ToolTip=source.ToolTip,Cursor=Cursors.IBeam,FocusVisualStyle=null};
        TextBlock.SetLineHeight(text,double.IsNaN(source.LineHeight)?source.FontSize*1.45:source.LineHeight);TextBlock.SetLineStackingStrategy(text,LineStackingStrategy.BlockLineHeight);SelectionMenu(text,label);return text;
    }
    private RichTextBox SelectableRichText(TextBlock source,string label)
    {
        var paragraph=new Paragraph{Margin=new Thickness(0),LineHeight=source.LineHeight};while(source.Inlines.FirstInline is {} inline){source.Inlines.Remove(inline);paragraph.Inlines.Add(inline);}
        var document=new FlowDocument(paragraph){PagePadding=new Thickness(0),FontFamily=source.FontFamily,FontSize=source.FontSize,FontWeight=source.FontWeight,Foreground=source.Foreground,TextAlignment=source.TextAlignment};
        var text=new RichTextBox{Document=document,IsReadOnly=true,IsReadOnlyCaretVisible=false,IsDocumentEnabled=true,Style=null,BorderThickness=new Thickness(0),Padding=new Thickness(0),Background=Brushes.Transparent,Foreground=source.Foreground,FontSize=source.FontSize,Margin=source.Margin,MaxHeight=source.MaxHeight,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Cursor=Cursors.IBeam,FocusVisualStyle=null};
        SelectionMenu(text,label);return text;
    }
    private static void SelectionMenu(TextBoxBase text,string label)
    {
        AutomationProperties.SetName(text,label+"（可选择文字）");var menu=new ContextMenu();menu.Items.Add(new MenuItem{Header="复制",Command=ApplicationCommands.Copy,CommandTarget=text});menu.Items.Add(new MenuItem{Header="全选",Command=ApplicationCommands.SelectAll,CommandTarget=text});text.ContextMenu=menu;
        text.PreviewMouseRightButtonUp+=(_,e)=>{e.Handled=true;menu.PlacementTarget=text;menu.Placement=PlacementMode.MousePoint;menu.IsOpen=true;};
    }
}
