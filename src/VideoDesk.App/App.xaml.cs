using System.IO;
using System.Windows;
using VideoDesk.Core;

namespace VideoDesk.App;

public partial class App : Application
{
    public static string Workspace { get; private set; } = "";
    public static string[] Arguments { get; private set; } = [];
    public static string DataDirectory => Arguments.FirstOrDefault(argument=>argument.StartsWith("--data-root="))?[12..]??Path.Combine(Workspace,".local");
    public static SessionDiagnostics Diagnostics { get; private set; } = new(AppContext.BaseDirectory, []);
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Arguments=e.Args;
        Workspace=e.Args.FirstOrDefault(a=>a.StartsWith("--root="))?[7..]??FindWorkspace();
        Diagnostics=new(DataDirectory,e.Args);
        Directory.CreateDirectory(DataDirectory);
        DispatcherUnhandledException+=(s,args)=> { _=Diagnostics.AppendAsync("errors.log",()=>DateTimeOffset.UtcNow+" "+args.Exception+Environment.NewLine);args.Handled=true;MessageBox.Show(args.Exception.Message,"VideoDesk",MessageBoxButton.OK,MessageBoxImage.Error); };
        var window=new MainWindow(); MainWindow=window;window.Show();
    }
    private static string FindWorkspace()
    {
        var d=new DirectoryInfo(AppContext.BaseDirectory);
        while(d!=null){if(File.Exists(Path.Combine(d.FullName,"VideoDesk.slnx")))return d.FullName;d=d.Parent;}
        return AppContext.BaseDirectory;
    }
}
