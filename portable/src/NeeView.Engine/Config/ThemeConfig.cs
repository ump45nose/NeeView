// Copyright (c) NeeLaboratory. 原 ThemeConfig，独立 Mac Profile 默认目录替换。
using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;
namespace NeeView;

/// <summary>保留原主题、目录及旧 PanelColor 后备；运行 JSON 仍由 SaveData 权威保存。</summary>
public sealed class ThemeConfig : ObservableObject
{
    private ThemeSource _themeType = new(NeeView.ThemeType.Dark);
    private string? _customThemeFolder;
    [JsonIgnore] internal string DefaultFolder { get; set; } = "";
    [PropertyMapIgnore] public ThemeSource ThemeType
    {
        get => _themeType;
        set { if (SetProperty(ref _themeType, value)) OnPropertyChanged(nameof(ThemeString)); }
    }
    [JsonIgnore, PropertyMapName("ThemeType")] public string ThemeString { get => ThemeType.ToString(); set => ThemeType = ThemeSource.Parse(value); }
    /// <summary>原空白/default 目录归一化；独立 Profile 落点只由启动加载注入。</summary>
    [JsonIgnore] public string CustomThemeFolder
    {
        get => _customThemeFolder ?? DefaultFolder;
        set => SetProperty(ref _customThemeFolder, string.IsNullOrWhiteSpace(value) || value.Trim() == DefaultFolder ? null : value.Trim());
    }
    [JsonPropertyName("CustomThemeFolder"), PropertyMapIgnore] public string? CustomThemeFolderRaw { get => _customThemeFolder; set => _customThemeFolder = value; }
    /// <summary>原 39 之前读取后备；现代字段优先，正常保存移除旧键。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] [PropertyMapIgnore] public string? PanelColor
    { get => null; set => ThemeType = new(value == "Light" ? NeeView.ThemeType.Light : NeeView.ThemeType.Dark); }
}
