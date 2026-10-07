// Copyright (c) NeeLaboratory. 原 FontsConfig；只替换 WPF 系统字体来源和属性编辑元数据。
using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;
namespace NeeView;

/// <summary>原六项字体配置及默认比例；平台默认字体由启动装配注入，不写入 Profile。</summary>
public sealed class FontsConfig : ObservableObject
{
    private double _fontScale = 1.25, _menuFontScale = 1, _folderTreeFontScale = 1, _panelFontScale = 1.25;
    private bool _isClearTypeEnabled = true;
    private string? _fontName;
    /// <summary>替换 SystemVisualParameters.MessageFontName 的实际平台值。</summary>
    [JsonIgnore] [PropertyMapIgnore] public string DefaultFontName { get; set; } = "";
    /// <summary>原空白/系统默认字体写回 null；缺失的显式字体仍保留原名称。</summary>
    [JsonIgnore] public string FontName
    {
        get => _fontName ?? DefaultFontName;
        set => SetProperty(ref _fontName, string.IsNullOrWhiteSpace(value) || value == DefaultFontName ? null : value);
    }
    [JsonPropertyName("FontName")] [PropertyMapIgnore] public string? FontNameRaw { get => _fontName; set => _fontName = value; }
    /// <summary>原默认1.25，非正值读取默认；保留原五位舍入，不截断有效导入比例。</summary>
    public double FontScale { get => _fontScale <= 0 ? 1.25 : _fontScale; set => SetProperty(ref _fontScale, Math.Round(value, 5)); }
    /// <summary>菜单以平台菜单字号为基准，原默认1。</summary>
    public double MenuFontScale { get => _menuFontScale <= 0 ? 1 : _menuFontScale; set => SetProperty(ref _menuFontScale, Math.Round(value, 5)); }
    /// <summary>目录树以消息字号为基准，原默认1。</summary>
    public double FolderTreeFontScale { get => _folderTreeFontScale <= 0 ? 1 : _folderTreeFontScale; set => SetProperty(ref _folderTreeFontScale, Math.Round(value, 5)); }
    /// <summary>原面板列表比例，默认1.25。</summary>
    public double PanelFontScale { get => _panelFontScale <= 0 ? 1.25 : _panelFontScale; set => SetProperty(ref _panelFontScale, Math.Round(value, 5)); }
    /// <summary>保留原Windows渲染偏好；Mac不将其伪装为等价开关。</summary>
    public bool IsClearTypeEnabled { get => _isClearTypeEnabled; set => SetProperty(ref _isClearTypeEnabled, value); }
}
