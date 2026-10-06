using CommunityToolkit.Mvvm.ComponentModel;
namespace NeeView.MacOS.ViewModels;

/// <summary>原字体六设置的独立草稿；字体列表由表现适配提供，不扫描文件或读取系统API。</summary>
public sealed class FontSettingsViewModel : ObservableObject
{
    private string _fontName;
    private decimal _fontScale, _menuScale, _treeScale, _panelScale;
    public IReadOnlyList<string> FontNames { get; }
    public string FontName { get => _fontName; set { if (value is not null) SetProperty(ref _fontName, value); } }
    public decimal FontPercent { get => _fontScale; set => SetProperty(ref _fontScale, value); }
    public decimal MenuPercent { get => _menuScale; set => SetProperty(ref _menuScale, value); }
    public decimal TreePercent { get => _treeScale; set => SetProperty(ref _treeScale, value); }
    public decimal PanelPercent { get => _panelScale; set => SetProperty(ref _panelScale, value); }
    // 原编辑范围100–200%；已导入的范围外有效值仍能显示/无改动保存。
    public decimal FontMinimum { get; } public decimal FontMaximum { get; }
    public decimal MenuMinimum { get; } public decimal MenuMaximum { get; }
    public decimal TreeMinimum { get; } public decimal TreeMaximum { get; }
    public decimal PanelMinimum { get; } public decimal PanelMaximum { get; }
    public bool IsClearTypeEnabled { get; }
    /// <summary>缺失字体加入选择列表，打开或取消表单不改原配置。</summary>
    /// <param name="config">当前唯一字体配置。</param><param name="installedNames">显示端提供的已安装字体族。</param>
    public FontSettingsViewModel(FontsConfig config, IEnumerable<string> installedNames)
    {
        _fontName = config.FontName;
        // SelectedItem使用精确字符串比较，保留当前名称的原大小写，避免打开表单就清空选择。
        FontNames = installedNames.Append(config.DefaultFontName).Where(s => !s.Equals(_fontName, StringComparison.OrdinalIgnoreCase)).Append(_fontName)
            .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.CurrentCulture).ToArray();
        _fontScale = Percent(config.FontScale); _menuScale = Percent(config.MenuFontScale); _treeScale = Percent(config.FolderTreeFontScale); _panelScale = Percent(config.PanelFontScale);
        FontMinimum = Math.Min(100, _fontScale); FontMaximum = Math.Max(200, _fontScale);
        MenuMinimum = Math.Min(100, _menuScale); MenuMaximum = Math.Max(200, _menuScale);
        TreeMinimum = Math.Min(100, _treeScale); TreeMaximum = Math.Max(200, _treeScale);
        PanelMinimum = Math.Min(100, _panelScale); PanelMaximum = Math.Max(200, _panelScale);
        IsClearTypeEnabled = config.IsClearTypeEnabled;
    }
    /// <summary>应用到原事务；禁用的WindowsClearType字段保持原值。</summary>
    public void Apply(FontsConfig target)
    { target.FontName = FontName; target.FontScale = (double)(FontPercent / 100); target.MenuFontScale = (double)(MenuPercent / 100); target.FolderTreeFontScale = (double)(TreePercent / 100); target.PanelFontScale = (double)(PanelPercent / 100); }
    private static decimal Percent(double scale) => checked((decimal)scale * 100);
}
