using System.Security.Cryptography;
using System.Text.Json;

namespace VideoDesk.Core;

public sealed class LocalStore(string directory)
{
    private readonly string directory = Directory.CreateDirectory(directory).FullName;
    private readonly Dictionary<string,WatchProgress> progress = new();
    private readonly object progressGate=new();
    private WatchProgress[]? pendingProgress;
    private Task progressWriter=Task.CompletedTask;
    private bool writingProgress;
    private Exception? progressError;
    public event Action<Exception>? ProgressWriteFailed;
    public void SaveSession(string cookies) => File.WriteAllBytes(Path.Combine(directory,"session.dpapi"),ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(cookies),null,DataProtectionScope.CurrentUser));
    public string? LoadSession() { try { var p=Path.Combine(directory,"session.dpapi"); return File.Exists(p)?System.Text.Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(p),null,DataProtectionScope.CurrentUser)):null; } catch(CryptographicException){return null;} }
    public string? LoadDeviceCookies(bool account)
    {
        try{var p=Path.Combine(directory,account?"account-device.dpapi":"guest-device.dpapi");return File.Exists(p)?System.Text.Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(p),null,DataProtectionScope.CurrentUser)):null;}catch(CryptographicException){return null;}
    }
    public void SaveDeviceCookies(bool account,string value)
    {
        var path=Path.Combine(directory,account?"account-device.dpapi":"guest-device.dpapi");WriteAtomic(path,ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(value),null,DataProtectionScope.CurrentUser));
    }
    public void LoadProgress() { try { var p=Path.Combine(directory,"progress.json"); if(File.Exists(p)) foreach(var w in JsonSerializer.Deserialize<List<WatchProgress>>(File.ReadAllText(p))??[]) progress[w.Bvid]=w; } catch(JsonException){} }
    public WatchProgress? GetProgress(string bvid) {lock(progressGate)return progress.GetValueOrDefault(bvid);}
    public IReadOnlyList<WatchProgress> SnapshotHistory(){lock(progressGate)return progress.Values.ToArray();}
    public IReadOnlyList<WatchProgress> History => SnapshotHistory().OrderByDescending(p=>p.WatchedAt).ToArray();
    public void SaveProgress(WatchProgress value) {lock(progressGate){progress[value.Bvid]=value;QueueProgress();}}
    public void RemoveProgress(string bvid){lock(progressGate){progress.Remove(bvid);QueueProgress();}}
    public void ClearProgress(){lock(progressGate){progress.Clear();QueueProgress();}}
    private void QueueProgress()
    {
        pendingProgress=progress.Values.ToArray();
        if(!writingProgress)StartProgressWriter();
    }
    private void StartProgressWriter(){writingProgress=true;progressError=null;progressWriter=Task.Run(WriteProgressLoop);}
    private async Task WriteProgressLoop()
    {
        while(true)
        {
            WatchProgress[] snapshot;
            lock(progressGate){if(pendingProgress==null){writingProgress=false;return;}snapshot=pendingProgress;pendingProgress=null;}
            // Coalesce rapid progress/delete events without holding UI objects or
            // a mutable dictionary on the worker. All writes remain sequential.
            await Task.Delay(40).ConfigureAwait(false);
            lock(progressGate){if(pendingProgress!=null){snapshot=pendingProgress;pendingProgress=null;}}
            try
            {
                var contents=JsonSerializer.SerializeToUtf8Bytes(snapshot);
                var path=Path.Combine(directory,"progress.json");var temporary=path+"."+Environment.ProcessId+"."+Guid.NewGuid().ToString("N")+".tmp";
                try{await File.WriteAllBytesAsync(temporary,contents).ConfigureAwait(false);File.Move(temporary,path,true);}
                finally{if(File.Exists(temporary))File.Delete(temporary);}
            }
            catch(Exception error)
            {
                lock(progressGate){pendingProgress??=snapshot;progressError=error;writingProgress=false;}
                try{ProgressWriteFailed?.Invoke(error);}catch{/* Reporting must not fault an unobserved worker. */}
                return;
            }
        }
    }
    public async Task FlushProgressAsync()
    {
        while(true)
        {
            Task current;
            lock(progressGate){if(!writingProgress&&pendingProgress!=null)StartProgressWriter();current=progressWriter;}
            await current.ConfigureAwait(false);
            lock(progressGate){if(progressError!=null)throw new IOException("本地观看进度保存失败",progressError);if(!writingProgress&&pendingProgress==null)return;}
        }
    }
    private static void WriteAtomic(string path,byte[] contents)
    {
        var temporary=path+"."+Environment.ProcessId+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllBytes(temporary,contents);File.Move(temporary,path,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
