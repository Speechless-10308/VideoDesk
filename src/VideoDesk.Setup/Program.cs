using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VideoDesk.Setup;

internal static class Program
{
    [STAThread] public static void Main()
    {
        new Application().Run(new SetupWindow());
    }
    internal static ZipArchive Payload()
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("ClientPayload.zip")??throw new InvalidDataException("安装包缺少客户端");var memory=new MemoryStream();stream.CopyTo(memory);memory.Position=0;
        using var hashStream=Assembly.GetExecutingAssembly().GetManifestResourceStream("ClientPayload.sha256")!;using var reader=new StreamReader(hashStream);var expected=reader.ReadToEnd().Trim();var actual=Convert.ToHexString(SHA256.HashData(memory));if(!actual.Equals(expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("安装包校验失败");memory.Position=0;return new ZipArchive(memory,ZipArchiveMode.Read);
    }
    internal static string[] Extract(ZipArchive archive,string target,IProgress<double>? progress)
    {
        var prefix=Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;var result=new List<string>();long total=archive.Entries.Sum(e=>e.Length),done=0;
        foreach(var entry in archive.Entries)
        {
            var name=entry.FullName.Replace('\\','/');var file=Path.GetFullPath(Path.Combine(prefix,name));if(!file.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||name.StartsWith(".local/",StringComparison.OrdinalIgnoreCase)||name.Contains("session.dpapi",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("安装包包含无效路径");
            if(name.EndsWith('/')){Directory.CreateDirectory(file);continue;}Directory.CreateDirectory(Path.GetDirectoryName(file)!);entry.ExtractToFile(file,true);result.Add(name);done+=entry.Length;progress?.Report(done/(double)Math.Max(1,total));
        }
        return result.ToArray();
    }
}
internal sealed class SetupWindow : Window
{
    private readonly TextBox destination=new(){Text=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","VideoDesk"),Height=38,FontSize=14,Padding=new Thickness(10,6,10,6)};
    private readonly CheckBox desktop=new(){Content="创建桌面快捷方式",IsChecked=true,Margin=new Thickness(0,18,0,16)};
    private readonly Button install=new(){Content="安装",Width=104,Height=40,Background=new SolidColorBrush(Color.FromRgb(255,71,140)),Foreground=Brushes.White};
    private readonly ProgressBar progress=new(){Height=5,Maximum=1,Margin=new Thickness(0,16,0,8)};
    private readonly TextBlock status=new(){Text="安装到当前用户，无需管理员权限。",Foreground=Brushes.Gray,TextWrapping=TextWrapping.Wrap,FontSize=13};
    private bool busy;
    public SetupWindow()
    {
        Title="安装 VideoDesk";Width=620;Height=400;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterScreen;FontFamily=new FontFamily("Microsoft YaHei UI");Background=Brushes.White;
        var panel=new StackPanel{Margin=new Thickness(32)};panel.Children.Add(new TextBlock{Text="VideoDesk",FontSize=30,FontWeight=FontWeights.ExtraBold,Foreground=install.Background});panel.Children.Add(new TextBlock{Text="安装客户端",FontSize=19,Margin=new Thickness(0,10,0,22)});var row=new DockPanel();var browse=new Button{Content="浏览",Width=70,Height=38,Margin=new Thickness(10,0,0,0)};browse.Click+=(_,_)=>{var picker=new Microsoft.Win32.OpenFolderDialog{Title="选择安装文件夹"};if(picker.ShowDialog(this)==true)destination.Text=Path.Combine(picker.FolderName,"VideoDesk");};DockPanel.SetDock(browse,Dock.Right);row.Children.Add(browse);row.Children.Add(destination);panel.Children.Add(row);panel.Children.Add(desktop);panel.Children.Add(progress);panel.Children.Add(status);var bottom=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0)};var cancel=new Button{Content="关闭",Width=85,Height=40,Margin=new Thickness(0,0,10,0)};cancel.Click+=(_,_)=>Close();bottom.Children.Add(cancel);bottom.Children.Add(install);panel.Children.Add(bottom);Content=panel;install.Click+=async(_,_)=>await Install();Closing+=(_,e)=>{if(busy)e.Cancel=true;};
    }
    private async Task Install()
    {
        if(install.Content?.ToString()=="完成"){Close();return;}string target;
        try{target=Path.GetFullPath(destination.Text.Trim()).TrimEnd(Path.DirectorySeparatorChar);if(Path.GetPathRoot(target)==target||target.Length<8)throw new IOException("请选择客户端专用文件夹");if(Directory.Exists(target)&&Directory.EnumerateFileSystemEntries(target).Any()&&!File.Exists(Path.Combine(target,"installed-files.json")))throw new IOException("这个文件夹包含其他文件，请选择一个新的 VideoDesk 文件夹");}
        catch(Exception e){status.Text=e.Message;return;}
        busy=true;install.IsEnabled=false;destination.IsEnabled=false;desktop.IsEnabled=false;status.Text="正在校验并安装…";var createDesktop=desktop.IsChecked==true;
        try
        {
            var feedback=new Progress<double>(value=>progress.Value=value);var files=await Task.Run(()=>
            {
                var staging=target+".stage-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(staging);using var archive=Program.Payload();var copied=Program.Extract(archive,staging,feedback);var previous=target+".previous-"+Guid.NewGuid().ToString("N");bool moved=false;
                try
                {
                    if(Directory.Exists(target)){Directory.Move(target,previous);moved=true;}Directory.Move(staging,target);
                    if(moved)foreach(var directory in new[]{".local","captures"}){var old=Path.Combine(previous,directory);if(Directory.Exists(old))Directory.Move(old,Path.Combine(target,directory));}
                }
                catch{if(!Directory.Exists(target)&&moved)Directory.Move(previous,target);throw;}
                // Retain the previous directory for recovery; updates never silently delete account data.
                return copied;
            });
            var shortcuts=new List<string>();var shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            void Shortcut(string folder){Directory.CreateDirectory(folder);var path=Path.Combine(folder,"VideoDesk.lnk");dynamic link=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,[path])!;link.TargetPath=Path.Combine(target,"VideoDesk.exe");link.WorkingDirectory=target;link.Description="VideoDesk Bilibili 客户端";link.Save();shortcuts.Add(path);}
            Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),"Programs"));if(createDesktop)Shortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            var script=Path.Combine(target,"uninstall.ps1");File.WriteAllText(script,UninstallScript);File.WriteAllText(Path.Combine(target,"installed-files.json"),JsonSerializer.Serialize(new{files,shortcuts},new JsonSerializerOptions{WriteIndented=true}));
            using var key=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\VideoDesk");key.SetValue("DisplayName","VideoDesk");key.SetValue("DisplayVersion",Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)??"0.1.2");key.SetValue("InstallLocation",target);key.SetValue("DisplayIcon",Path.Combine(target,"VideoDesk.exe"));key.SetValue("UninstallString","powershell.exe -NoProfile -WindowStyle Hidden -Command \"& ([scriptblock]::Create([IO.File]::ReadAllText('"+script.Replace("'","''")+"')))\"");
            progress.Value=1;status.Text="安装完成。可以从开始菜单打开 VideoDesk。";install.Content="完成";
        }
        catch(Exception e){status.Text="安装未完成，请关闭正在运行的客户端后重试。"+e.Message;destination.IsEnabled=true;desktop.IsEnabled=true;}
        finally{busy=false;install.IsEnabled=true;}
    }
    private const string UninstallScript="""
Add-Type -AssemblyName System.Windows.Forms
if([System.Windows.Forms.MessageBox]::Show('卸载 VideoDesk？登录数据、观看记录与抓包档案将保留。','VideoDesk','YesNo') -ne 'Yes'){return}
$taskKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\VideoDesk'
$taskRoot=(Get-ItemProperty -LiteralPath $taskKey).InstallLocation
$taskAbsolute=[IO.Path]::GetFullPath($taskRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
$taskPrefix=$taskAbsolute+[IO.Path]::DirectorySeparatorChar
$taskManifest=Get-Content -LiteralPath (Join-Path $taskAbsolute 'installed-files.json') -Raw | ConvertFrom-Json
try {
  foreach($taskRelative in $taskManifest.files){$taskFile=[IO.Path]::GetFullPath((Join-Path $taskAbsolute $taskRelative));if(-not $taskFile.StartsWith($taskPrefix,[StringComparison]::OrdinalIgnoreCase)){throw '卸载文件路径无效'};if(Test-Path -LiteralPath $taskFile){Remove-Item -LiteralPath $taskFile -Force -ErrorAction Stop}}
  foreach($taskShortcut in $taskManifest.shortcuts){if(Test-Path -LiteralPath $taskShortcut){$taskShell=New-Object -ComObject WScript.Shell;$taskLink=$taskShell.CreateShortcut($taskShortcut);if($taskLink.TargetPath -eq (Join-Path $taskAbsolute 'VideoDesk.exe')){Remove-Item -LiteralPath $taskShortcut -Force}}}
  Remove-Item -LiteralPath $taskKey -Force
  Remove-Item -LiteralPath (Join-Path $taskAbsolute 'installed-files.json') -Force
  Remove-Item -LiteralPath (Join-Path $taskAbsolute 'uninstall.ps1') -Force
  [System.Windows.Forms.MessageBox]::Show('程序已卸载。你的本地数据保留在 '+$taskAbsolute,'VideoDesk') | Out-Null
} catch {[System.Windows.Forms.MessageBox]::Show('请先关闭客户端再卸载。'+$_.Exception.Message,'VideoDesk') | Out-Null}
""";
}



