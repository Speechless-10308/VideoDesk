using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using VideoDesk.Core;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private readonly Dictionary<string,long> commentMentions=[];
    private readonly List<string> commentImagePaths=[];
    private CheckBox? commentSync;
    private WrapPanel? commentAttachments;
    private Popup? composerPopup;
    private IReadOnlyList<EmojiPackage>? commentEmojiPackages;
    private long emojiAccount=-1;
    private void BuildNativeCommentEditor(StackPanel panel,Func<Button,Task>? publisher=null)
    {
        composerPopup?.SetCurrentValue(Popup.IsOpenProperty,false);commentMentions.Clear();commentImagePaths.Clear();
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(40)});grid.ColumnDefinitions.Add(new());
        var face=AvatarImage(account?.Avatar??"",30);face.HorizontalAlignment=HorizontalAlignment.Left;face.VerticalAlignment=VerticalAlignment.Top;face.Margin=new Thickness(0,8,0,0);grid.Children.Add(face);
        var body=new StackPanel();Grid.SetColumn(body,1);grid.Children.Add(body);
        commentInput=new TextBox{FontSize=14,Style=null,Foreground=Ink,CaretBrush=Ink,BorderThickness=new Thickness(0),Background=Brushes.Transparent,Padding=new Thickness(12,10,12,10),Height=48,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxLength=1000};
        // Use one native text host for both the caret and entered text. The
        // theme's default text template did not apply this editor's padding.
        var textHost=new FrameworkElementFactory(typeof(ScrollViewer),"PART_ContentHost");textHost.SetValue(FrameworkElement.MarginProperty,new TemplateBindingExtension(Control.PaddingProperty));textHost.SetValue(UIElement.FocusableProperty,false);textHost.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty,ScrollBarVisibility.Disabled);textHost.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty,ScrollBarVisibility.Auto);
        commentInput.Template=new ControlTemplate(typeof(TextBox)){VisualTree=textHost};commentInput.VerticalContentAlignment=VerticalAlignment.Top;
        AutomationProperties.SetName(commentInput,"评论内容");var inputGrid=new Grid();inputGrid.Children.Add(commentInput);var placeholder=Text("发一条友善的评论",14);placeholder.Foreground=Muted;placeholder.Margin=new Thickness(12,10,12,10);placeholder.VerticalAlignment=VerticalAlignment.Top;placeholder.IsHitTestVisible=false;inputGrid.Children.Add(placeholder);var editor=commentInput;editor.TextChanged+=(_,_)=>placeholder.Visibility=editor.Text.Length==0?Visibility.Visible:Visibility.Collapsed;
        var inputFrame=new Border{Child=inputGrid,Background=Surface,CornerRadius=new CornerRadius(14),BorderBrush=Brushes.Transparent,BorderThickness=new Thickness(0)};inputFrame.SizeChanged+=(_,e)=>inputFrame.Clip=new RectangleGeometry(new Rect(e.NewSize),14,14);body.Children.Add(inputFrame);
        commentAttachments=new WrapPanel();body.Children.Add(new ScrollViewer{Content=commentAttachments,MaxHeight=126,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        var tools=new Grid{Margin=new Thickness(0,7,0,0)};tools.ColumnDefinitions.Add(new());tools.ColumnDefinitions.Add(new(){Width=GridLength.Auto});tools.RowDefinitions.Add(new(){Height=GridLength.Auto});tools.RowDefinitions.Add(new(){Height=GridLength.Auto});var send=Button("发布");send.Width=76;send.Height=38;send.Padding=new Thickness(8,3,8,3);send.Background=Pink;send.Foreground=Brushes.White;send.ToolTip="Ctrl+Enter 发布";Grid.SetColumn(send,1);tools.Children.Add(send);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal};
        var emoji=IconButton(PackIconKind.EmoticonHappyOutline,"表情",28);emoji.Height=32;emoji.Click+=async(_,_)=>await Run(()=>ShowCommentEmojis(emoji));buttons.Children.Add(emoji);
        var mention=IconButton(PackIconKind.At,"@ 用户",28);mention.Height=32;mention.Click+=async(_,_)=>await Run(()=>ShowCommentMentions(mention));buttons.Children.Add(mention);
        var image=IconButton(PackIconKind.ImageOutline,"添加图片（最多 9 张）",28);image.Height=32;image.Click+=(_,_)=>AddCommentImages();buttons.Children.Add(image);
        commentSync=new CheckBox{Content="同步到动态",FontSize=11,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(4,6,0,0),ToolTip="发布评论时同时转发到我的动态"};Grid.SetRow(commentSync,1);tools.Children.Add(commentSync);tools.Children.Add(buttons);body.Children.Add(tools);panel.Children.Add(grid);panel.Tag="native-comment-editor";
        send.Click+=async(_,_)=>await Run(()=>publisher?.Invoke(send)??PublishComment(send));
        commentInput.PreviewKeyDown+=async(_,e)=>{if(e.Key==Key.Enter&&(Keyboard.Modifiers&ModifierKeys.Control)!=0){e.Handled=true;if(send.IsEnabled)await Run(()=>publisher?.Invoke(send)??PublishComment(send));}};
    }
    private void InsertCommentText(string text)
    {
        if(commentInput==null)return;if(commentInput.Text.Length-commentInput.SelectionLength+text.Length>1000){Notify("评论不能超过 1000 字");return;}
        var position=commentInput.SelectionStart;commentInput.SelectedText=text;commentInput.CaretIndex=position+text.Length;commentInput.Focus();
    }
    private void OpenComposerPopup(FrameworkElement target,FrameworkElement body)
    {
        composerPopup?.SetCurrentValue(Popup.IsOpenProperty,false);
        var popup=new Popup{PlacementTarget=target,Placement=PlacementMode.Custom,StaysOpen=false,AllowsTransparency=true,
            CustomPopupPlacementCallback=(menu,button,_)=>[new(new Point(-8,-menu.Height-4),PopupPrimaryAxis.Vertical),new(new Point(-8,button.Height+4),PopupPrimaryAxis.Vertical)]};
        popup.Child=new Border{Child=body,Width=340,Background=CanvasBrush,CornerRadius=new CornerRadius(8),BorderBrush=new SolidColorBrush(Color.FromRgb(222,226,234)),BorderThickness=new Thickness(1),Padding=new Thickness(10)};composerPopup=popup;popup.IsOpen=true;
        target.Unloaded+=Closed;void Closed(object sender,RoutedEventArgs e){popup.IsOpen=false;target.Unloaded-=Closed;}
    }
    private async Task ShowCommentEmojis(FrameworkElement target)
    {
        var ct=navigation.Token;if(emojiAccount!=(account?.Mid??0)){commentEmojiPackages=null;emojiAccount=account?.Mid??0;}
        var panel=new DockPanel();var label=Text("正在加载表情…",12);label.Foreground=Muted;panel.Children.Add(label);OpenComposerPopup(target,panel);
        commentEmojiPackages??=await api.GetCommentEmojis(ct);ct.ThrowIfCancellationRequested();if(!target.IsLoaded)return;panel.Children.Clear();
        var packages=commentEmojiPackages;var selector=new ComboBox{ItemsSource=packages,DisplayMemberPath="Name",Height=34,Margin=new Thickness(0,0,0,8)};DockPanel.SetDock(selector,Dock.Top);panel.Children.Add(selector);
        var choices=new WrapPanel();var scroll=new SmoothScrollViewer{Content=choices,Height=230,ShowBackToTop=false,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};panel.Children.Add(scroll);
        selector.SelectionChanged+=(_,_)=>
        {
            choices.Children.Clear();if(selector.SelectedItem is not EmojiPackage package)return;
            foreach(var choice in package.Items)
            {
                var button=Button("");button.Width=choice.IsText?145:package.Items.Any(e=>e.Size>=2)?60:40;button.Height=choice.IsText?36:button.Width;button.Padding=new Thickness(3);button.ToolTip=choice.Accessible?choice.Text:choice.Text+"（尚未拥有）";button.IsEnabled=choice.Accessible;
                if(choice.IsText)button.Content=Text(choice.Text,13);else{var image=new Image{Stretch=Stretch.Uniform};LoadImage(image,choice.Url,128);button.Content=image;}
                button.Click+=(_,_)=>{composerPopup!.IsOpen=false;InsertCommentText(choice.Text);};choices.Children.Add(button);
            }
            scroll.ScrollToTop();
        };if(packages.Count>0)selector.SelectedIndex=0;else{panel.Children.Clear();panel.Children.Add(Text("暂无可用表情",13));}
    }
    private async Task ShowCommentMentions(FrameworkElement target)
    {
        if(!RequireLogin())return;var ct=navigation.Token;var panel=new DockPanel();var searchUser=new TextBox{Height=34,FontSize=13,Padding=new Thickness(6),Margin=new Thickness(0,0,0,8)};var searchSurface=InputSurface(searchUser,"搜索用户昵称");DockPanel.SetDock(searchSurface,Dock.Top);panel.Children.Add(searchSurface);var users=new StackPanel();panel.Children.Add(new SmoothScrollViewer{Content=users,Height=230,ShowBackToTop=false,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});OpenComposerPopup(target,panel);
        int revision=0;
        async Task LoadUsers()
        {
            var current=++revision;var result=await api.SearchMentions(searchUser.Text.Trim(),ct);ct.ThrowIfCancellationRequested();if(current!=revision||!target.IsLoaded)return;users.Children.Clear();
            foreach(var user in result.Take(40))
            {
                var row=new DockPanel();var avatar=AvatarImage(user.Avatar,26);avatar.Margin=new Thickness(0,0,8,0);DockPanel.SetDock(avatar,Dock.Left);row.Children.Add(avatar);var name=Text(user.Name,13);name.TextTrimming=TextTrimming.CharacterEllipsis;name.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(name);var button=Button("");button.Content=row;button.Height=38;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.Padding=new Thickness(4);button.ToolTip=user.Group;
                button.Click+=(_,_)=>{composerPopup!.IsOpen=false;commentMentions[user.Name]=user.Mid;var caret=commentInput!.CaretIndex;if(caret>0&&commentInput.Text[caret-1]=='@')commentInput.Select(caret-1,1);InsertCommentText("@"+user.Name+" ");};users.Children.Add(button);
            }
            if(users.Children.Count==0)users.Children.Add(Text("没有找到用户",13));
        }
        searchUser.TextChanged+=async(_,_)=>{var current=++revision;try{await Task.Delay(250,ct);if(current==revision)await Run(LoadUsers);}catch(OperationCanceledException){}};await LoadUsers();
    }
    private void AddCommentImages()
    {
        if(!RequireLogin())return;var picker=new OpenFileDialog{Title="选择评论图片",Filter="图片|*.jpg;*.jpeg;*.png;*.gif",Multiselect=true};if(picker.ShowDialog(this)!=true)return;
        foreach(var path in picker.FileNames)
        {
            if(commentImagePaths.Contains(path,StringComparer.OrdinalIgnoreCase))continue;if(commentImagePaths.Count>=9){Notify("最多添加 9 张图片");break;}
            if(new FileInfo(path).Length>10*1024*1024){Notify("图片超过 10 MB："+Path.GetFileName(path));continue;}commentImagePaths.Add(path);
        }
        RenderCommentAttachments();
    }
    private void RenderCommentAttachments()
    {
        if(commentAttachments==null)return;commentAttachments.Children.Clear();
        foreach(var path in commentImagePaths.ToArray())
        {
            var tile=new Grid{Width=58,Height=58,Margin=new Thickness(0,6,6,0),ToolTip=Path.GetFileName(path)};
            try{using var file=File.OpenRead(path);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=110;bitmap.StreamSource=file;bitmap.EndInit();bitmap.Freeze();tile.Children.Add(new Image{Source=bitmap,Stretch=Stretch.UniformToFill});}catch(Exception error) when(error is IOException or NotSupportedException){commentImagePaths.Remove(path);Notify("无法读取图片："+Path.GetFileName(path));continue;}
            var remove=IconButton(PackIconKind.Close,"移除图片",20);remove.Height=20;remove.Background=CanvasBrush;remove.HorizontalAlignment=HorizontalAlignment.Right;remove.VerticalAlignment=VerticalAlignment.Top;remove.Click+=(_,_)=>{commentImagePaths.Remove(path);RenderCommentAttachments();};tile.Children.Add(remove);commentAttachments.Children.Add(tile);
        }
    }
}
