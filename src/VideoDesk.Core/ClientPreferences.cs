using System.Text.Json;

namespace VideoDesk.Core;

public sealed class ClientPreferences
{
    public string Theme {get;set;}="Light";
    public long ImageCacheMegabytes {get;set;}=192;
    public bool HardwareDecoding {get;set;}=true;
    public double InterfaceScale {get;set;}=1;
    public int DefaultQuality {get;set;}=80;
    public DiscoveryMode HomeMode {get;set;}=DiscoveryMode.Regular;
    public DiscoveryMode SearchMode {get;set;}=DiscoveryMode.Roaming;
    public DiscoveryMode RelatedMode {get;set;}=DiscoveryMode.Regular;
    public DanmakuPreferences Danmaku {get;set;}=new();
    public static ClientPreferences Load(string directory)
    {
        try{return JsonSerializer.Deserialize<ClientPreferences>(File.ReadAllText(Path.Combine(directory,"preferences.json")))??new();}catch(Exception e) when(e is IOException or JsonException){return new();}
    }
    public void Save(string directory){Directory.CreateDirectory(directory);var path=Path.Combine(directory,"preferences.json");File.WriteAllText(path+".tmp",JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));File.Move(path+".tmp",path,true);}
}
public sealed class DanmakuPreferences
{
    public double Opacity {get;set;}=.75;
    public double Area {get;set;}=.5;
    public double FontScale {get;set;}=1;
    public double Speed {get;set;}=1;
    public bool Scroll {get;set;}=true;
    public bool Fixed {get;set;}=true;
    public bool Colored {get;set;}=true;
    public bool Advanced {get;set;}=true;
    public bool PreventOverlap {get;set;}=true;
    public bool SmartFilter {get;set;}=true;
    public bool PersonMask {get;set;}=true;
    public List<DanmakuFilter> SyncedFilters {get;set;}=[];
    public int SendMode {get;set;}=1;
    public int SendFontSize {get;set;}=25;
    public uint SendColor {get;set;}=0xFFFFFF;
    public bool ScaleWithFullscreen {get;set;}=true;
    public bool SyncPlaybackSpeed {get;set;}=true;
    public string FontFamily {get;set;}="Microsoft YaHei UI";
    public bool Bold {get;set;}=true;
    public bool Outline {get;set;}=true;
    public bool ProtectSubtitles {get;set;}=true;
    public string BlockedWords {get;set;}="";
}
