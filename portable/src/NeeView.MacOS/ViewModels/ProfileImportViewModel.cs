using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
namespace NeeView.MacOS.ViewModels;

/// <summary>路径映射编辑草稿；不触碰原 JSON 或文件系统。</summary>
public sealed class ProfilePathMappingEdit : ObservableObject
{
    private string _windowsPrefix = "", _macPrefix = "";
    public string WindowsPrefix { get => _windowsPrefix; set => SetProperty(ref _windowsPrefix, value); }
    public string MacPrefix { get => _macPrefix; set => SetProperty(ref _macPrefix, value); }
}
/// <summary>独立预览表现；只持有候选，代次拒绝关闭、换来源或编辑映射后晚到的结果。</summary>
public sealed class ProfileImportViewModel : ObservableObject, IDisposable
{
    private readonly ProfileImportService _service;
    private ProfileImportSource? _source;
    private ProfileImportPreview? _preview;
    private CancellationTokenSource? _request;
    private long _generation;
    private bool _disposed, _busy;
    private string? _error;
    private readonly HashSet<ProfilePathMappingEdit> _observedMappings = [];
    public ObservableCollection<ProfilePathMappingEdit> Mappings { get; } = [];
    public string SourceLabel => _source?.Path ?? "请选择旧 Profile 目录或 .nvzip 备份";
    public ProfileImportPreview? Preview { get => _preview; private set { if (SetProperty(ref _preview, value)) OnPropertyChanged(nameof(Summary)); } }
    public bool IsBusy { get => _busy; private set { if (SetProperty(ref _busy, value)) OnPropertyChanged(nameof(CanPreview)); } }
    public bool CanPreview => !_disposed && _source is not null && !IsBusy;
    public string? Error { get => _error; private set => SetProperty(ref _error, value); }
    public string Summary => Preview is { } p ? $"{p.Paths.Count} 条路径，{p.UnmappedCount} 条未映射，{p.Commands.Count} 个命令 · 只读预览" : "未生成预览；不会写入当前数据。";
    /// <summary>使用 Engine 预览契约，编辑行为与窗口结构、主题分开。</summary>
    public ProfileImportViewModel(ProfileImportService service) { _service = service; Mappings.CollectionChanged += MappingsChanged; }
    /// <summary>来源切换立即使旧候选失效，再读取明确选定来源。</summary>
    /// <param name="source">系统选择器返回的真实来源。</param><returns>读取与预览完成任务。</returns>
    public Task SelectSourceAsync(ProfileImportSource source)
    {
        if (_disposed) return Task.CompletedTask;
        Invalidate(); _source = source; OnPropertyChanged(nameof(SourceLabel)); return RefreshAsync();
    }
    /// <summary>快照编辑字段，再进行后台预览；失败保留来源及映射草稿供重试。</summary>
    public async Task RefreshAsync()
    {
        if (!CanPreview || _source is null) return;
        Invalidate(); var generation = _generation; var request = new CancellationTokenSource(); _request = request; IsBusy = true;
        var mappings = Mappings.Select(m => new ProfilePathMapping(m.WindowsPrefix, m.MacPrefix)).ToArray();
        try
        {
            var preview = await _service.PreviewAsync(_source, mappings, request.Token);
            if (!_disposed && generation == _generation && !request.IsCancellationRequested) Preview = preview;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex) { if (!_disposed && generation == _generation) Error = ex.Message; }
        finally
        {
            if (ReferenceEquals(_request, request)) { _request = null; IsBusy = false; }
            request.Dispose();
        }
    }
    private void MappingsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var item in _observedMappings.Where(item => !Mappings.Contains(item)).ToArray())
        { item.PropertyChanged -= MappingChanged; _observedMappings.Remove(item); }
        foreach (var item in Mappings)
            if (_observedMappings.Add(item)) item.PropertyChanged += MappingChanged;
        Invalidate();
    }
    private void MappingChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Invalidate();
    private void Invalidate()
    { _generation++; _request?.Cancel(); Preview = null; Error = null; IsBusy = false; OnPropertyChanged(nameof(CanPreview)); }
    /// <summary>关闭只取消读取资格；所有晚到任务已在 RefreshAsync 中观察，不能刷新关闭后的界面。</summary>
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; Invalidate(); Mappings.CollectionChanged -= MappingsChanged;
        foreach (var mapping in _observedMappings) mapping.PropertyChanged -= MappingChanged;
        _observedMappings.Clear();
    }
}
