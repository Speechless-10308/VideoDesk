using System.Diagnostics;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VideoDesk.Core;

namespace VideoDesk.App;

// Retained glyph drawings are shaped once. Frames only move matrices, never recreate WPF text controls.
internal sealed class DanmakuSurface : FrameworkElement,IDisposable
{
    private readonly VisualCollection visuals;
    private readonly List<Entry> active=[];
    private sealed record GlyphKey(string Text,double Font,uint Color,string Family,bool Bold,bool Outline,double Dpi);
    private sealed record Glyph(DrawingGroup Drawing,double Width,double Height);
    private readonly Dictionary<GlyphKey,Glyph> glyphs=[];
    private readonly Dictionary<GlyphKey,Glyph> readyGlyphs=[];
    private readonly Dictionary<GlyphKey,Task<Glyph>> shaping=[];
    private readonly Queue<DanmakuItem> deferred=[];
    private readonly HashSet<long> selfSent=[];
    // WPF font shaping stays on one STA thread, independent of image/network thread-pool work.
    private static readonly BlockingCollection<(GlyphKey Key,TaskCompletionSource<Glyph> Completion)> shapeJobs=new(64);
    private static readonly Lazy<Thread> shapeWorker=new(StartShapeWorker);
    private int rasterized,shapesBuilt,rejectedBeforeShaping,maxPending;
    private long cacheBytes;
    private readonly Queue<double> cpuTimes=[];
    internal bool MeasureFrames {get;set;}
    private IReadOnlyList<DanmakuItem> messages=[];
    private readonly Func<(double Position,bool Paused,double Speed)> clock;
    private readonly Func<bool> waitingForFrame;
    private readonly DanmakuPreferences settings;
    private readonly Stopwatch watch=Stopwatch.StartNew();
    private readonly PlaybackClock timeline=new();
    private TimeSpan renderedAt=TimeSpan.MinValue;
    private double sampledAt,sampledSpeed=1,lastPosition=-100;
    private bool sampledPaused,enabled=true,disposed,coldCursor,awaitingFrame;
    private int next;
    private Entry? hovered;
    private int frames,maxActive;
    private readonly Queue<double> frameGaps=[];
    private double lastFrame;
    private bool rendering;
    private double lastClockPosition=double.NaN;
    private readonly DispatcherTimer clockTimer=new(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(250)};
    public int MessageCount=>messages.Count;
    public int ActiveCount=>active.Count;
    public Rect? FirstVisibleBounds=>active.FirstOrDefault(e=>e.X>=0&&e.X+e.Width<ActualWidth) is {} entry?new Rect(entry.X,entry.Y,entry.Width,entry.Height):null;
    public DanmakuItem? Hovered=>hovered?.Item;
    public DanmakuMaskController? Mask {get;set;}
    public double VideoAspect {get;set;}=16d/9;
    private DanmakuMaskController.MaskFrame? currentMask;
    private string[] blockedWords=[];private DanmakuFilter[] filters=[];private System.Text.RegularExpressions.Regex[] filterPatterns=[];
    public object Diagnostics=>new{messages=messages.Count,active=active.Count,maxActive,drawings=glyphs.Count,frames,rendering,clockPolling=clockTimer.IsEnabled,frameGapP95Ms=Percentile(frameGaps),frameCpuP95Ms=Percentile(cpuTimes),maxFrameCpuMs=cpuTimes.Count>0?cpuTimes.Max():0,shapesBuilt,rejectedBeforeShaping,maxPending,pending=deferred.Count,shaping=shaping.Count,readyShapes=readyGlyphs.Count,shapeJobs=shapeJobs.Count,completedShapes=shaping.Values.Count(t=>t.IsCompleted),position=timeline.Position,next,cacheMiB=cacheBytes/1048576d,hovered=hovered?.Item.Id,clockDiscontinuities=timeline.Discontinuities};
    internal void ResetDiagnostics(){frames=maxActive=shapesBuilt=rejectedBeforeShaping=maxPending=0;frameGaps.Clear();cpuTimes.Clear();lastFrame=0;}
    private static double Percentile(IReadOnlyCollection<double> values)=>values.Count>0?values.OrderBy(x=>x).ElementAt((int)((values.Count-1)*.95)):0;
    private sealed class Entry(DanmakuItem item,DrawingVisual visual,MatrixTransform transform,double width,double height)
    {
        public DanmakuItem Item=item;public DrawingVisual Visual=visual;public MatrixTransform Transform=transform;public double Width=width,Height=height,Start=item.Time,Duration,Y,X,Velocity,HeldAt=-1;public bool Fixed,Advanced;public Point From,To;public double Rotation,AdvancedOpacity=1;
    }
    public DanmakuSurface(Func<(double Position,bool Paused,double Speed)> clock,DanmakuPreferences settings,Func<bool>? waitingForFrame=null)
    {
        this.clock=clock;this.settings=settings;this.waitingForFrame=waitingForFrame??(()=>false);visuals=new(this);ClipToBounds=true;IsHitTestVisible=false;
        Loaded+=(_,_)=>UpdateVisibility();Unloaded+=(_,_)=>{clockTimer.Stop();SetRendering(false);};
        IsVisibleChanged+=(_,_)=>UpdateVisibility();SizeChanged+=(_,_)=>Reset();clockTimer.Tick+=(_,_)=>PollPausedClock();
    }
    private void SetRendering(bool value)
    {
        if(rendering==value)return;rendering=value;
        if(value)CompositionTarget.Rendering+=Frame;else CompositionTarget.Rendering-=Frame;
        lastFrame=0;
    }
    private void UpdateVisibility()
    {
        if(disposed||!enabled||!IsLoaded||!IsVisible){clockTimer.Stop();SetRendering(false);return;}
        clockTimer.Start();Wake();
    }
    public void Wake()
    {
        if(disposed||!enabled||!IsLoaded||!IsVisible)return;
        if(HoldForFrame())return;
        if(awaitingFrame){awaitingFrame=false;timeline.Reset();coldCursor=true;}
        sampledAt=-1;renderedAt=TimeSpan.MinValue;SetRendering(true);
    }
    private bool HoldForFrame()
    {
        if(!waitingForFrame())return false;
        if(!awaitingFrame)
        {
            awaitingFrame=true;visuals.Clear();active.Clear();deferred.Clear();hovered=null;
            lastPosition=-100;sampledAt=-1;lastClockPosition=double.NaN;timeline.Reset();coldCursor=true;currentMask=null;Clip=null;
        }
        // Keep shaped/rasterized glyphs, but never extrapolate decoder time or
        // publish newly fetched messages before the target frame is playable.
        SetRendering(false);return true;
    }
    private void PollPausedClock()
    {
        if(rendering||disposed||!enabled||!IsVisible)return;
        if(HoldForFrame())return;
        if(awaitingFrame){Wake();return;}
        var sample=clock();
        if(!sample.Paused||double.IsNaN(lastClockPosition)||Math.Abs(sample.Position-lastClockPosition)>.001||shaping.Values.Any(t=>t.IsCompleted)||next<messages.Count&&messages[next].Time<=sample.Position||settings.PersonMask&&!ReferenceEquals(Mask?.At(sample.Position),currentMask))Wake();
    }
    protected override int VisualChildrenCount=>visuals.Count;
    protected override Visual GetVisualChild(int index)=>visuals[index];
    public void SetMessages(IReadOnlyList<DanmakuItem> items){messages=items.OrderBy(m=>m.Time).ToArray();Reset();coldCursor=true;}
    public void ShowSent(DanmakuItem item)
    {
        if(disposed||!enabled)return;
        if(selfSent.Count>=32)selfSent.Remove(selfSent.First());selfSent.Add(item.Id);
        if(deferred.Count>=32)deferred.Dequeue();deferred.Enqueue(item with{Time=clock().Position});Wake();
    }
    // A segment window is already chronological. Keep active visuals and shaped
    // glyphs while releasing messages outside the current playback window.
    public void SetWindowMessages(IReadOnlyList<DanmakuItem> items)
    {
        messages=items;next=LowerBound(Math.Max(0,lastPosition));Wake();
    }
    public void AppendMessages(IReadOnlyList<DanmakuItem> items)
    {
        var upcoming=next<messages.Count?messages[next].Id:0;var known=messages.Select(m=>m.Id).ToHashSet();var additions=items.Where(m=>!known.Contains(m.Id)&&m.Time>=Math.Max(0,lastPosition)).Select(m=>m.Id).ToHashSet();
        messages=messages.Concat(items).DistinctBy(m=>m.Id).OrderBy(m=>m.Time).ToArray();var oldIndex=upcoming>0?messages.ToList().FindIndex(m=>m.Id==upcoming):messages.Count;var newIndex=messages.ToList().FindIndex(m=>additions.Contains(m.Id));next=Math.Min(oldIndex,newIndex<0?messages.Count:newIndex);
    }
    internal IReadOnlyList<long> ActiveIds=>active.Select(e=>e.Item.Id).ToArray();
    internal IReadOnlyList<double> ActiveOpacities=>active.Select(e=>e.Visual.Opacity).ToArray();
    public void SetEnabled(bool value){if(enabled==value)return;enabled=value;Visibility=value?Visibility.Visible:Visibility.Collapsed;visuals.Clear();active.Clear();deferred.Clear();hovered=null;next=LowerBound(Math.Max(0,clock().Position));UpdateVisibility();}
    public void SettingsChanged(){blockedWords=settings.BlockedWords.Split(['\n',',','，'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);filters=settings.SyncedFilters.ToArray();var patterns=new List<System.Text.RegularExpressions.Regex>();foreach(var filter in filters.Where(f=>f.Type==1))try{var expression=filter.Filter;var match=System.Text.RegularExpressions.Regex.Match(expression,@"^/(.*)/[img]*$");if(match.Success)expression=match.Groups[1].Value;patterns.Add(new(expression,System.Text.RegularExpressions.RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(3)));}catch(ArgumentException){}filterPatterns=patterns.ToArray();Wake();}
    public async Task PrimeAsync(double position,CancellationToken ct)
    {
        if(ActualWidth<=0)return;var scale=settings.ScaleWithFullscreen?Math.Clamp(ActualWidth/1280,.65,1.5):1;var dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var keys=messages.Where(m=>m.Time>=Math.Max(0,position-.1)&&m.Time<=position+2&&m.Mode!=7).Take(16).Select(m=>new GlyphKey(m.Text.Length>200?m.Text[..200]:m.Text,Math.Clamp(m.FontSize*settings.FontScale*scale,14,70),m.Color,settings.FontFamily,settings.Bold,settings.Outline,dpi)).Distinct().ToArray();
        foreach(var key in keys){ct.ThrowIfCancellationRequested();var shaped=await Shape(key).WaitAsync(ct);if(disposed)return;readyGlyphs[key]=shaped;}
    }
    private void Reset(){visuals.Clear();active.Clear();glyphs.Clear();readyGlyphs.Clear();shaping.Clear();deferred.Clear();selfSent.Clear();cacheBytes=0;hovered=null;lastPosition=-100;sampledAt=-1;lastClockPosition=double.NaN;timeline.Reset();renderedAt=TimeSpan.MinValue;next=0;currentMask=null;Clip=null;Wake();}
    public void HoverAt(Point? point,bool keep=false)
    {
        if(keep)return;
        var found=point is {} p?active.LastOrDefault(e=>new Rect(e.X,e.Y,e.Width,e.Height).Contains(p)):null;
        if(ReferenceEquals(found,hovered))return;
        if(hovered!=null&&hovered.HeldAt>=0){hovered.Start+=CurrentPosition()-hovered.HeldAt;hovered.HeldAt=-1;}
        hovered=found;if(found!=null)found.HeldAt=CurrentPosition();Wake();
    }
    public void ClearHover()=>HoverAt(null);
    public Point HoverLocation=>hovered==null?new Point():new Point(hovered.X,hovered.Y+hovered.Height);
    public bool IsOverHovered(Point point)=>hovered!=null&&new Rect(hovered.X,hovered.Y,hovered.Width,hovered.Height).Contains(point);
    private double CurrentPosition()=>timeline.Position;
    private void Frame(object? sender,EventArgs args)
    {
        if(disposed||!enabled||!IsVisible||ActualWidth<1||ActualHeight<1)return;
        if(HoldForFrame())return;
        if(args is RenderingEventArgs rendered){if(rendered.RenderingTime==renderedAt)return;renderedAt=rendered.RenderingTime;}
        var cpu=Stopwatch.StartNew();rasterized=0;
        // A lane can become occupied while a glyph is shaping. Completed jobs must
        // release their slot even when that message is subsequently filtered out.
        foreach(var finished in shaping.Where(pair=>pair.Value.IsCompleted).ToArray())
        {
            shaping.Remove(finished.Key);if(!finished.Value.IsCompletedSuccessfully)continue;
            if(readyGlyphs.Count>=64)readyGlyphs.Remove(readyGlyphs.First().Key);readyGlyphs[finished.Key]=finished.Value.Result;
        }
        var now=watch.Elapsed.TotalSeconds;
        if(now-sampledAt>.08||sampledAt<0){var sample=clock();sampledPaused=sample.Paused;sampledSpeed=sample.Speed;lastClockPosition=sample.Position;timeline.Synchronize(sample.Position,sampledPaused,sampledSpeed,now);sampledAt=now;}
        var time=timeline.Advance(now);
        var mask=settings.PersonMask?Mask?.At(time):null;if(!ReferenceEquals(mask,currentMask)){currentMask=mask;if(mask==null)Clip=null;else{var width=Math.Min(ActualWidth,ActualHeight*VideoAspect);var height=Math.Min(ActualHeight,ActualWidth/VideoAspect);var visible=new GeometryGroup{Children=new GeometryCollection{mask.Visible},Transform=new MatrixTransform(width/mask.Width,0,0,height/mask.Height,(ActualWidth-width)/2,(ActualHeight-height)/2)};visible.Freeze();Clip=visible;}}
        if(coldCursor){next=LowerBound(Math.Max(0,time-.1));lastPosition=time;coldCursor=false;}
        if(time<lastPosition-.25||time-lastPosition>1.5){visuals.Clear();active.Clear();deferred.Clear();hovered=null;next=LowerBound(Math.Max(0,time-14));}
        lastPosition=time;
        if(MeasureFrames){if(!sampledPaused&&lastFrame>0){if(frameGaps.Count>=1800)frameGaps.Dequeue();frameGaps.Enqueue((now-lastFrame)*1000);}lastFrame=now;frames++;}
        int pending=deferred.Count;for(int i=0;i<Math.Min(pending,16)&&cpu.Elapsed.TotalMilliseconds<2;i++){var item=deferred.Dequeue();if(item.Time+16>=time&&!Spawn(item,time))deferred.Enqueue(item);}
        int processed=0;while(next<messages.Count&&messages[next].Time<=time&&processed++<96&&cpu.Elapsed.TotalMilliseconds<2){var item=messages[next++];if(item.Time+16<time)continue;if(!Spawn(item,time)&&deferred.Count<32)deferred.Enqueue(item);}maxPending=Math.Max(maxPending,deferred.Count);
        for(int i=active.Count-1;i>=0;i--)
        {
            var entry=active[i];var elapsed=(entry.HeldAt>=0?entry.HeldAt:time)-entry.Start;
            if(elapsed>entry.Duration){visuals.Remove(entry.Visual);active.RemoveAt(i);continue;}
            double x,y=entry.Y;
            if(entry.Advanced){var t=Math.Clamp(elapsed/entry.Duration,0,1);x=entry.From.X+(entry.To.X-entry.From.X)*t;y=entry.From.Y+(entry.To.Y-entry.From.Y)*t;}
            else x=entry.Fixed?(ActualWidth-entry.Width)/2:entry.Item.Mode==6?-entry.Width+entry.Velocity*elapsed:ActualWidth-entry.Velocity*elapsed;
            entry.X=x;entry.Y=y;var matrix=Matrix.Identity;if(entry.Rotation!=0)matrix.Rotate(entry.Rotation);matrix.Translate(x,y);entry.Transform.Matrix=matrix;
        }
        if(MeasureFrames){maxActive=Math.Max(maxActive,active.Count);if(cpuTimes.Count>=1800)cpuTimes.Dequeue();cpuTimes.Enqueue(cpu.Elapsed.TotalMilliseconds);}
        if(sampledPaused)SetRendering(false);
    }
    private int LowerBound(double time){int low=0,high=messages.Count;while(low<high){int mid=(low+high)/2;if(messages[mid].Time<time)low=mid+1;else high=mid;}return low;}
    private bool Spawn(DanmakuItem item,double time)
    {
        if(active.Any(entry=>entry.Item.Id==item.Id))return true;
        var own=selfSent.Contains(item.Id);
        if(active.Count>=80)return !own;
        if(!own){
        if(item.Mode is 8 or 9||!settings.Scroll&&item.Mode is 1 or 6||!settings.Fixed&&item.Mode is 4 or 5||!settings.Advanced&&item.Mode==7||!settings.Colored&&item.Color!=0xFFFFFF)return true;
        if(blockedWords.Any(w=>item.Text.Contains(w,StringComparison.OrdinalIgnoreCase))||filters.Any(f=>f.Type==0&&item.Text.Contains(f.Filter,StringComparison.OrdinalIgnoreCase)||f.Type==2&&item.SenderHash==f.Filter))return true;
        foreach(var regex in filterPatterns)try{if(regex.IsMatch(item.Text))return true;}catch(System.Text.RegularExpressions.RegexMatchTimeoutException){}
        if(settings.SmartFilter&&active.Any(e=>e.Item.Text==item.Text&&time-e.Start<3))return true;}
        string text=item.Text;double duration=4,rotation=0,alpha=1;Point from=new(),to=new();bool advanced=item.Mode==7;
        if(advanced)
        {
            try
            {
                using var json=JsonDocument.Parse(item.Text);var a=json.RootElement;if(a.ValueKind!=JsonValueKind.Array||a.GetArrayLength()<5)return true;
                double N(int index,double fallback=0)=>index<a.GetArrayLength()&&double.TryParse(a[index].ToString(),NumberStyles.Any,CultureInfo.InvariantCulture,out var n)?n:fallback;
                double X(double n)=>n<=1?n*ActualWidth:n/672*ActualWidth;double Y(double n)=>n<=1?n*ActualHeight:n/438*ActualHeight;
                text=a[4].GetString()??"";duration=Math.Clamp(N(3,4),.5,15);from=new(X(N(0)),Y(N(1)));to=new(X(N(7,N(0))),Y(N(8,N(1))));rotation=N(5);var opacity=a[2].ToString().Split('-');if(double.TryParse(opacity[0],NumberStyles.Any,CultureInfo.InvariantCulture,out var value))alpha=Math.Clamp(value,0,1);
            }
            catch(Exception e) when(e is JsonException or InvalidOperationException){return true;}
        }
        if(text.Length>200)text=text[..200];text=text.Replace('\n',' ');
        var scale=settings.ScaleWithFullscreen?Math.Clamp(ActualWidth/1280,.65,1.5):1;var font=Math.Clamp(item.FontSize*settings.FontScale*scale,14,70);
        var lineHeight=font*1.35;var area=Math.Min(settings.ProtectSubtitles ? .8 : 1,Math.Clamp(settings.Area,.1,1));var laneCount=Math.Clamp((int)(ActualHeight*area/lineHeight),1,24);int lane=-1;var velocity=ActualWidth/6*settings.Speed*(settings.SyncPlaybackSpeed?1:1/Math.Max(.25,sampledSpeed));
        for(int i=0;i<laneCount;i++)
        {
            var y=item.Mode==4?ActualHeight*area-(i+1)*lineHeight:8+i*lineHeight;
            bool free=!settings.PreventOverlap||!active.Any(e=>Math.Abs(e.Y-y)<lineHeight*.85&&(item.Mode is 4 or 5||e.Fixed||e.HeldAt>=0||item.Mode==6||e.X+e.Width+25>ActualWidth-velocity*Math.Max(0,time-item.Time)));
            if(free){lane=i;break;}
        }
        if(lane<0&&!advanced){rejectedBeforeShaping++;return !own;}
        var key=new GlyphKey(text,font,item.Color,settings.FontFamily,settings.Bold,settings.Outline,VisualTreeHelper.GetDpi(this).PixelsPerDip);
        if(!glyphs.TryGetValue(key,out var glyph))
        {
            if(!readyGlyphs.TryGetValue(key,out var ready))
            {
                if(!shaping.ContainsKey(key)){if(shaping.Count>=8)return false;shaping[key]=Shape(key);}return false;
            }
            if(rasterized>=2)return false;readyGlyphs.Remove(key);
            glyph=Rasterize(ready,key.Dpi);rasterized++;shapesBuilt++;long bytes=(long)(glyph.Width*glyph.Height*key.Dpi*key.Dpi*4);while(glyphs.Count>0&&(cacheBytes+bytes>32*1048576||glyphs.Count>=256)){var old=glyphs.First();cacheBytes-=(long)(old.Value.Width*old.Value.Height*old.Key.Dpi*old.Key.Dpi*4);glyphs.Remove(old.Key);}glyphs[key]=glyph;cacheBytes+=bytes;
        }
        var visual=new DrawingVisual{Opacity=Math.Clamp(settings.Opacity,.05,1)*(advanced?alpha:1)};using(var drawing=visual.RenderOpen())drawing.DrawDrawing(glyph.Drawing);var transform=new MatrixTransform();visual.Transform=transform;visuals.Add(visual);
        var entry=new Entry(item,visual,transform,glyph.Width,glyph.Height){Fixed=item.Mode is 4 or 5,Advanced=advanced,From=from,To=to,Rotation=rotation,AdvancedOpacity=alpha,Duration=advanced?duration:item.Mode is 4 or 5?4:(ActualWidth+glyph.Width)/velocity,Velocity=velocity,X=ActualWidth-velocity*(time-item.Time),Y=item.Mode==4?ActualHeight*area-(lane+1)*lineHeight:8+lane*lineHeight};active.Add(entry);
        if(own&&!entry.Fixed)entry.Start=time-Math.Min(2,(entry.Width+16)/velocity);
        return true;
    }
    private static Task<Glyph> Shape(GlyphKey key)
    {
        var completion=new TaskCompletionSource<Glyph>(TaskCreationOptions.RunContinuationsAsynchronously);_=shapeWorker.Value;
        if(!shapeJobs.TryAdd((key,completion)))completion.SetException(new InvalidOperationException("Glyph queue is full"));return completion.Task;
    }
    private static Thread StartShapeWorker()
    {
        var thread=new Thread(()=>{foreach(var job in shapeJobs.GetConsumingEnumerable())try{var key=job.Key;var formatted=new FormattedText(key.Text,CultureInfo.GetCultureInfo("zh-CN"),FlowDirection.LeftToRight,new Typeface(new FontFamily(key.Family),FontStyles.Normal,key.Bold?FontWeights.SemiBold:FontWeights.Normal,FontStretches.Normal),key.Font,Brushes.White,key.Dpi);var geometry=formatted.BuildGeometry(new Point(2,2));var group=new DrawingGroup();var color=new SolidColorBrush(Color.FromRgb((byte)(key.Color>>16),(byte)(key.Color>>8),(byte)key.Color));color.Freeze();var pen=key.Outline?new Pen(new SolidColorBrush(Color.FromArgb(190,0,0,0)),1.3):null;pen?.Freeze();using(var drawing=group.Open())drawing.DrawGeometry(color,pen,geometry);group.Freeze();job.Completion.SetResult(new(group,formatted.WidthIncludingTrailingWhitespace+5,formatted.Height+5));}catch(Exception e){job.Completion.SetException(e);}}){IsBackground=true,Name="VideoDesk glyph shaping"};thread.SetApartmentState(ApartmentState.STA);thread.Start();return thread;
    }
    private static Glyph Rasterize(Glyph glyph,double dpi)
    {
        var visual=new DrawingVisual();using(var context=visual.RenderOpen())context.DrawDrawing(glyph.Drawing);var bitmap=new RenderTargetBitmap(Math.Clamp((int)Math.Ceiling(glyph.Width*dpi),1,8192),Math.Clamp((int)Math.Ceiling(glyph.Height*dpi),1,256),96*dpi,96*dpi,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();var group=new DrawingGroup();using(var context=group.Open())context.DrawImage(bitmap,new Rect(0,0,glyph.Width,glyph.Height));group.Freeze();return new(group,glyph.Width,glyph.Height);
    }
    public void Dispose(){disposed=true;clockTimer.Stop();SetRendering(false);Mask?.Dispose();visuals.Clear();active.Clear();glyphs.Clear();readyGlyphs.Clear();shaping.Clear();deferred.Clear();messages=[];}
}


