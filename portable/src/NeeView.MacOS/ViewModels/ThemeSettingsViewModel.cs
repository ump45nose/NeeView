using CommunityToolkit.Mvvm.ComponentModel;
namespace NeeView.MacOS.ViewModels;

/// <summary>主题设置草稿；仅转交 Engine 扫描，不读文件或创建显示资源。</summary>
public sealed class ThemeSettingsViewModel : ObservableObject, IDisposable
{
    private readonly ThemeManager _manager = new();
    private CancellationTokenSource? _scan;
    private bool _disposed;
    private string _folder, _message = "";
    private ThemeChoice _selected;
    private IReadOnlyList<ThemeChoice> _items;
    /// <summary>保存当前原主题标识；缺失的自定义项仍保留，取消或无关保存不会改选择。</summary>
    public ThemeSettingsViewModel(ThemeConfig config)
    {
        _folder = config.CustomThemeFolder; _selected = Choice(config.ThemeType);
        _items = Enum.GetValues<ThemeType>().Where(t => t != ThemeType.Custom).Select(t => Choice(new(t))).ToArray();
        if (config.ThemeType.Type == ThemeType.Custom) _items = _items.Append(_selected).ToArray();
    }
    public string Folder { get => _folder; set => SetProperty(ref _folder, value); }
    public ThemeChoice Selected { get => _selected; set { if (value is not null) SetProperty(ref _selected, value); } }
    public IReadOnlyList<ThemeChoice> Items { get => _items; private set => SetProperty(ref _items, value); }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    /// <summary>只发布最新目录扫描；清空 ItemsSource 导致的临时 null 不改变原选择。</summary>
    public async Task RefreshAsync()
    {
        if (_disposed) return;
        _scan?.Cancel(); _scan?.Dispose(); var source = _scan = new CancellationTokenSource();
        Message = "读取主题列表…";
        try
        {
            var result = await _manager.CollectThemesAsync(Folder, source.Token);
            if (_disposed || source != _scan || source.IsCancellationRequested) return;
            var selected = Selected.Source; var items = result.Items.Select(Choice).ToList();
            if (!items.Any(i => i.Source.Equals(selected))) items.Add(new(selected, Choice(selected).Label + "（未找到文件）"));
            Items = items.AsReadOnly(); Selected = items.Single(i => i.Source.Equals(selected));
            Message = result.Error ?? "自定义主题读取原 JSON；保存后应用，加载失败使用 Dark 并提示。";
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { }
    }
    /// <summary>在原 ApplyOptions 保存锁内写入已验证草稿，失败由同一配置快照回滚。</summary>
    public void Apply(ThemeConfig target) { target.CustomThemeFolder = Folder; target.ThemeType = Selected.Source; }
    /// <summary>显示文案可单独调整，配置使用稳定原标识。</summary>
    private static ThemeChoice Choice(ThemeSource source) => new(source, source.Type switch
    {
        ThemeType.Dark => "深色", ThemeType.DarkMonochrome => "深色（单色）", ThemeType.Light => "浅色",
        ThemeType.LightMonochrome => "浅色（单色）", ThemeType.HighContrast => "高对比度", ThemeType.System => "跟随系统",
        _ => Path.GetFileNameWithoutExtension(source.FileName)!
    });
    /// <summary>关闭表单取消扫描，晚到结果不更新已关闭窗口。</summary>
    public void Dispose() { _disposed = true; _scan?.Cancel(); _scan?.Dispose(); _scan = null; }
}
public sealed record ThemeChoice(ThemeSource Source, string Label);
