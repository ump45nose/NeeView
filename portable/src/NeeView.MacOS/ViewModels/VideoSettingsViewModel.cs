using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
namespace NeeView.MacOS.ViewModels;
/// <summary>原视频设置的隔离草稿；表单不拥有播放器、路径枚举或保存逻辑。</summary>
public sealed class VideoSettingsViewModel : ObservableObject
{
    private bool _enabled,_pages,_muted,_repeat;
    private string _types;
    private decimal _delay,_volume;
    private readonly double _originalDelay,_originalVolume;
    private readonly decimal _initialDelay,_initialVolume;
    public bool IsEnabled {get=>_enabled;set=>SetProperty(ref _enabled,value);}
    public bool IsMediaPageEnabled {get=>_pages;set=>SetProperty(ref _pages,value);}
    public bool IsMuted {get=>_muted;set=>SetProperty(ref _muted,value);}
    public bool IsRepeat {get=>_repeat;set=>SetProperty(ref _repeat,value);}
    public string SupportFileTypes {get=>_types;set=>SetProperty(ref _types,value??"");}
    public decimal Delay {get=>_delay;set=>SetProperty(ref _delay,value);}
    public decimal Volume {get=>_volume;set=>SetProperty(ref _volume,value);}
    public decimal DelayMinimum {get;}
    public decimal DelayMaximum {get;}
    public decimal VolumeMinimum {get;}
    public decimal VolumeMaximum {get;}
    public bool LegacyVlc {get;}
    public VideoSettingsViewModel(MediaArchiveConfig config)
    {
        _enabled=config.IsEnabled;_pages=config.IsMediaPageEnabled;_muted=config.IsMuted;_repeat=config.IsRepeat;_types=config.SupportFileTypes.ToString();
        _originalDelay=config.MediaStartDelaySeconds;_originalVolume=config.Volume;
        _delay=_initialDelay=DisplayValue(_originalDelay);_volume=_initialVolume=DisplayValue(_originalVolume);
        DelayMinimum=Math.Min(0,_delay);DelayMaximum=Math.Max(3600,_delay);VolumeMinimum=Math.Min(0,_volume);VolumeMaximum=Math.Max(1,_volume);LegacyVlc=config.IsLibVlcEnabled;
    }
    /// <summary>进入现有设置候选事务；未展示Windows字段及未来字段保持。</summary>
    public void Apply(MediaArchiveConfig config)
    {config.IsEnabled=IsEnabled;config.IsMediaPageEnabled=IsMediaPageEnabled;config.IsMuted=IsMuted;config.IsRepeat=IsRepeat;config.SupportFileTypes=new(SupportFileTypes);config.MediaStartDelaySeconds=Delay==_initialDelay?_originalDelay:(double)Delay;config.Volume=Volume==_initialVolume?_originalVolume:(double)Volume;}
    /// <summary>旧double可能超出表单decimal范围；显示受限，未编辑值仍原样保留。</summary>
    private static decimal DisplayValue(double value)
    {
        if(double.IsNaN(value))return 0;
        if(value>=(double)decimal.MaxValue)return decimal.MaxValue;
        if(value<=(double)decimal.MinValue)return decimal.MinValue;
        return (decimal)value;
    }
}
