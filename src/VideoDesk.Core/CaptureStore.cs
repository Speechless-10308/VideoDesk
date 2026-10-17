using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace VideoDesk.Core;

public sealed class CaptureStore : IAsyncDisposable
{
    private sealed record CaptureRecord(DateTimeOffset CapturedAt,string Label,string Identity,string Method,string Url,Dictionary<string,string> Headers,string? RequestBody,int Status,string? StatusText,Dictionary<string,string> ResponseHeaders,string ResponseText,long Elapsed,long Bytes);
    private const long QueueBudget=32*1048576L;
    private readonly object queueGate=new();
    private readonly Channel<CaptureRecord>? queue;
    private readonly Task worker=Task.CompletedTask;
    private volatile bool enabled;
    private bool disposed;
    private long queuedBytes,written,skipped,failed;
    private int pending;
    private string? lastError;
    private TaskCompletionSource idle=CompletedIdle();
    public CaptureStore(string root,bool enabled=true,bool background=false)
    {
        Root=Path.GetFullPath(root);this.enabled=enabled;
        if(background)
        {
            queue=Channel.CreateBounded<CaptureRecord>(new BoundedChannelOptions(32){SingleReader=true,FullMode=BoundedChannelFullMode.Wait});
            worker=Task.Run(DrainAsync);
        }
    }
    private static TaskCompletionSource CompletedIdle(){var result=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);result.SetResult();return result;}
    public bool Enabled {get=>enabled;set=>enabled=value&&!disposed;}
    public object Diagnostics {get{lock(queueGate)return new{enabled,background=queue!=null,pending,queuedBytes,maxQueuedBytes=QueueBudget,written,skipped,failed,lastError};}}
    public (int Pending,long Written,long Skipped,long Failed,string? Error) Status {get{lock(queueGate)return(pending,Interlocked.Read(ref written),skipped,failed,lastError);}}
    private readonly SemaphoreSlim gate = new(1,1);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly HashSet<string> SecretNames = new(StringComparer.OrdinalIgnoreCase) { "Cookie", "Set-Cookie", "Authorization", "SESSDATA", "bili_jct", "DedeUserID", "DedeUserID__ckMd5", "refresh_token", "qrcode_key", "csrf", "csrf_token", "access_key", "token", "b_3", "b_4", "buvid", "buvid3", "buvid4", "b_wet", "b_ret", "bili_ticket", "gaia_vtoken", "x-bili-gaia-vtoken", "x-bili-gaia-provider", "x-bili-gaia-token", "x-bili-metadata-gaia-bin", "x-bili-mid", "x-bili-aurora-eid" };
    public string Root { get; }
    public static string RedactUrl(string value) => Regex.Replace(value, @"(?i)([?&](?:SESSDATA|bili_jct|DedeUserID|refresh_token|qrcode_key|csrf|access_key|token|deadline|wsSecret|wsTime|sign|upsig|uparams|trid|mid|e|oi|uipk|buvid|buvid3|buvid4|b_wet|b_ret|bili_ticket|gaia_vtoken|x-bili-gaia-token)=)[^&#\s""'<>\\]*", "$1[REDACTED]");
    public static JsonNode? Sanitize(JsonNode? node, string name = "")
    {
        if (SecretNames.Contains(name)) return JsonValue.Create("[REDACTED]");
        if (node is JsonObject obj) { var result = new JsonObject(); foreach (var pair in obj) result[pair.Key] = Sanitize(pair.Value, pair.Key); return result; }
        if (node is JsonArray arr) return new JsonArray(arr.Select(n => Sanitize(n)).ToArray());
        if (node is JsonValue val && val.TryGetValue<string>(out var s)) return JsonValue.Create(RedactUrl(s));
        return node?.DeepClone();
    }
    // Copy HTTP metadata before returning: callers may immediately dispose both
    // messages. Only immutable strings and private dictionaries reach the worker.
    public Task RecordAsync(string label, string identity, HttpRequestMessage request, HttpResponseMessage response, string responseText, long elapsed, string? requestBody = null, string? cookies = null)
    {
        if(!Enabled)return Task.CompletedTask;
        CaptureRecord record;
        try
        {
            var headers=request.Headers.Concat(request.Content?.Headers??Enumerable.Empty<KeyValuePair<string,IEnumerable<string>>>()).ToDictionary(h=>h.Key,h=>string.Join("; ",h.Value));
            if(!string.IsNullOrEmpty(cookies))headers["Cookie"]=cookies;
            var responseHeaders=response.Headers.Concat(response.Content.Headers).ToDictionary(h=>h.Key,h=>string.Join("; ",h.Value));
            var bytes=2L*(responseText.Length+(requestBody?.Length??0)+headers.Sum(h=>h.Key.Length+h.Value.Length)+responseHeaders.Sum(h=>h.Key.Length+h.Value.Length))+512;
            record=new(DateTimeOffset.UtcNow,label,identity,request.Method.Method,request.RequestUri!.ToString(),headers,requestBody,(int)response.StatusCode,response.ReasonPhrase,responseHeaders,responseText,elapsed,bytes);
        }
        catch(Exception error) when(queue!=null){NoteFailure(error);return Task.CompletedTask;}
        if(queue==null)return Task.Run(()=>RecordCoreAsync(record));
        lock(queueGate)
        {
            if(disposed||pending>=32||record.Bytes>QueueBudget-queuedBytes){skipped++;return Task.CompletedTask;}
            if(pending==0)idle=new(TaskCreationOptions.RunContinuationsAsynchronously);
            pending++;queuedBytes+=record.Bytes;
            if(!queue.Writer.TryWrite(record)){pending--;queuedBytes-=record.Bytes;skipped++;if(pending==0)idle.TrySetResult();}
        }
        return Task.CompletedTask;
    }
    private void NoteFailure(Exception error){lock(queueGate){failed++;lastError=RedactUrl(error.Message);}}
    private async Task DrainAsync()
    {
        await foreach(var record in queue!.Reader.ReadAllAsync())
        {
            try{await RecordCoreAsync(record).ConfigureAwait(false);Interlocked.Increment(ref written);}
            catch(Exception error){NoteFailure(error);}
            finally{lock(queueGate){pending--;queuedBytes-=record.Bytes;if(pending==0)idle.TrySetResult();}}
        }
    }
    public Task FlushAsync(CancellationToken ct=default){lock(queueGate)return idle.Task.WaitAsync(ct);}
    public async ValueTask DisposeAsync()
    {
        lock(queueGate){disposed=true;enabled=false;queue?.Writer.TryComplete();}
        try{await worker.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);}catch(TimeoutException){}
    }
    private async Task RecordCoreAsync(CaptureRecord record)
    {
        await gate.WaitAsync();
        try
        {
            var now = record.CapturedAt;
            var label=record.Label;var identity=record.Identity;var requestBody=record.RequestBody;var responseText=record.ResponseText;var elapsed=record.Elapsed;
            var local = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(now, "China Standard Time");
            var folder = Path.Combine(Root, local.ToString("yyyy-MM-dd"), local.ToString("HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..6] + "-" + label);
            Directory.CreateDirectory(folder);
            var headers=record.Headers;var responseHeaders=record.ResponseHeaders;
            var original = new { capturedAt=now,identity,label,method=record.Method,url=record.Url,headers,requestBody,status=record.Status,responseHeaders,responseBody=responseText,elapsedMs=elapsed };
            await File.WriteAllBytesAsync(Path.Combine(folder,"original.dpapi"), ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(original),null,DataProtectionScope.CurrentUser));
            var safeHeaders = headers.ToDictionary(h=>h.Key,h=>SecretNames.Contains(h.Key)?"[REDACTED]":h.Value);
            var safeResponseHeaders = responseHeaders.ToDictionary(h=>h.Key,h=>SecretNames.Contains(h.Key)?"[REDACTED]":h.Value);
            string safeBody;
            try { safeBody=Sanitize(JsonNode.Parse(responseText))?.ToJsonString(Json)??"null"; }
            catch (JsonException) { safeBody=RedactUrl(responseText); }
            var safeRequest = new {capturedAt=now,identity,label,method=record.Method,url=RedactUrl(record.Url),headers=safeHeaders,requestBody=requestBody is null?null:"[REDACTED]",status=record.Status,elapsedMs=elapsed,responseHeaders=safeResponseHeaders};
            await File.WriteAllTextAsync(Path.Combine(folder,"request.json"),JsonSerializer.Serialize(safeRequest,Json));
            await File.WriteAllTextAsync(Path.Combine(folder,"response.json"),safeBody);
            await File.AppendAllTextAsync(Path.Combine(Root,"index.jsonl"),JsonSerializer.Serialize(new {capturedAt=now,label,identity,status=record.Status,elapsedMs=elapsed,path=Path.GetRelativePath(Root,folder)})+Environment.NewLine);
            var har = new { log = new { version="1.2",creator=new {name="VideoDesk HTTP capture",version="0.1"},entries=new[]{new {startedDateTime=now,time=elapsed,request=new {method=record.Method,url=RedactUrl(record.Url),httpVersion="HTTP/1.1",cookies=Array.Empty<object>(),headers=safeHeaders.Select(h=>new{name=h.Key,value=h.Value}),queryString=Array.Empty<object>(),headersSize=-1,bodySize=-1},response=new{status=record.Status,statusText=record.StatusText,httpVersion="HTTP/1.1",cookies=Array.Empty<object>(),headers=safeResponseHeaders.Select(h=>new{name=h.Key,value=h.Value}),content=new{size=Encoding.UTF8.GetByteCount(safeBody),mimeType="application/json",text=safeBody},redirectURL="",headersSize=-1,bodySize=Encoding.UTF8.GetByteCount(safeBody)},cache=new{},timings=new{send=0,wait=elapsed,receive=0}}}}};
            await File.WriteAllTextAsync(Path.Combine(folder,"capture.har"),JsonSerializer.Serialize(har,Json));
        }
        finally { gate.Release(); }
    }
}
