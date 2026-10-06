// Copyright (c) NeeLaboratory. MIT; original ImageEffectConfig and layers without WPF.
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using NeeView.Effects;
namespace NeeView;
/// <summary>原总开关及外到内层列表；未识别的未来设置在唯一 JSON 中保留。</summary>
public sealed class ImageEffectConfig : ObservableObject
{
    private bool _enabled;
    private EffectLayerCollection _layers = new() { new() };
    public bool IsEnabled { get => _enabled; set => SetProperty(ref _enabled, value); }
    public EffectLayerCollection Layers { get => _layers; set => SetProperty(ref _layers, value); }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
/// <summary>原层切换：缓存旧类型；已有同类型层时新建默认参数，避免共享可变参数。</summary>
public sealed class EffectLayer : ObservableObject
{
    private bool _enabled = true;
    private EffectUnit? _effect;
    public bool IsEnabled { get => _enabled; set => SetProperty(ref _enabled, value); }
    public EffectUnit? Effect { get => _effect; set { if (SetProperty(ref _effect, value)) OnPropertyChanged(nameof(EffectType)); } }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    [JsonIgnore] public EffectType EffectType
    {
        get => Effect is null || Effect is UnknownEffectUnit ? EffectType.None : Enum.Parse<EffectType>(Effect.GetType().Name[..^"EffectUnit".Length]);
    }
    /// <summary>显式上下文供草稿编辑，不能把草稿参数提前写入运行缓存。</summary>
    public void ChangeType(EffectType type, ImageEffectConfig config, EffectUnitCache cache)
    {
        if (EffectType == type && Effect is not UnknownEffectUnit) return;
        cache.Add(Effect);
        Effect = type == EffectType.None ? null : config.Layers.Any(x => x != this && x.EffectType == type)
            ? EffectUnit.CreateInstance(JsonEffectUnitConverter.Types[type.ToString()]) : cache.Get(JsonEffectUnitConverter.Types[type.ToString()]);
        IsEnabled = true;
    }
    public void Reset() { if (Effect is not null && Effect is not UnknownEffectUnit) Effect = EffectUnit.CreateInstance(Effect.GetType()); }
}
/// <summary>原最多十层、新层插在外侧，删除仍保留一层。</summary>
public sealed class EffectLayerCollection : ObservableCollection<EffectLayer>
{
    public const int MaxLayerCount = 10;
    public bool CanCreateNew() => Count < MaxLayerCount;
    public bool CanMoveUp(EffectLayer layer) => IndexOf(layer) > 0;
    public bool CanMoveDown(EffectLayer layer) => Contains(layer) && IndexOf(layer) < Count - 1;
    public bool CanDelete(EffectLayer layer) => Contains(layer) && Count > 1;
    public EffectLayer? CreateNew() { if (!CanCreateNew()) return null; var layer = new EffectLayer(); Insert(0, layer); return layer; }
    public void MoveUp(EffectLayer layer) { if (CanMoveUp(layer)) Move(IndexOf(layer), IndexOf(layer) - 1); }
    public void MoveDown(EffectLayer layer) { if (CanMoveDown(layer)) Move(IndexOf(layer), IndexOf(layer) + 1); }
    public void Delete(EffectLayer layer, EffectUnitCache cache) { if (CanDelete(layer)) { Remove(layer); cache.Add(layer.Effect); } }
}
