using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
namespace NeeView.MacOS.Views;

/// <summary>原字体资源的显示替换点；只应用资源，不访问书籍或图片工厂。</summary>
public sealed class FontPresenter
{
    private readonly Application _app;
    private readonly FontsConfig _config;
    private readonly FontEnvironment _environment;
    public string? FallbackMessage { get; private set; }
    /// <summary>绑定唯一配置及平台值，不订阅草稿变更，避免保存失败时泄漏外观。</summary>
    public FontPresenter(Application app, FontsConfig config, FontEnvironment environment)
    { _app = app; _config = config; _environment = environment; config.DefaultFontName = environment.DefaultFontName; }
    /// <summary>完整计算成功后替换资源；未知字体保留原配置，以系统默认显示并提示。</summary>
    public void Apply()
    {
        Dispatcher.UIThread.VerifyAccess();
        var sizes = FontParameters.Calculate(_config, _environment);
        var name = _config.FontName;
        var available = name == _environment.DefaultFontName || FontManager.Current.SystemFonts.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        FallbackMessage = available ? null : "字体“" + name + "”未安装，当前使用系统默认字体；原配置名称已保留。";
        var family = new FontFamily(available ? name : _environment.DefaultFontName);
        _app.Resources["DefaultFontFamily"] = family;
        // 原Calibri箭头字体在Mac不保证存在，使用同一字体后端的系统字形回退。
        _app.Resources["ArrowFontFamily"] = family;
        foreach (var (key, value) in new[] { ("SystemFontSize", sizes.SystemFontSize), ("SystemFontSizeNormal", sizes.SystemFontSizeNormal),
            ("SystemFontSizeLarge", sizes.SystemFontSizeLarge), ("SystemFontSizeHuge", sizes.SystemFontSizeHuge),
            ("DefaultFontSize", sizes.DefaultFontSize), ("MenuFontSize", sizes.MenuFontSize),
            ("FolderTreeFontSize", sizes.FolderTreeFontSize), ("PanelFontSize", sizes.PaneFontSize), ("FontIconSize", sizes.FontIconSize) })
            _app.Resources[key] = value;
    }
}
