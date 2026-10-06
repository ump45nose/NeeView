// Copyright (c) NeeLaboratory. MIT; original six-branch EffectProfile, no display resources.
using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
namespace NeeView;
/// <summary>原六分支预设；配置克隆仅在切换/编辑/保存时发生，不进入绘制热路径。</summary>
public sealed class EffectProfile : ObservableObject, IComparable<EffectProfile>
{
    public int Id { get; set; }
    private string _name = "";
    public string Name { get => _name; set { if (SetProperty(ref _name, value)) OnPropertyChanged(nameof(DisplayName)); } }
    [JsonIgnore] public string DisplayName => string.IsNullOrEmpty(Name) ? "默认" : Name;
    public ImageCustomSizeConfig ImageCustomSize { get; set; } = new();
    public ImageTrimConfig ImageTrim { get; set; } = new();
    public ImageDotKeepConfig ImageDotKeep { get; set; } = new();
    public ImageResizeFilterConfig ImageResizeFilter { get; set; } = new();
    public ImageGridConfig ImageGrid { get; set; } = new();
    public ImageEffectConfig ImageEffect { get; set; } = new();
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    internal static T Copy<T>(T source) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(source))!;
    /// <summary>先保存当前原六分支，未知效果参数和暂未迁的滤镜材料同时保留。</summary>
    public void Store(Config config)
    {
        ImageCustomSize = Copy(config.ImageCustomSize); ImageTrim = Copy(config.ImageTrim); ImageDotKeep = Copy(config.ImageDotKeep);
        ImageResizeFilter = Copy(config.ImageResizeFilter); ImageGrid = Copy(config.ImageGrid); ImageEffect = Copy(config.ImageEffect);
    }
    /// <summary>恢复六分支；页框读取 Config 的当前对象，不持有过期分支引用。</summary>
    public void Restore(Config config)
    {
        config.ImageCustomSize = Copy(ImageCustomSize); config.ImageTrim = Copy(ImageTrim); config.ImageDotKeep = Copy(ImageDotKeep);
        config.ImageResizeFilter = Copy(ImageResizeFilter); config.ImageGrid = Copy(ImageGrid); config.ImageEffect = Copy(ImageEffect);
    }
    public int CompareTo(EffectProfile? other) => other is null ? 1 : other.Id == 0 ? Id == 0 ? 0 : 1 : Id == 0 ? -1 : string.Compare(Name, other.Name, StringComparison.CurrentCulture);
}
