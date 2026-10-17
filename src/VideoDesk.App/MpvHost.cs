using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows.Interop;
using VideoDesk.Core;

namespace VideoDesk.App;

internal sealed class MpvHost : HwndHost
{
    private readonly bool hardware;
    private readonly bool preview;
    public MpvHost(bool hardware=true,bool preview=false){this.hardware=hardware;this.preview=preview;}
    private IntPtr child, mpv;
    private bool replacing,fileLoaded,eofNotified;
    private readonly Dictionary<string,double> observedNumbers=[];
    private readonly Dictionary<string,string> observedFlags=[];
    private WakeupCallback? wakeup;
    private int pumpQueued;
    private static readonly object shutdownGate=new();
    private static readonly HashSet<Task> shutdowns=[];
    internal Task ShutdownCompleted {get;private set;}=Task.CompletedTask;
    public event Action<string>? StateChanged;
    public long LoadRevision {get;private set;}
    public bool Ready=>mpv!=IntPtr.Zero;
    public event Action<string>? PlaybackFailed;
    public event Action? FileLoaded;
    public event Action<long>? PlaybackEnded;
    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        child=CreateWindowEx(0,"static","",0x50000000|0x02000000|0x04000000|0x00000004,0,0,16,16,parent.Handle,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
        mpv=mpv_create();if(mpv==IntPtr.Zero)throw new InvalidOperationException("无法创建播放器");
        Option("wid",child.ToInt64().ToString());Option("vo","gpu");Option("gpu-api","d3d11");Option("hwdec",hardware?"auto":"no");Option("hwdec-extra-frames","2");Option("osc","no");Option("input-default-bindings","no");Option("input-vo-keyboard","no");Option("terminal","no");Option("keep-open","yes");Option("idle","yes");Option("demuxer-max-bytes","12MiB");Option("demuxer-max-back-bytes","2MiB");Option("audio-client-name","VideoDesk");Option("title","VideoDesk video");
        Option("user-agent",BilibiliClient.UserAgent);Option("referrer","https://www.bilibili.com/");
        Option("volume","60");
        if(preview){Option("aid","no");Option("mute","yes");Option("volume","0");Option("demuxer-max-bytes","3MiB");Option("demuxer-max-back-bytes","1MiB");Option("demuxer-readahead-secs","2");}
        var proxy=System.Net.Http.HttpClient.DefaultProxy.GetProxy(new Uri("https://www.bilibili.com/"));
        if(proxy is not null && proxy.Host!="www.bilibili.com")Option("http-proxy",proxy.ToString());
        var code=mpv_initialize(mpv);if(code<0)throw new InvalidOperationException("播放器初始化失败："+Error(code));
        foreach(var name in new[]{"time-pos","duration","speed","video-params/w","video-params/h","volume","demuxer-cache-duration"})mpv_observe_property(mpv,0,name,5);
        foreach(var name in new[]{"pause","paused-for-cache","eof-reached"})mpv_observe_property(mpv,0,name,3);
        wakeup=_=>QueuePump();mpv_set_wakeup_callback(mpv,wakeup,IntPtr.Zero);
        return new HandleRef(this,child);
    }
    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        StateChanged=null;PlaybackFailed=null;FileLoaded=null;PlaybackEnded=null;
        var context=mpv;mpv=IntPtr.Zero;child=IntPtr.Zero;
        if(context==IntPtr.Zero){DestroyWindow(hwnd.Handle);return;}
        // Keep the UI-owned child HWND valid while libmpv releases its renderer.
        // Navigation may finish immediately; application close drains this work
        // before the owner HWND is destroyed.
        ShowWindow(hwnd.Handle,0);mpv_set_wakeup_callback(context,null,IntPtr.Zero);
        ShutdownCompleted=ReleaseNative(context,hwnd.Handle);
        lock(shutdownGate)shutdowns.Add(ShutdownCompleted);
        _=ShutdownCompleted.ContinueWith(task=>{lock(shutdownGate)shutdowns.Remove(task);_ = task.Exception;},TaskScheduler.Default);
    }
    private async Task ReleaseNative(IntPtr context,IntPtr window)
    {
        await Task.Run(()=>mpv_terminate_destroy(context)).ConfigureAwait(false);
        if(!Dispatcher.HasShutdownStarted)await Dispatcher.InvokeAsync(()=>DestroyWindow(window));
    }
    internal static async Task DrainShutdownsAsync()
    {
        while(true){Task[] pending;lock(shutdownGate)pending=shutdowns.ToArray();if(pending.Length==0)return;await Task.WhenAll(pending);}
    }
    internal static async Task WaitForShutdownCapacityAsync(CancellationToken ct)
    {
        while(true){Task[] pending;lock(shutdownGate)pending=shutdowns.ToArray();if(pending.Length<2)return;await Task.WhenAny(pending).WaitAsync(ct);}
    }
    private void QueuePump()
    {
        if(!Ready||Dispatcher.HasShutdownStarted||Interlocked.Exchange(ref pumpQueued,1)!=0)return;
        try{_ = Dispatcher.BeginInvoke(()=>{Interlocked.Exchange(ref pumpQueued,0);if(Ready)PumpEvents();},System.Windows.Threading.DispatcherPriority.Background);}catch(InvalidOperationException){Interlocked.Exchange(ref pumpQueued,0);}
    }
    private void Option(string key,string value){var code=mpv_set_option_string(mpv,key,value);if(code<0)throw new InvalidOperationException(key+": "+Error(code));}
    public void Load(PlayStream stream,double start=0)
    {
        replacing=true;
        try
        {
            PumpEvents();LoadRevision++;fileLoaded=false;eofNotified=false;
            Set("audio-files","");if(stream.AudioUrl!=null)Set("audio-files",stream.AudioUrl);
            Set("pause","no");Set("start",Math.Max(0,start).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Command("loadfile",stream.VideoUrl,"replace");
        }
        finally{replacing=false;}
    }
    public void Command(params string[] args)
    {
        if(!Ready)return;
        if(args.Length>0&&args[0] is "seek" or "loadfile"){observedNumbers.Remove("time-pos");observedFlags.Remove("eof-reached");}
        if(args.Length>1&&args[0]=="cycle"){observedFlags.Remove(args[1]);observedNumbers.Remove(args[1]);}
        var pointers=args.Select(Marshal.StringToCoTaskMemUTF8).ToArray();var array=Marshal.AllocHGlobal((pointers.Length+1)*IntPtr.Size);
        try{for(int i=0;i<pointers.Length;i++)Marshal.WriteIntPtr(array,i*IntPtr.Size,pointers[i]);Marshal.WriteIntPtr(array,pointers.Length*IntPtr.Size,IntPtr.Zero);var code=mpv_command(mpv,array);if(code<0)throw new InvalidOperationException(Error(code));}
        finally{foreach(var p in pointers)Marshal.FreeCoTaskMem(p);Marshal.FreeHGlobal(array);}
    }
    public void Set(string key,string value){if(Ready){var code=mpv_set_property_string(mpv,key,value);if(code<0)throw new InvalidOperationException(key+": "+Error(code));observedNumbers.Remove(key);observedFlags.Remove(key);}}
    public double Number(string key){if(!Ready)return 0;if(observedNumbers.TryGetValue(key,out var cached))return cached;return mpv_get_property(mpv,key,5,out var value)>=0?value:0;}
    public string Text(string key){if(!Ready)return "";if(observedFlags.TryGetValue(key,out var cached))return cached;var p=mpv_get_property_string(mpv,key);if(p==IntPtr.Zero)return "";try{return Marshal.PtrToStringUTF8(p)??"";}finally{mpv_free(p);}}
    public IReadOnlyList<(double Start,double End)> CachedRanges()
    {
        if(!Ready||mpv_get_node(mpv,"demuxer-cache-state",6,out var node)<0)return [];
        try
        {
            var ranges=MapValue(node,"seekable-ranges");if(ranges.Format!=7)return [];
            var list=Marshal.PtrToStructure<MpvNodeList>(ranges.Pointer);var result=new List<(double,double)>();
            for(int i=0;i<Math.Min(list.Count,256);i++){var range=Marshal.PtrToStructure<MpvNode>(list.Values+i*16);var start=MapValue(range,"start");var end=MapValue(range,"end");if(start.Format==5&&end.Format==5&&end.Number>start.Number)result.Add((start.Number,end.Number));}return result;
        }
        finally{mpv_free_node_contents(ref node);}
    }
    private static MpvNode MapValue(MpvNode map,string key)
    {
        if(map.Format!=8||map.Pointer==IntPtr.Zero)return default;var list=Marshal.PtrToStructure<MpvNodeList>(map.Pointer);
        for(int i=0;i<Math.Min(list.Count,256);i++)if(Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(list.Keys,i*IntPtr.Size))==key)return Marshal.PtrToStructure<MpvNode>(list.Values+i*16);return default;
    }
    [StructLayout(LayoutKind.Explicit,Size=16)]private struct MpvNode{[FieldOffset(0)]public IntPtr Pointer;[FieldOffset(0)]public double Number;[FieldOffset(8)]public int Format;}
    [StructLayout(LayoutKind.Sequential)]private struct MpvNodeList{public int Count;public IntPtr Values,Keys;}
    [DllImport("libmpv-2.dll",EntryPoint="mpv_get_property",CallingConvention=CallingConvention.Cdecl)]private static extern int mpv_get_node(IntPtr handle,[MarshalAs(UnmanagedType.LPUTF8Str)]string key,int format,out MpvNode value);
    [DllImport("libmpv-2.dll",CallingConvention=CallingConvention.Cdecl)]private static extern void mpv_free_node_contents(ref MpvNode value);
    public void PumpEvents()
    {
        if(!Ready)return;
        for(var i=0;i<64&&Ready;i++)
        {
            var p=mpv_wait_event(mpv,0);var ev=Marshal.PtrToStructure<MpvEvent>(p);if(ev.Id==0)break;
            if(ev.Id==8){fileLoaded=true;FileLoaded?.Invoke();}
            if(ev.Id==7&&ev.Data!=IntPtr.Zero){var end=Marshal.PtrToStructure<EndFile>(ev.Data);if(end.Reason==0)NotifyEnd();fileLoaded=false;if(end.Reason==4)PlaybackFailed?.Invoke("视频加载失败："+Error(end.Error));}
            if(ev.Id==22&&ev.Data!=IntPtr.Zero)
            {
                var property=Marshal.PtrToStructure<EventProperty>(ev.Data);var name=Marshal.PtrToStringUTF8(property.Name);
                if(name!=null)
                {
                    if(property.Format==5&&property.Data!=IntPtr.Zero)observedNumbers[name]=Marshal.PtrToStructure<double>(property.Data);
                    else if(property.Format==3&&property.Data!=IntPtr.Zero)observedFlags[name]=Marshal.ReadInt32(property.Data)!=0?"yes":"no";
                    else{observedNumbers.Remove(name);observedFlags.Remove(name);}
                    StateChanged?.Invoke(name);
                }
            }
            if(i==63)QueuePump();
        }
        // keep-open retains the file and final frame; end-file need not arrive.
        // eof-reached is the authoritative completion signal in that mode.
        if(Ready&&fileLoaded&&!replacing){if(Text("eof-reached")=="yes")NotifyEnd();else eofNotified=false;}
    }
    private void NotifyEnd(){if(replacing||!fileLoaded||eofNotified)return;eofNotified=true;PlaybackEnded?.Invoke(LoadRevision);}
    private static string Error(int code)=>Marshal.PtrToStringUTF8(mpv_error_string(code))??code.ToString();
    [StructLayout(LayoutKind.Sequential)]private struct MpvEvent{public int Id;public int Error;public ulong Reply;public IntPtr Data;}
    [StructLayout(LayoutKind.Sequential)]private struct EndFile{public int Reason;public int Error;}
    [StructLayout(LayoutKind.Sequential)]private struct EventProperty{public IntPtr Name;public int Format;public IntPtr Data;}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate void WakeupCallback(IntPtr data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr CreateWindowEx(int ex,string cls,string name,int style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr param);
    [DllImport("user32.dll")]private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")]private static extern bool ShowWindow(IntPtr hwnd,int command);
    [DllImport("libmpv-2.dll",CallingConvention=CallingConvention.Cdecl)]private static extern int mpv_observe_property(IntPtr handle,ulong reply,[MarshalAs(UnmanagedType.LPUTF8Str)]string name,int format);
    [DllImport("libmpv-2.dll",CallingConvention=CallingConvention.Cdecl)]private static extern void mpv_set_wakeup_callback(IntPtr handle,WakeupCallback? callback,IntPtr data);
    [DllImport("libmpv-2.dll",CallingConvention=CallingConvention.Cdecl)]private static extern IntPtr mpv_create();
    [DllImport("libmpv-2.dll",CallingConvention=CallingConvention.Cdecl)]private static extern int mpv_initialize(IntPtr handle);
    [DllImport("libmpv-2.dll",CallingConvention=CallingConvention.Cdecl)]private static extern void mpv_terminate_destroy(IntPtr handle);
    [DllImport("libmpv-2.dll",CallingConvention=CallingConvention.Cdecl)]private static extern int mpv_set_option_string(IntPtr handle,[MarshalAs(UnmanagedType.LPUTF8Str)]string key,[MarshalAs(UnmanagedType.LPUTF8Str)]string value);
    [DllImport("libmpv-2.dll",CallingConvention=CallingConvention.Cdecl)]private static extern int mpv_set_property_string(IntPtr handle,[MarshalAs(UnmanagedType.LPUTF8Str)]string key,[MarshalAs(UnmanagedType.LPUTF8Str)]string value);
    [DllImport("libmpv-2.dll",CallingConvention=CallingConvention.Cdecl)]private static extern int mpv_get_property(IntPtr handle,[MarshalAs(UnmanagedType.LPUTF8Str)]string key,int format,out double value);
    [DllImport("libmpv-2.dll",CallingConvention=CallingConvention.Cdecl)]private static extern IntPtr mpv_get_property_string(IntPtr handle,[MarshalAs(UnmanagedType.LPUTF8Str)]string key);
    [DllImport("libmpv-2.dll",CallingConvention=CallingConvention.Cdecl)]private static extern int mpv_command(IntPtr handle,IntPtr args);
    [DllImport("libmpv-2.dll",CallingConvention=CallingConvention.Cdecl)]private static extern IntPtr mpv_wait_event(IntPtr handle,double timeout);
    [DllImport("libmpv-2.dll",CallingConvention=CallingConvention.Cdecl)]private static extern IntPtr mpv_error_string(int code);
    [DllImport("libmpv-2.dll",CallingConvention=CallingConvention.Cdecl)]private static extern void mpv_free(IntPtr data);
}
