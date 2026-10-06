// Copyright (c) NeeLaboratory. 原 RefreshThemeColor 的 Avalonia 资源适配。
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
namespace NeeView.MacOS.Views;

/// <summary>唯一应用主题适配；不持有书籍/缓存，不改变布局或阅读规则。</summary>
public sealed class ThemePresenter : IDisposable
{
    private readonly Application _app;
    private readonly ThemeConfig _config;
    private readonly ThemeManager _manager = new();
    private CancellationTokenSource? _loading;
    private bool _disposed;
    public ThemeLoadResult? Current { get; private set; }
    public event Action<ThemeLoadResult>? Applied;
    /// <summary>窗口重建绑定其原配置实例；生命周期内只订阅一个系统颜色入口。</summary>
    public ThemePresenter(Application app, ThemeConfig config)
    { _app = app; _config = config; if (app.PlatformSettings is { } settings) settings.ColorValuesChanged += SystemColorsChanged; }
    /// <summary>复制平台色值，Engine 不接收 Avalonia 类型；Headless 缺平台时采用明确默认。</summary>
    public SystemThemeState GetSystemState()
    {
        var colors = _app.PlatformSettings?.GetColorValues();
        var accent = colors?.AccentColor1 ?? Color.FromRgb(0x11, 0x88, 0xDD);
        return new(colors?.ThemeVariant != PlatformThemeVariant.Light, colors?.ContrastPreference == ColorContrastPreference.High,
            new(accent.A, accent.R, accent.G, accent.B));
    }
    /// <summary>按代次提交完整颜色表；旧请求、关闭或系统连续变化不能覆盖新主题。</summary>
    public async Task RefreshAsync()
    {
        if (_disposed) return;
        _loading?.Cancel(); _loading?.Dispose(); var request = _loading = new CancellationTokenSource();
        var source = _config.ThemeType; var folder = _config.CustomThemeFolder; var system = GetSystemState();
        try
        {
            var result = await _manager.LoadAsync(source, folder, system, request.Token);
            if (_disposed || request != _loading || request.IsCancellationRequested) return;
            Apply(result); Applied?.Invoke(result);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
    }
    /// <summary>原完整色键、特殊按钮和边框；Fluent 变体与扩展绘制颜色只在表现层适配。</summary>
    public void Apply(ThemeLoadResult result)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed) return;
        var profile = result.Profile;
        _app.RequestedThemeVariant = result.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        Color ColorOf(string key) { var c = profile.GetColor(key, 1); return Color.FromArgb(c.A, c.R, c.G, c.B); }
        void Brush(string target, string key) => _app.Resources[target] = new SolidColorBrush(ColorOf(key));
        foreach (var key in ThemeProfile.Keys) Brush(key, key);
        _app.Resources["BottomBar.Background.Color"] = ColorOf("BottomBar.Background");
        _app.Resources["Window.BorderThickness"] = new Thickness(ColorOf("Window.Border").A > 0 ? 1 : 0);
        _app.Resources["Window.Dialog.BorderThickness"] = new Thickness(ColorOf("Window.Dialog.Border").A > 0 ? 1 : 0);
        foreach (var kind in new[] { "Accent", "Danger" })
        {
            var opaque = ColorOf("Button.Background").A > 0;
            Brush("Button." + kind + ".Background", opaque ? "Control." + kind : "Button.Background");
            Brush("Button." + kind + ".Foreground", opaque ? "Control." + kind + "Text" : "Button.Foreground");
            Brush("Button." + kind + ".Border", "Control." + kind);
        }
        // Mac 扩展没有新主题文件格式，映射到原角色以保持配置可复用。
        foreach (var (target, key) in new[] { ("Gallery.Background", "Window.Background"), ("Gallery.Placeholder", "Panel.Background"),
            ("Gallery.Selection", "Control.Accent"), ("Gallery.Scrollbar", "ScrollBar.Foreground"), ("Gallery.Text", "Panel.Foreground"),
            ("PlaylistMarkBrush", "PlaylistItemIcon.Foreground"), ("Control.Danger.Text", "Control.DangerText") }) Brush(target, key);
        _app.Resources["SystemAccentColor"] = ColorOf("Control.Accent");
        Current = result;
        // 自定义绘制按需读取原资源；仅失效可见控件，不请求图片或复制像素。
        if (_app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            foreach (var window in desktop.Windows)
                foreach (var control in window.GetVisualDescendants().OfType<Control>().Prepend(window)) control.InvalidateVisual();
    }
    /// <summary>系统主题事件来自任意线程，统一到 UI；只刷新原 System 选择。</summary>
    private void SystemColorsChanged(object? sender, PlatformColorValues e) => Dispatcher.UIThread.Post(async () =>
    { if (!_disposed && _config.ThemeType.Type == ThemeType.System) await RefreshAsync(); });
    /// <summary>关闭/导入重建取消旧需求并退订平台事件，应用资源保留到新窗口接管。</summary>
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        if (_app.PlatformSettings is { } settings) settings.ColorValuesChanged -= SystemColorsChanged;
        _loading?.Cancel(); _loading?.Dispose(); _loading = null; Applied = null;
    }
}
