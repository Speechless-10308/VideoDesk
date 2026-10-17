using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using VideoDesk.Core;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private readonly Button settingsNav;
    private static readonly System.Net.Http.HttpClient updatesHttp=new(new System.Net.Http.HttpClientHandler{UseCookies=false}){Timeout=TimeSpan.FromSeconds(15)};
    private void SavePreferences()=>preferences.Save(App.DataDirectory);
    private void ApplyTheme()
    {
        var dark=preferences.Theme=="Dark";
        if(preferences.Theme=="System")try{dark=(int?)Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")?.GetValue("AppsUseLightTheme") ==0;}catch(System.Security.SecurityException){}
        ((SolidColorBrush)Ink).Color=dark?Color.FromRgb(242,233,240):Color.FromRgb(39,35,42);
        ((SolidColorBrush)Muted).Color=dark?Color.FromRgb(186,171,184):Color.FromRgb(139,130,147);
        ((SolidColorBrush)CanvasBrush).Color=dark?Color.FromRgb(35,29,36):Color.FromRgb(255,251,255);
        ((SolidColorBrush)Surface).Color=dark?Color.FromRgb(48,38,46):Color.FromRgb(246,239,245);
        ((SolidColorBrush)HeaderBrush).Color=dark?Color.FromRgb(28,23,29):Color.FromRgb(255,251,255);
        ((SolidColorBrush)RailBrush).Color=dark?Color.FromRgb(28,23,29):Color.FromRgb(255,249,253);
        ((SolidColorBrush)PalePink).Color=dark?Color.FromRgb(89,49,67):Color.FromRgb(255,229,240);
        ((SolidColorBrush)BackdropBrush).Color=dark?Color.FromRgb(28,23,29):Color.FromRgb(250,244,249);
        ((SolidColorBrush)OutlineBrush).Color=dark?Color.FromRgb(70,56,68):Color.FromRgb(235,226,235);
        ((SolidColorBrush)RaisedBrush).Color=dark?Color.FromRgb(48,38,46):Colors.White;
        // Templates can freeze resource values. Keep their copies separate from
        // the live brushes shared by cached pages, so theme changes stay writable.
        foreach(var (key,brush) in new[]{("Ink",Ink),("Muted",Muted),("Accent",Pink),("UiSurface",Surface),("UiCanvas",CanvasBrush),("UiOutline",OutlineBrush),("UiSelected",PalePink)})Application.Current.Resources[key]=brush.CloneCurrentValue();
        var palette=new PaletteHelper();var theme=palette.GetTheme();theme.SetBaseTheme(dark?BaseTheme.Dark:BaseTheme.Light);palette.SetTheme(theme);
        content.LayoutTransform=new ScaleTransform(Math.Clamp(preferences.InterfaceScale,.9,1.25),Math.Clamp(preferences.InterfaceScale,.9,1.25));
    }
    private Task ShowSettings()
    {
        BeginPage();staticPage="settings";SelectNav(settingsNav);SetPageSurface(settings:true);var stack=new StackPanel{Margin=new Thickness(6,0,10,8)};stack.Children.Add(Text("设置",26,FontWeights.SemiBold));
        StackPanel Card(string label){var rows=new StackPanel{Margin=new Thickness(24,16,24,8)};var heading=Text(label,15,FontWeights.SemiBold);heading.Foreground=Muted;heading.Margin=new Thickness(0,0,0,8);rows.Children.Add(heading);stack.Children.Add(new Border{Child=rows,Background=CanvasBrush,CornerRadius=new CornerRadius(20),Margin=new Thickness(0,16,0,0)});return rows;}
        void Row(Panel parent,string label,FrameworkElement control){var grid=new Grid{MinHeight=56};grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});var text=Text(label,16);text.TextWrapping=TextWrapping.Wrap;text.Margin=new Thickness(0,8,20,8);grid.Children.Add(text);Grid.SetColumn(control,1);control.VerticalAlignment=VerticalAlignment.Center;grid.Children.Add(control);parent.Children.Add(grid);}
        ComboBox Choice(IEnumerable<string> values,int selected,Action<int> changed){var box=new ComboBox{Style=(Style)Application.Current.FindResource("CompactChoice"),Foreground=Ink,ItemsSource=values,SelectedIndex=selected,Width=220,Height=44,FontSize=15,Padding=new Thickness(16,0,12,0)};box.SelectionChanged+=(_,_)=>{changed(box.SelectedIndex);SavePreferences();};return box;}
        var accountCard=Card("账户");var login=Button(account?.LoggedIn==true?"切换账户":"扫码登录");login.Background=PalePink;login.Foreground=Pink;login.Width=156;login.Height=44;login.Click+=async(_,_)=>await Run(Login);Row(accountCard,account?.LoggedIn==true?$"{account.Name} · LV{account.Level} · {(account.Vip?"大会员":"普通账号")}":"未登录",login);
        var appearance=Card("外观");Row(appearance,"主题",Choice(["浅色","深色","跟随系统"],preferences.Theme=="Dark"?1:preferences.Theme=="System"?2:0,i=>{preferences.Theme=new[]{"Light","Dark","System"}[i];ApplyTheme();}));Row(appearance,"界面缩放",Choice(["100%","110%","125%"],preferences.InterfaceScale>=1.2?2:preferences.InterfaceScale>1?1:0,i=>{preferences.InterfaceScale=new[]{1d,1.1,1.25}[i];ApplyTheme();}));
        var discovery=Card("内容");Row(discovery,"首页推荐",ModeSwitch(preferences.HomeMode,m=>{preferences.HomeMode=homeMode=m;SavePreferences();return Task.CompletedTask;}));Row(discovery,"搜索结果",ModeSwitch(preferences.SearchMode,m=>{preferences.SearchMode=searchMode=m;SavePreferences();return Task.CompletedTask;}));Row(discovery,"相关推荐",ModeSwitch(preferences.RelatedMode,m=>{preferences.RelatedMode=relatedMode=m;SavePreferences();return Task.CompletedTask;}));
        var playback=Card("播放");Row(playback,"默认清晰度",Choice(["高清 1080P","高清 720P","清晰 480P","流畅 360P"],Array.IndexOf(new[]{80,64,32,16},preferences.DefaultQuality),i=>preferences.DefaultQuality=new[]{80,64,32,16}[i]));var hardware=new ToggleButton{IsChecked=preferences.HardwareDecoding,Style=(Style)Application.Current.FindResource("MaterialDesignSwitchToggleButton")};hardware.Click+=(_,_)=>{preferences.HardwareDecoding=hardware.IsChecked==true;SavePreferences();};Row(playback,"硬件解码（下次打开视频生效）",hardware);
        var cache=Card("缓存");Row(cache,"图片缓存上限",Choice(["64 MB","192 MB","512 MB"],preferences.ImageCacheMegabytes<=64?0:preferences.ImageCacheMegabytes>=512?2:1,i=>{preferences.ImageCacheMegabytes=new[]{64,192,512}[i];images.MaxDiskBytes=preferences.ImageCacheMegabytes*1048576;images.TrimDisk(true);}));var clear=Button("清理图片缓存");clear.Foreground=Pink;clear.Background=PalePink;clear.Height=44;clear.Padding=new Thickness(18,0,18,0);clear.Click+=async(_,_)=>{clear.IsEnabled=false;await images.Clear();clear.IsEnabled=true;Notify("图片缓存已清理");};Row(cache,"只清理图片，保留登录和历史",clear);
        if(App.Diagnostics.Enabled)
        {
            var diagnostic=Card("本次排错");Row(diagnostic,"重启后恢复正常模式",Text("已临时启用诊断记录",13));
            var captureState=Text("",12);captureState.Foreground=Muted;var inspect=Button("查看归档状态");inspect.Click+=(_,_)=>{var state=captures.Status;captureState.Text=$"待写 {state.Pending} · 已保存 {state.Written} · 跳过 {state.Skipped} · 失败 {state.Failed}"+(state.Error is null?"":" · "+state.Error);};Row(diagnostic,"后台保存状态",inspect);diagnostic.Children.Add(captureState);
            var open=Button("打开抓包目录");open.Click+=(_,_)=>{if(!Directory.Exists(captures.Root)){Notify("尚未生成诊断档案");return;}System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",captures.Root){UseShellExecute=true});};Row(diagnostic,"已保存的诊断档案",open);
        }
        var version=GetType().Assembly.GetName().Version??new Version(0,1,1);
        var about=Card("关于");var updates=Button("检查更新");updates.Height=44;updates.Padding=new Thickness(18,0,18,0);Uri? releasePage=null;
        updates.Click+=async(_,_)=>await Run(async()=>
        {
            if(releasePage!=null){System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(releasePage.AbsoluteUri){UseShellExecute=true});return;}
            var ct=navigation.Token;updates.IsEnabled=false;
            try
            {
                var release=await GitHubRelease.GetLatest(updatesHttp,ct);ct.ThrowIfCancellationRequested();
                if(release==null){Notify("暂未发布新版本");return;}
                if(!release.IsNewerThan(version)){Notify("当前已是最新版本");return;}
                releasePage=release.Page;updates.Content="下载 "+release.Version.ToString(3);Notify("发现新版本 "+release.Version.ToString(3));
            }
            catch(OperationCanceledException) when(!ct.IsCancellationRequested){Notify("检查更新超时，请稍后重试");}
            catch(Exception error) when(error is System.Net.Http.HttpRequestException or InvalidDataException or System.Text.Json.JsonException){Notify("检查更新失败："+error.Message);}
            finally{updates.IsEnabled=true;}
        });
        Row(about,"VideoDesk · "+version.ToString(3),updates);
        var licenses=Button("查看许可");licenses.Height=44;licenses.Padding=new Thickness(18,0,18,0);licenses.Click+=(_,_)=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.IO.Path.Combine(AppContext.BaseDirectory,"THIRD-PARTY-NOTICES.md")){UseShellExecute=true});Row(about,"第三方组件",licenses);
        content.Children.Add(new SmoothScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});return Task.CompletedTask;
    }
    private FrameworkElement BuildDanmakuTools()
    {
        var row=new StackPanel{Orientation=Orientation.Horizontal,Height=32,VerticalAlignment=VerticalAlignment.Center};
        var toggle=PlayerTextButton("弹幕");toggle.Padding=new Thickness(8,0,8,0);toggle.ToolTip="显示 / 隐藏弹幕";toggle.Click+=(_,_)=>{SetDanmakuVisibility(!danmakuEnabled);toggle.Foreground=danmakuEnabled?Brushes.White:Muted;};DockPanel.SetDock(toggle,Dock.Left);row.Children.Add(toggle);
        var settings=IconButton(PackIconKind.Tune,"弹幕设置",32);settings.Height=32;settings.Foreground=Brushes.White;settings.Click+=(_,_)=>ShowDanmakuSettings(settings);DockPanel.SetDock(settings,Dock.Left);row.Children.Add(settings);
        var list=IconButton(PackIconKind.FormatListBulleted,"弹幕列表",32);list.Height=32;list.Foreground=Brushes.White;list.Click+=(_,_)=>ShowDanmakuList(list);DockPanel.SetDock(list,Dock.Left);row.Children.Add(list);
        var style=IconButton(PackIconKind.FormatColorText,"发送弹幕样式",32);style.Height=32;style.Foreground=Brushes.White;style.Click+=(_,_)=>ShowDanmakuSendStyle(style);DockPanel.SetDock(style,Dock.Left);row.Children.Add(style);
        row.SizeChanged+=(_,_)=>{var wide=overlay==null||overlay.ActualWidth>=660;list.Visibility=style.Visibility=wide?Visibility.Visible:Visibility.Collapsed;};
        return row;
    }
    private FrameworkElement BuildDanmakuInput()
    {
        var row=new DockPanel{Height=32,Margin=new Thickness(8,0,8,0)};
        var send=PlayerTextButton("发送");send.Background=Pink;send.Width=64;send.VerticalAlignment=VerticalAlignment.Center;send.Padding=new Thickness(8,0,8,0);DockPanel.SetDock(send,Dock.Right);row.Children.Add(send);
        var input=new TextBox{Height=32,FontSize=13,Background=new SolidColorBrush(Color.FromArgb(180,40,43,51)),Foreground=Brushes.White,BorderThickness=new Thickness(0),Style=null,VerticalContentAlignment=VerticalAlignment.Center,Padding=new Thickness(10,0,10,0),Margin=new Thickness(0,0,8,0),MaxLength=100};AutomationProperties.SetName(input,"弹幕内容");input.ToolTip="发一条友善的弹幕";var host=new FrameworkElementFactory(typeof(ScrollViewer),"PART_ContentHost");host.SetValue(FrameworkElement.MarginProperty,new TemplateBindingExtension(Control.PaddingProperty));host.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);host.SetValue(UIElement.FocusableProperty,false);input.Template=new ControlTemplate(typeof(TextBox)){VisualTree=host};row.Children.Add(input);
        System.Windows.Input.InputMethod.SetIsInputMethodEnabled(input,true);input.CaretBrush=Brushes.White;
        send.Click+=async(_,_)=>await Run(async()=>{if(!RequireLogin())return;send.IsEnabled=false;try{var video=detail!.Video;var currentPart=part!;var text=input.Text;var ct=navigation.Token;var posted=await api.SendDanmaku(video.Aid,currentPart.Cid,player!.Number("time-pos"),text,ct,preferences.Danmaku.SendMode,preferences.Danmaku.SendFontSize,preferences.Danmaku.SendColor);ct.ThrowIfCancellationRequested();if(part?.Cid!=currentPart.Cid)return;RememberSentDanmaku(posted);if(input.Text==text)input.Clear();Notify("弹幕已发送");RefreshDanmakuWindow();}finally{send.IsEnabled=true;}});return row;
    }
    private void OpenPlayerPopup(FrameworkElement body,Button anchor)
    {
        overlay?.ShowPanel(body,anchor,CanvasBrush,Surface,SavePreferences);
    }
    private void ShowDanmakuSettings(Button anchor)
    {
        var p=preferences.Danmaku;var panel=new StackPanel{Width=295};panel.Children.Add(Text("弹幕设置",17,FontWeights.SemiBold));
        void Changed(){overlay?.Danmaku.SettingsChanged();}
        void Check(string label,bool value,Action<bool> apply){var box=new CheckBox{Foreground=Ink,Content=label,IsChecked=value,FontSize=13,Margin=new Thickness(0,8,0,0)};box.Click+=(_,_)=>{apply(box.IsChecked==true);Changed();};panel.Children.Add(box);}
        var kindHeading=Text("按类型过滤",12);kindHeading.Foreground=Muted;kindHeading.Margin=new Thickness(0,14,0,0);panel.Children.Add(kindHeading);
        var kinds=new UniformGrid{Columns=4,Margin=new Thickness(0,8,0,8)};
        foreach(var (label,icon,on,apply) in new[]{("滚动",PackIconKind.FormatAlignLeft,p.Scroll,(Action<bool>)(v=>p.Scroll=v)),("固定",PackIconKind.FormatAlignCenter,p.Fixed,v=>p.Fixed=v),("彩色",PackIconKind.Palette,p.Colored,v=>p.Colored=v),("高级",PackIconKind.AutoFix,p.Advanced,v=>p.Advanced=v)}){var b=Button("");var contents=new StackPanel();var symbol=Icon(icon,22);symbol.HorizontalAlignment=HorizontalAlignment.Center;contents.Children.Add(symbol);var labelText=Text(label,12);labelText.HorizontalAlignment=HorizontalAlignment.Center;labelText.Margin=new Thickness(0,5,0,0);contents.Children.Add(labelText);b.Content=contents;b.Height=62;b.Margin=new Thickness(0,0,5,0);b.Padding=new Thickness(6,8,6,8);b.Background=on?PalePink:Surface;b.Foreground=on?Pink:Muted;labelText.Foreground=b.Foreground;bool selected=on;b.ToolTip="显示或隐藏"+label+"弹幕";b.Click+=(_,_)=>{selected=!selected;apply(selected);b.Background=selected?PalePink:Surface;b.Foreground=selected?Pink:Muted;labelText.Foreground=b.Foreground;Changed();};kinds.Children.Add(b);}panel.Children.Add(kinds);
        Check("弹幕随全屏缩放",p.ScaleWithFullscreen,v=>p.ScaleWithFullscreen=v);Check("防止弹幕重叠",p.PreventOverlap,v=>p.PreventOverlap=v);Check("智能防挡弹幕（使用官方人物遮罩）",p.PersonMask,v=>p.PersonMask=v);Check("智能屏蔽重复弹幕",p.SmartFilter,v=>p.SmartFilter=v);Check("弹幕不遮挡底部字幕",p.ProtectSubtitles,v=>p.ProtectSubtitles=v);Check("同步视频速度",p.SyncPlaybackSpeed,v=>p.SyncPlaybackSpeed=v);
        var maskStatus=Text("正在检查人物遮罩…",11);maskStatus.Foreground=Muted;maskStatus.TextWrapping=TextWrapping.Wrap;maskStatus.Margin=new Thickness(0,7,0,0);panel.Children.Add(maskStatus);var mask=overlay?.Danmaku.Mask;
        void UpdateMask(){maskStatus.Text=mask==null||mask.Initializing?"正在检查人物遮罩…":mask.Error!=null?"人物遮罩加载失败："+mask.Error:mask.Available?"已加载官方人物遮罩": "此视频未提供人物遮罩";}
        UpdateMask();if(mask!=null){panel.Loaded+=(_,_)=>mask.StatusChanged+=UpdateMask;panel.Unloaded+=(_,_)=>mask.StatusChanged-=UpdateMask;}
        var sync=Button("同步 B站屏蔽列表");sync.Background=Surface;sync.Margin=new Thickness(0,10,0,0);sync.Click+=async(_,_)=>await Run(async()=>{if(!RequireLogin())return;sync.IsEnabled=false;try{p.SyncedFilters=(await api.GetDanmakuFilters(navigation.Token)).ToList();Changed();SavePreferences();sync.Content=$"已同步 {p.SyncedFilters.Count} 条规则";}finally{sync.IsEnabled=true;}});panel.Children.Add(sync);
        void Range(string label,double value,double min,double max,Action<double> apply,Func<double,string> format){var grid=new Grid{Margin=new Thickness(0,14,0,0)};grid.ColumnDefinitions.Add(new(){Width=new GridLength(76)});grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=new GridLength(48)});grid.Children.Add(Text(label,13));var slider=new Slider{Minimum=min,Maximum=max,Value=value,Height=24,VerticalAlignment=VerticalAlignment.Center,Foreground=Pink};AutomationProperties.SetName(slider,label);Grid.SetColumn(slider,1);grid.Children.Add(slider);var n=Text(format(value),12);n.Foreground=Muted;n.HorizontalAlignment=HorizontalAlignment.Right;Grid.SetColumn(n,2);grid.Children.Add(n);slider.ValueChanged+=(_,_)=>{apply(slider.Value);n.Text=format(slider.Value);Changed();};panel.Children.Add(grid);}
        Range("显示区域",p.Area,.1,1,v=>p.Area=v,v=>$"{v*100:0}%");Range("不透明度",p.Opacity,.05,1,v=>p.Opacity=v,v=>$"{v*100:0}%");Range("弹幕字号",p.FontScale,.6,1.8,v=>p.FontScale=v,v=>$"{v*100:0}%");Range("弹幕速度",p.Speed,.5,2,v=>p.Speed=v,v=>$"{v:0.0}×");
        var advanced=new StackPanel{Margin=new Thickness(0,5,0,0)};var fonts=new ComboBox{Foreground=Ink,ItemsSource=new[]{"Microsoft YaHei UI","SimHei","Segoe UI"},SelectedItem=p.FontFamily,Height=34};fonts.SelectionChanged+=(_,_)=>{p.FontFamily=fonts.SelectedItem?.ToString()??"Microsoft YaHei UI";Changed();};advanced.Children.Add(fonts);var bold=new CheckBox{Foreground=Ink,Content="加粗",IsChecked=p.Bold,Margin=new Thickness(0,8,0,0)};bold.Click+=(_,_)=>{p.Bold=bold.IsChecked==true;Changed();};advanced.Children.Add(bold);var outline=new CheckBox{Foreground=Ink,Content="描边",IsChecked=p.Outline,Margin=new Thickness(0,8,0,8)};outline.Click+=(_,_)=>{p.Outline=outline.IsChecked==true;Changed();};advanced.Children.Add(outline);advanced.Children.Add(Text("屏蔽词（逗号或换行分隔）",12));var words=new TextBox{Text=p.BlockedWords,Height=60,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0)};words.LostFocus+=(_,_)=>{p.BlockedWords=words.Text;Changed();};advanced.Children.Add(words);panel.Children.Add(new Expander{Foreground=Ink,Header="高级设置",Content=advanced,Margin=new Thickness(0,16,0,0)});OpenPlayerPopup(panel,anchor);
    }
    private async void ShowDanmakuList(Button anchor)
    {
        if(part is not {} videoPart||overlay==null)return;
        var cancellation=CancellationTokenSource.CreateLinkedTokenSource(navigation.Token);var ct=cancellation.Token;bool opened=true;
        var panel=new StackPanel{Width=310};var heading=Text("弹幕列表 · 正在加载完整列表…",16,FontWeights.SemiBold);panel.Children.Add(heading);
        var items=new StackPanel();var scroll=new ScrollViewer{Content=items,MaxHeight=320,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};panel.Children.Add(scroll);
        IReadOnlyList<DanmakuItem> all=danmakuMessages.ToArray();int count=0;var more=Button("加载更多弹幕");
        void AddPage()
        {
            items.Children.Remove(more);
            foreach(var item in all.Skip(count).Take(200))
            {
                var button=Button("");var text=Text(BilibiliClient.DurationLabel(item.Time)+"   "+item.Text,13);text.TextTrimming=TextTrimming.CharacterEllipsis;
                button.Content=text;button.ToolTip=item.Text;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.Height=36;
                button.Click+=(_,_)=>{player?.Command("seek",item.Time.ToString(System.Globalization.CultureInfo.InvariantCulture),"absolute+exact");overlay?.Danmaku.Wake();overlay?.ShowFeedback("已跳转到弹幕时间",0);};
                var menu=new ContextMenu();var report=new MenuItem{Header="举报这条弹幕"};report.Click+=async(_,_)=>await ReportHoveredDanmaku(item);menu.Items.Add(report);button.ContextMenu=menu;items.Children.Add(button);count++;
            }
            if(count<all.Count)items.Children.Add(more);
        }
        more.Click+=(_,_)=>AddPage();AddPage();
        overlay.ShowPanel(panel,anchor,CanvasBrush,Surface,()=>{opened=false;cancellation.Cancel();cancellation.Dispose();SavePreferences();});
        try
        {
            var complete=await api.GetDanmaku(videoPart.Cid,videoPart.Duration,ct);
            if(!opened||ct.IsCancellationRequested||part?.Cid!=videoPart.Cid)return;
            all=complete;items.Children.Clear();count=0;heading.Text="弹幕列表 · "+all.Count+" 条";AddPage();
        }
        catch(OperationCanceledException){}
        catch(Exception error){if(opened)heading.Text="完整列表加载失败："+error.Message;}
    }
    private void ShowDanmakuSendStyle(Button anchor)
    {
        var p=preferences.Danmaku;var panel=new StackPanel{Width=250};panel.Children.Add(Text("发送弹幕样式",16,FontWeights.SemiBold));panel.Children.Add(Text("位置",12));var modes=new ComboBox{Foreground=Ink,ItemsSource=new[]{"滚动","顶部","底部"},SelectedIndex=p.SendMode==5?1:p.SendMode==4?2:0,Height=36,Margin=new Thickness(0,6,0,12)};modes.SelectionChanged+=(_,_)=>p.SendMode=new[]{1,5,4}[modes.SelectedIndex];panel.Children.Add(modes);panel.Children.Add(Text("字号",12));var size=new ComboBox{Foreground=Ink,ItemsSource=new[]{"小 · 18","标准 · 25","大 · 36"},SelectedIndex=p.SendFontSize==18?0:p.SendFontSize==36?2:1,Height=36,Margin=new Thickness(0,6,0,12)};size.SelectionChanged+=(_,_)=>p.SendFontSize=new[]{18,25,36}[size.SelectedIndex];panel.Children.Add(size);panel.Children.Add(Text("颜色",12));var colors=new UniformGrid{Columns=4,Margin=new Thickness(0,8,0,0)};var buttons=new Dictionary<uint,Button>();foreach(uint color in new uint[]{0xFFFFFF,0xFF4747,0xFF9C27,0xFFEA00,0x55CE61,0x00CFFF,0x8474FF,0xFF478C}){var b=Button("");buttons[color]=b;b.Content=new Border{Width=24,Height=24,CornerRadius=new CornerRadius(12),Background=new SolidColorBrush(Color.FromRgb((byte)(color>>16),(byte)(color>>8),(byte)color)),BorderBrush=Muted,BorderThickness=new Thickness(1)};b.Height=42;b.Background=p.SendColor==color?PalePink:Brushes.Transparent;b.Click+=(_,_)=>{p.SendColor=color;foreach(var pair in buttons)pair.Value.Background=pair.Key==color?PalePink:Brushes.Transparent;};colors.Children.Add(b);}panel.Children.Add(colors);OpenPlayerPopup(panel,anchor);
    }
    private async Task ReportHoveredDanmaku(DanmakuItem item)=>await Run(async()=>
    {
        if(!RequireLogin())return;var panel=new StackPanel();panel.Children.Add(Text("举报弹幕",20,FontWeights.SemiBold));var quote=Text(item.Text,13);quote.TextWrapping=TextWrapping.Wrap;quote.MaxHeight=80;quote.Margin=new Thickness(0,12,0,12);panel.Children.Add(quote);var reasons=new[]{("违法违禁",1),("色情低俗",2),("恶意刷屏",9),("赌博诈骗",3),("人身攻击",4),("侵犯隐私",5),("垃圾广告",6),("视频无关",10),("引战",7),("剧透",8),("青少年不良信息",12),("违法信息外链",13),("其他",11)};var reason=new ComboBox{Foreground=Ink,ItemsSource=reasons.Select(r=>r.Item1),SelectedIndex=0,Height=38};panel.Children.Add(reason);var explanation=new TextBox{MaxLength=200,Margin=new Thickness(0,12,0,0),Height=60,AcceptsReturn=true};panel.Children.Add(explanation);if(!Dialog(panel,"提交举报"))return;await api.ReportDanmaku(part!.Cid,item.Id,reasons[reason.SelectedIndex].Item2,explanation.Text);Notify("举报已提交");
    });
}




