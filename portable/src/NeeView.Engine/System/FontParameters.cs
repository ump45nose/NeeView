// Copyright (c) NeeLaboratory. 原 FontParameters.UpdateFonts 的纯值计算；显示资源移至 Mac。
namespace NeeView;

/// <summary>平台字体度量快照，以DIP提供消息/菜单字号；不含原生字体或界面对象。</summary>
public sealed record FontEnvironment(string DefaultFontName, double MessageFontSize, double MenuFontSize);

/// <summary>原字体资源尺寸；没有全局订阅或应用资源依赖。</summary>
public sealed record FontParameters(double SystemFontSize, double DefaultFontSize, double MenuFontSize,
    double FolderTreeFontSize, double PaneFontSize, double FontIconSize)
{
    public double SystemFontSizeNormal => Math.Min(SystemFontSize * 1.25, Math.Max(SystemFontSize, 24));
    public double SystemFontSizeLarge => Math.Min(SystemFontSize * 1.5, Math.Max(SystemFontSize, 24));
    public double SystemFontSizeHuge => Math.Min(SystemFontSize * 2, Math.Max(SystemFontSize, 24));
    /// <summary>沿原判断/乘法顺序计算所有字体角色；非法度量在应用资源前报错。</summary>
    /// <param name="config">唯一原字体配置。</param><param name="environment">启动层提供的系统值。</param>
    /// <returns>可直接应用的完整字号快照。</returns>
    public static FontParameters Calculate(FontsConfig config, FontEnvironment environment)
    {
        var result = new FontParameters(environment.MessageFontSize, environment.MessageFontSize * config.FontScale,
            environment.MenuFontSize * config.MenuFontScale, environment.MessageFontSize * config.FolderTreeFontScale,
            environment.MessageFontSize * config.PanelFontScale, Math.Max(environment.MessageFontSize * config.FontScale + 15, 28));
        if (new[] { environment.MenuFontSize, result.SystemFontSize, result.DefaultFontSize, result.MenuFontSize,
            result.FolderTreeFontSize, result.PaneFontSize, result.FontIconSize }.Any(v => !double.IsFinite(v) || v <= 0))
            throw new ArgumentOutOfRangeException(nameof(config), "字体比例或平台字号必须为有限正数。");
        return result;
    }
}
