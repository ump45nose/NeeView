// Copyright (c) NeeLaboratory. 原主题选择、BasedOn 和失败回退，移除 WPF/Windows 资源及 Toast 操作。
namespace NeeView;

/// <summary>平台只提供值快照，Engine 不读取 AppKit 或界面主题。</summary>
public sealed record SystemThemeState(bool IsDark, bool IsHighContrast, ThemeRgba AccentColor);
/// <summary>可提交给显示适配的完整颜色表；错误保留供提示，配置选择不被回退覆盖。</summary>
public sealed record ThemeLoadResult(ThemeProfile Profile, ThemeType EffectiveType, string? Error)
{
    public bool IsDark
    {
        get { var c = Profile.GetColor("Window.Background", 1); return .2126 * c.R + .7152 * c.G + .0722 * c.B < 128; }
    }
}

/// <summary>只读主题服务；单槽后台加载和递归深度上限，显示资源由 Mac 表现层管理。</summary>
public sealed class ThemeManager
{
    private readonly SemaphoreSlim _gate = new(1);
    /// <summary>原六预设及一级 JSON 自定义项；扫描错误只返回预设并报告，不创建目录。</summary>
    /// <param name="folder">本次配置的自定义目录。</param><param name="token">表单关闭/新扫描取消。</param>
    public async Task<(IReadOnlyList<ThemeSource> Items, string? Error)> CollectThemesAsync(string folder, CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                var items = Enum.GetValues<ThemeType>().Where(t => t != ThemeType.Custom).Select(t => new ThemeSource(t)).ToList();
                try
                {
                    if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                        foreach (var file in Directory.EnumerateFiles(folder).Where(p => Path.GetExtension(p).Equals(".json", StringComparison.OrdinalIgnoreCase)))
                        { token.ThrowIfCancellationRequested(); items.Add(new(ThemeType.Custom, Path.GetFileName(file))); }
                    return ((IReadOnlyList<ThemeSource>)items.AsReadOnly(), (string?)null);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                { return ((IReadOnlyList<ThemeSource>)items.AsReadOnly(), ex.Message); }
            }, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
    /// <summary>沿原选择与回退规则生成已解析颜色，取消不应用回退，错误不覆写原 ThemeType。</summary>
    public async Task<ThemeLoadResult> LoadAsync(ThemeSource source, string folder, SystemThemeState system, CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var effective = source.Type == ThemeType.System ? system.IsHighContrast ? ThemeType.HighContrast : system.IsDark ? ThemeType.Dark : ThemeType.Light : source.Type;
                    var profile = effective == ThemeType.Custom
                        ? ValidateBasedOn(ThemeProfileTools.LoadFromFile(Path.Combine(folder, source.FileName!)), Path.GetFullPath(Path.Combine(folder, source.FileName!)), [], token)
                        : ThemeProfileTools.LoadFromContent(effective + "Theme.json");
                    if (source.Type == ThemeType.System && !system.IsHighContrast) profile.Colors["Control.Accent"] = new(system.AccentColor, 1);
                    token.ThrowIfCancellationRequested();
                    return new ThemeLoadResult(profile.Validate(), effective, null);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // 原自定义失败先回Dark，内置预设失败才使用默认颜色；不要更改用户选择。
                    if (source.Type == ThemeType.Custom)
                        try { return new ThemeLoadResult(ThemeProfileTools.LoadFromContent("DarkTheme.json").Validate(), ThemeType.Dark, ex.Message); }
                        catch (Exception fallback) { return new ThemeLoadResult(ThemeProfile.Default.Validate(), ThemeType.Dark, ex.Message + "; " + fallback.Message); }
                    return new ThemeLoadResult(ThemeProfile.Default.Validate(), ThemeType.Dark, ex.Message);
                }
            }, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
    /// <summary>原 themes://、绝对及当前文件相对继承；规范绝对定位检测循环，最多32层。</summary>
    private static ThemeProfile ValidateBasedOn(ThemeProfile profile, string currentFile, HashSet<string> ancestors, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (ancestors.Count >= 32 || !ancestors.Add(currentFile)) throw new FormatException("Circular or excessive theme inheritance: " + currentFile);
        if (string.IsNullOrWhiteSpace(profile.BasedOn)) return ThemeProfileTools.Merge(ThemeProfile.Default, profile);
        if (profile.BasedOn.StartsWith("themes://", StringComparison.Ordinal))
            return ThemeProfileTools.Merge(ThemeProfileTools.LoadFromContent(profile.BasedOn["themes://".Length..]), profile);
        var path = Path.GetFullPath(Path.IsPathFullyQualified(profile.BasedOn) ? profile.BasedOn : Path.Combine(Path.GetDirectoryName(currentFile)!, profile.BasedOn));
        return ThemeProfileTools.Merge(ValidateBasedOn(ThemeProfileTools.LoadFromFile(path), path, ancestors, token), profile);
    }
}
