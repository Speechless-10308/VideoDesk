using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows.Interop;
using System.Windows.Threading;
using VideoDesk.Core;

namespace VideoDesk.App;

internal sealed partial class MpvHost : HwndHost
{
    private readonly bool hardware;
    private readonly bool preview;
    private DispatcherOperation? seekDispatch;
    public MpvHost(bool hardware=true,bool preview=false)
    {
        this.hardware=hardware;this.preview=preview;
        startupTimer.Tick+=(_,_)=>CheckStartup();
    }
    private IntPtr child, mpv;
    private bool replacing,initialized,eofNotified,prepared,pumping;
    private readonly PlaybackLoadState loadState=new();
    private (PlayStream Stream,double Start)? pendingLoad;
    private Stopwatch? loadWatch;
    private bool playbackRestartMeasured;
    private readonly Dictionary<string,double> observedNumbers=[];
    private readonly Dictionary<string,string> observedFlags=[];
    private WakeupCallback? wakeup;
    private int pumpQueued;
    private static readonly object shutdownGate=new();
    private static readonly HashSet<Task> shutdowns=[];
    internal Task ShutdownCompleted {get;private set;}=Task.CompletedTask;
    public event Action<string>? StateChanged;
    public long LoadRevision=>loadState.Revision;
    public bool Ready=>mpv!=IntPtr.Zero&&initialized;
    public bool MediaReady=>Ready&&loadState.Loaded;
    public bool Seeking=>loadState.HasPendingSeek||loadState.Seeking||Ready&&Text("seeking")=="yes";
    public double ResumePosition=>loadState.SeekTarget??(MediaReady&&Number("time-pos")>0?Number("time-pos"):resumePosition);
    public bool CompletionPending=>eofNotified&&!loadState.HasPendingSeek&&!loadState.Seeking;
    public event Action<string>? PlaybackFailed;
    public event Action? FileLoaded;
    public event Action<long>? PlaybackEnded;
    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        var buildWatch=Stopwatch.StartNew();
        child=CreateWindowEx(0,"static","",0x50000000|0x02000000|0x04000000|0x00000004,0,0,16,16,parent.Handle,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
        mpv=mpv_create();if(mpv==IntPtr.Zero)throw new InvalidOperationException("无法创建播放器");
        Option("wid",child.ToInt64().ToString());Option("vo","gpu");Option("gpu-api","d3d11");Option("hwdec",hardware?"auto":"no");Option("osc","no");Option("input-default-bindings","no");Option("input-vo-keyboard","no");Option("terminal","no");Option("keep-open","yes");Option("idle","yes");Option("demuxer-max-bytes","64MiB");Option("demuxer-max-back-bytes","8MiB");Option("network-timeout","15");Option("audio-client-name","VideoDesk");Option("title","VideoDesk video");
        Option("user-agent",BilibiliClient.UserAgent);Option("referrer","https://www.bilibili.com/");
        Option("volume","60");
        if(preview){Option("aid","no");Option("mute","yes");Option("volume","0");Option("demuxer-max-bytes","3MiB");Option("demuxer-max-back-bytes","1MiB");Option("demuxer-readahead-secs","2");}
        var proxyWatch=Stopwatch.StartNew();var proxy=System.Net.Http.HttpClient.DefaultProxy.GetProxy(new Uri("https://www.bilibili.com/"));var proxyMs=proxyWatch.Elapsed.TotalMilliseconds;
        if(proxy is not null && proxy.Host!="www.bilibili.com")Option("http-proxy",proxy.ToString());
        var initWatch=Stopwatch.StartNew();var code=mpv_initialize(mpv);if(code<0)throw new InvalidOperationException("播放器初始化失败："+Error(code));initialized=true;
        if(App.Diagnostics.Enabled){var initMs=initWatch.Elapsed.TotalMilliseconds;var totalMs=buildWatch.Elapsed.TotalMilliseconds;_=App.Diagnostics.AppendAsync("player-timing.log",()=>FormattableString.Invariant($"mpv-create proxy={proxyMs:F1} ms initialize={initMs:F1} ms total={totalMs:F1} ms")+Environment.NewLine);}
        foreach(var name in new[]{"time-pos","duration","speed","video-params/w","video-params/h","volume","demuxer-cache-duration"})mpv_observe_property(mpv,0,name,5);
        foreach(var name in new[]{"pause","paused-for-cache","eof-reached","seeking"})mpv_observe_property(mpv,0,name,3);
        mpv_request_log_messages(mpv,"warn");
        wakeup=_=>QueuePump();mpv_set_wakeup_callback(mpv,wakeup,IntPtr.Zero);
        if(pendingLoad!=null)_=Dispatcher.BeginInvoke(LoadPending);
        return new HandleRef(this,child);
    }
    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        CancelScheduledSeek();startupTimer.Stop();
        StateChanged=null;PlaybackFailed=null;FileLoaded=null;PlaybackEnded=null;PresentationChanged=null;
        var context=mpv;mpv=IntPtr.Zero;initialized=false;pendingLoad=null;child=IntPtr.Zero;
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
        try{_ = Dispatcher.BeginInvoke(()=>{Interlocked.Exchange(ref pumpQueued,0);if(Ready)PumpEvents();},DispatcherPriority.Loaded);}catch(InvalidOperationException){Interlocked.Exchange(ref pumpQueued,0);}
    }
    private void Option(string key,string value){var code=mpv_set_option_string(mpv,key,value);if(code<0)throw new InvalidOperationException(key+": "+Error(code));}
    public void Load(PlayStream stream,double start=0)
    {
        if(!prepared)PrepareLoad(start);prepared=false;currentLoad=pendingLoad=(stream,start);softwareFallback=false;
        health.Start(healthClock.Elapsed,start);
        if(Ready)LoadPending();
    }
    public void PrepareLoad(double start=0)
    {
        CancelScheduledSeek();startupTimer.Stop();hasFrame=false;currentLoad=null;LastNativeError="";resumePosition=start;
        SetPresentation(PlayerPresentation.Loading,"正在加载视频");
        replacing=true;
        try
        {
            if(Ready){PumpEvents();SetNative("pause","yes");}
            loadState.Begin(start);observedNumbers.Clear();observedFlags.Clear();pendingLoad=null;eofNotified=false;prepared=true;
        }
        finally{replacing=false;}
    }
    private void LoadPending()
    {
        if(!Ready||pendingLoad is not {} load)return;pendingLoad=null;var (stream,start)=load;loadWatch=Stopwatch.StartNew();playbackRestartMeasured=false;
        replacing=true;
        try
        {
            // File options are applied after the previous file has unloaded.
            // Unpausing the old EOF frame here can let keep-open pause the new file.
            var options="start="+Math.Max(0,start).ToString(System.Globalization.CultureInfo.InvariantCulture)+",pause="+(loadState.Paused?"yes":"no")+",hwdec="+(hardware&&!softwareFallback?"auto":"no");
            var code=InvokeCommand(["loadfile",MpvMediaSource.Create(stream),"replace","-1",options],true,out var result);
            try
            {
                if(code<0)throw new InvalidOperationException("loadfile: "+Error(code));
                var entry=MapValue(result,"playlist_entry_id");
                if(entry.Format!=4)throw new InvalidOperationException("播放器没有返回本次加载的标识");
                loadState.Expect(entry.Integer);
                startupTimer.Start();
            }
            finally{mpv_free_node_contents(ref result);}
        }
        catch(Exception error){LastNativeError=error.Message;RecoverOrFail("视频暂时无法播放");}
        finally{replacing=false;}
    }
    public void Command(params string[] args)
    {
        if(args.Length==0)return;
        if(args[0]=="seek")
        {
            if(!CanInteract)return;
            if(args.Length>1&&double.TryParse(args[1],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var seconds)&&loadState.Seek(seconds,args.Length>2&&args[2].Contains("absolute",StringComparison.Ordinal),Number("time-pos")))
            {
                eofNotified=false;StateChanged?.Invoke("seeking");ScheduleSeek();
            }
            return;
        }
        if(args.Length>1&&args[0]=="cycle"&&args[1]=="pause")
        {
            if(!CanInteract)return;
            Set("pause",Text("pause")=="yes"?"no":"yes");return;
        }
        if(!Ready)return;
        if(args.Length>1&&args[0]=="cycle"){observedFlags.Remove(args[1]);observedNumbers.Remove(args[1]);}
        var code=InvokeCommand(args,false,out _);if(code<0)throw new InvalidOperationException(args[0]+": "+Error(code));
    }
    private int InvokeCommand(string[] args,bool readResult,out MpvNode result)
    {
        var pointers=args.Select(Marshal.StringToCoTaskMemUTF8).ToArray();var array=Marshal.AllocHGlobal((pointers.Length+1)*IntPtr.Size);result=default;
        try
        {
            for(int i=0;i<pointers.Length;i++)Marshal.WriteIntPtr(array,i*IntPtr.Size,pointers[i]);Marshal.WriteIntPtr(array,pointers.Length*IntPtr.Size,IntPtr.Zero);
            return readResult?mpv_command_ret(mpv,array,out result):mpv_command(mpv,array);
        }
        finally{foreach(var p in pointers)Marshal.FreeCoTaskMem(p);Marshal.FreeHGlobal(array);}
    }
    private void ScheduleSeek()
    {
        if(!Ready||!loadState.Playable||!loadState.HasPendingSeek)return;
        if(seekDispatch?.Status==DispatcherOperationStatus.Pending)return;
        seekDispatch=Dispatcher.BeginInvoke(()=>{seekDispatch=null;FlushSeek();},DispatcherPriority.Input);
    }
    private void CancelScheduledSeek(){seekDispatch?.Abort();seekDispatch=null;}
    private void FlushSeek()
    {
        if(!MediaReady||loadState.TakeSeek() is not {} position)return;
        var duration=Number("duration");if(duration>0)position=Math.Min(position,Math.Max(0,duration-.1));
        observedNumbers.Remove("time-pos");observedFlags.Remove("eof-reached");
        var code=InvokeCommand(["seek",position.ToString(System.Globalization.CultureInfo.InvariantCulture),"absolute+exact"],false,out _);
        if(code<0){loadState.SeekFailed();StateChanged?.Invoke("seeking");PlaybackFailed?.Invoke("无法跳转："+Error(code));}
    }
    public void Set(string key,string value)
    {
        if(key=="pause"&&value is "yes" or "no")
        {
            loadState.SetPaused(value=="yes");
            if(loadState.Loading||loadState.Loaded&&!loadState.Playable)return;
        }
        SetNative(key,value);
    }
    private void SetNative(string key,string value){if(Ready){var code=mpv_set_property_string(mpv,key,value);if(code<0)throw new InvalidOperationException(key+": "+Error(code));observedNumbers.Remove(key);observedFlags.Remove(key);}}
    public double Number(string key){if(!Ready||!loadState.Loaded&&(key is "time-pos" or "duration" or "video-params/w" or "video-params/h"))return 0;if(observedNumbers.TryGetValue(key,out var cached))return cached;return mpv_get_property(mpv,key,5,out var value)>=0?value:0;}
    public string Text(string key){if(key=="pause"&&(loadState.Loading||loadState.Loaded&&!loadState.Playable))return loadState.Paused?"yes":"no";if(!Ready)return "";if(observedFlags.TryGetValue(key,out var cached))return cached;return NativeText(key);}
    private string NativeText(string key){var p=mpv_get_property_string(mpv,key);if(p==IntPtr.Zero)return "";try{return Marshal.PtrToStringUTF8(p)??"";}finally{mpv_free(p);}}
    public IReadOnlyList<(double Start,double End)> CachedRanges()
    {
        if(!MediaReady||mpv_get_node(mpv,"demuxer-cache-state",6,out var node)<0)return [];
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
    [StructLayout(LayoutKind.Explicit,Size=16)]private struct MpvNode{[FieldOffset(0)]public IntPtr Pointer;[FieldOffset(0)]public double Number;[FieldOffset(0)]public long Integer;[FieldOffset(8)]public int Format;}
    [StructLayout(LayoutKind.Sequential)]private struct MpvNodeList{public int Count;public IntPtr Values,Keys;}
    [DllImport("libmpv-2.dll",EntryPoint="mpv_get_property",CallingConvention=CallingConvention.Cdecl)]private static extern int mpv_get_node(IntPtr handle,[MarshalAs(UnmanagedType.LPUTF8Str)]string key,int format,out MpvNode value);
    [DllImport("libmpv-2.dll",CallingConvention=CallingConvention.Cdecl)]private static extern void mpv_free_node_contents(ref MpvNode value);
    public void PumpEvents()
    {
        if(!Ready||pumping)return;
        pumping=true;
        try
        {
        var restarted=false;
        for(var i=0;i<64&&Ready;i++)
        {
            var p=mpv_wait_event(mpv,0);var ev=Marshal.PtrToStructure<MpvEvent>(p);if(ev.Id==0)break;
            if(ev.Id==6&&ev.Data!=IntPtr.Zero){var entry=Marshal.ReadInt64(ev.Data);loadState.Started(entry);if(loadState.IsCurrent(entry)){observedNumbers.Clear();observedFlags.Clear();}}
            if(ev.Id==8&&loadState.FileLoaded())
            {
                observedNumbers.Clear();observedFlags.Clear();RecordLoadTiming("file-loaded");SetNative("pause",loadState.Paused?"yes":"no");FileLoaded?.Invoke();
            }
            if(ev.Id==21&&loadState.PlaybackRestarted())
            {
                restarted=true;
                // A seek during startup waits for decoding to become ready.
                if(!playbackRestartMeasured){SetNative("pause",loadState.Paused?"yes":"no");playbackRestartMeasured=true;RecordLoadTiming("playback-restart");}
                ScheduleSeek();
            }
            if(ev.Id==7&&ev.Data!=IntPtr.Zero){var end=Marshal.PtrToStructure<EndFile>(ev.Data);if(loadState.IsCurrent(end.Entry)&&end.Reason==0)HandleEnd();if(loadState.IsCurrent(end.Entry)&&end.Reason==4){LastNativeError=Error(end.Error);RecoverOrFail("视频加载失败");}loadState.Ended(end.Entry);}
            if(ev.Id==2&&ev.Data!=IntPtr.Zero)ReadNativeLog(ev.Data);
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
        if(Ready&&loadState.Playable&&!replacing){if(Text("eof-reached")=="yes")HandleEnd();else eofNotified=false;}
        // Health checks run at 4 Hz, plus the first decoded frame. A native
        // property notification must not trigger synchronous polling each time.
        if(restarted){CheckStartup();StateChanged?.Invoke("seeking");}
        }
        finally{pumping=false;}
    }
    private void NotifyEnd(){if(replacing||!loadState.Playable||loadState.HasPendingSeek||loadState.Seeking||eofNotified)return;eofNotified=true;PlaybackEnded?.Invoke(LoadRevision);}
    private void RecordLoadTiming(string stage)
    {
        if(!App.Diagnostics.Enabled||loadWatch==null)return;var elapsed=loadWatch.Elapsed.TotalMilliseconds;var revision=LoadRevision;
        _=App.Diagnostics.AppendAsync("player-timing.log",()=>FormattableString.Invariant($"{stage} revision={revision} {elapsed:F1} ms")+Environment.NewLine);
    }
    private static string Error(int code)=>Marshal.PtrToStringUTF8(mpv_error_string(code))??code.ToString();
    [StructLayout(LayoutKind.Sequential)]private struct MpvEvent{public int Id;public int Error;public ulong Reply;public IntPtr Data;}
    [StructLayout(LayoutKind.Sequential)]private struct EndFile{public int Reason;public int Error;public long Entry;}
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
    [DllImport("libmpv-2.dll",CallingConvention=CallingConvention.Cdecl)]private static extern int mpv_command_ret(IntPtr handle,IntPtr args,out MpvNode result);
    [DllImport("libmpv-2.dll",CallingConvention=CallingConvention.Cdecl)]private static extern IntPtr mpv_wait_event(IntPtr handle,double timeout);
    [DllImport("libmpv-2.dll",CallingConvention=CallingConvention.Cdecl)]private static extern IntPtr mpv_error_string(int code);
    [DllImport("libmpv-2.dll",CallingConvention=CallingConvention.Cdecl)]private static extern void mpv_free(IntPtr data);
}
