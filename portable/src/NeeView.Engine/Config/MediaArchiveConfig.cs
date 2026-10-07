// Copyright (c) NeeView. Original MediaArchiveConfig fields and defaults, MIT license.
using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace NeeView;
/// <summary>原视频配置；LibVLC字段保持数据兼容，其Windows实现不进入Mac构建。</summary>
public sealed class MediaArchiveConfig : ObservableObject
{
    public static FileTypeCollection DefaultSupportFileTypes { get; } = new(".asf;.avi;.mp4;.mkv;.mov;.wmv");
    private bool _enabled=true,_pages,_muted,_repeat,_libVlc;
    private double _seconds=10,_delay=.5,_volume=.5;
    private FileTypeCollection _types=(FileTypeCollection)DefaultSupportFileTypes.Clone();
    public bool IsEnabled {get=>_enabled;set=>SetProperty(ref _enabled,value);}
    public bool IsMediaPageEnabled {get=>_pages;set=>SetProperty(ref _pages,value);}
    public FileTypeCollection SupportFileTypes {get=>_types;set=>SetProperty(ref _types,value??new());}
    public double PageSeconds {get=>_seconds;set=>SetProperty(ref _seconds,Round(value,10));}
    public double MediaStartDelaySeconds {get=>_delay;set=>SetProperty(ref _delay,Round(value,.5));}
    public bool IsMuted {get=>_muted;set=>SetProperty(ref _muted,value);}
    public double Volume {get=>_volume;set=>SetProperty(ref _volume,Round(value,.5));}
    public bool IsRepeat {get=>_repeat;set=>SetProperty(ref _repeat,value);}
    public bool IsLibVlcEnabled {get=>_libVlc;set=>SetProperty(ref _libVlc,value);}
    public string? LibVlcPath {get;set;}
    public DefaultSubtitle DefaultSubtitle {get;set;}
    [JsonExtensionData] public Dictionary<string,JsonElement>? ExtensionData {get;set;}
    private static double Round(double value,double fallback)=>double.IsFinite(value)?Math.Round(value,5):fallback;
}
/// <summary>原字幕默认值序号；系统后端字幕选择另有明确能力边界。</summary>
public enum DefaultSubtitle {Default,Disable}
/// <summary>统一原媒体扩展名资格，压缩/列表/PDF后端保持各自优先顺序。</summary>
public static class MediaFormats
{
    public static bool IsSupported(string path)=>Config.Current.Archive.Media.SupportFileTypes.Contains(System.IO.Path.GetExtension(path));
    public static bool IsBook(string path)=>Config.Current.Archive.Media.IsEnabled&&IsSupported(path);
    public static bool IsPage(string path)=>Config.Current.Archive.Media.IsMediaPageEnabled&&IsSupported(path);
    /// <summary>原DirtyBook字段；音量/循环/延迟不重新枚举来源。</summary>
    public static bool IndexChanged(MediaArchiveConfig before,MediaArchiveConfig after)=>before.IsEnabled!=after.IsEnabled
        ||before.IsMediaPageEnabled!=after.IsMediaPageEnabled||before.SupportFileTypes.ToString()!=after.SupportFileTypes.ToString();
}
