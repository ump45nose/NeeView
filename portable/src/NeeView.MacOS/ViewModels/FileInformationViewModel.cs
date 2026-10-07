using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using NeeView.Media.Imaging.Metadata;
namespace NeeView.MacOS.ViewModels;

/// <summary>原信息面板的页选择/加载表现；业务字段由PageMetadataTools提供，样式独立。</summary>
public sealed class FileInformationViewModel : ObservableObject, IDisposable
{
    private Book? _book;
    private CancellationTokenSource? _request;
    private int _revision;
    private bool _active, _disposed;
    private Page? _selected;
    private IReadOnlyList<Page> _pages = [];
    private IReadOnlyList<InformationGroupRow> _groups = [];
    private string _error = "";
    private bool _loading;
    private PagePictureInfo? _info;
    public IReadOnlyList<Page> Pages { get => _pages; private set => SetProperty(ref _pages, value); }
    public Page? Selected { get => _selected; set { if (SetProperty(ref _selected, value)) StartLoad(); } }
    public IReadOnlyList<InformationGroupRow> Groups { get => _groups; private set => SetProperty(ref _groups, value); }
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    public bool IsLoading { get => _loading; private set => SetProperty(ref _loading, value); }
    public Task Loading { get; private set; } = Task.CompletedTask;
    public GridLength HeaderWidth { get { try { return GridLength.Parse(Config.Current.Information.PropertyHeaderWidth); } catch { return new(128); } } }
    public bool CanOpenMap => _info?.Metadata[BitmapMetadataKey.GPSLatitude] is ExifGpsDegree { IsValid: true }
        && _info.Metadata[BitmapMetadataKey.GPSLongitude] is ExifGpsDegree { IsValid: true };
    /// <summary>接收同一书籍当前Page集合；隐藏取消加载，配置变化使用已读信息重排分组。</summary>
    public void Update(Book? book, bool active)
    {
        if (_disposed) return;
        var pages = book?.CurrentPages ?? [];
        bool changed = !ReferenceEquals(book, _book) || !_pages.SequenceEqual(pages) || _active != active;
        _book = book; _active = active;
        if (!_pages.SequenceEqual(pages)) Pages = pages;
        if (!pages.Contains(Selected!)) { _selected = book?.CurrentPage; OnPropertyChanged(nameof(Selected)); changed = true; }
        if (changed) StartLoad(); else Publish();
    }
    private void StartLoad()
    {
        _revision++; _request?.Cancel(); _request = null; _info = null; Error = ""; IsLoading = false; Publish();
        if (_disposed || !_active || Selected is not { } page) { Loading = Task.CompletedTask; return; }
        var source = _request = new CancellationTokenSource(); IsLoading = true; Loading = LoadAsync(page, _revision, source);
    }
    private async Task LoadAsync(Page page, int revision, CancellationTokenSource request)
    {
        try
        {
            var info = await page.LoadPictureInfoAsync(request.Token);
            if (_disposed || revision != _revision || request.IsCancellationRequested) return;
            _info = info; Error = info?.MetadataWarning ?? page.Content.Error ?? ""; Publish();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex) { if (!_disposed && revision == _revision) { Error = ex.Message; Publish(); } }
        finally { if (ReferenceEquals(_request, request)) { _request = null; IsLoading = false; } request.Dispose(); }
    }
    private void Publish()
    {
        OnPropertyChanged(nameof(HeaderWidth)); OnPropertyChanged(nameof(CanOpenMap));
        if (Selected is not { } page) { Groups = []; return; }
        try
        {
            var rows = InformationKeyExtensions.DefaultKeys.Where(key => key.ToInformationCategory() switch
                { InformationCategory.Image => _info is not null || page.Content.HasSize, InformationCategory.Metadata => _info?.Metadata.IsValid == true, _ => true })
                .Select(key => new InformationFieldRow(key.ToString(),
                Label("InformationKey." + key), MetadataValueTools.ToDisplayString(PageMetadataTools.GetValue(page, key, _info)) ?? "", key.ToInformationGroup())).ToList();
            if (_info is not null) rows.AddRange(_info.Metadata.ExtraMap.Select(pair => new InformationFieldRow(pair.Key, pair.Key,
                MetadataValueTools.ToDisplayString(pair.Value) ?? "", InformationGroup.Extras)));
            if (_info?.Metadata.IsValid == true && _info.Metadata.ExtraMap.Count == 0)
                rows.Add(new("ExtraEmpty", Label("InformationKey.ExtraEmpty"), "", InformationGroup.Extras));
            Groups = rows.GroupBy(row => row.Group).Where(group => Config.Current.Information.IsVisibleGroup(group.Key))
                .Select(group => new InformationGroupRow(Label("InformationGroup." + group.Key), group.ToArray())).ToArray();
        }
        catch (Exception ex) { Error = ex.Message; Groups = []; }
    }
    public static string Label(string key) => HelpText.GetString(key) is { Length: > 0 } label ? label : key.Split('.').Last();
    public void Refresh() => Update(_book, _active);
    /// <summary>原四个地图替换键；系统打开由宿主提供，缺失坐标不执行。</summary>
    public Uri? CreateMapUri()
    {
        if (!CanOpenMap) return null;
        var lat = (ExifGpsDegree)_info!.Metadata[BitmapMetadataKey.GPSLatitude]!;
        var lon = (ExifGpsDegree)_info.Metadata[BitmapMetadataKey.GPSLongitude]!;
        return new Uri(Config.Current.Information.MapProgramFormat.Replace("{LatDeg}", lat.ToValueString("{0:F5}"), StringComparison.Ordinal)
            .Replace("{LonDeg}", lon.ToValueString("{0:F5}"), StringComparison.Ordinal).Replace("{Lat}", lat.ToFormatString(), StringComparison.Ordinal)
            .Replace("{Lon}", lon.ToFormatString(), StringComparison.Ordinal), UriKind.Absolute);
    }
    /// <summary>取消需求；真实工作结束后释放取消源，晚到结果不能更新旧面板。</summary>
    public void Dispose() { _disposed = true; _revision++; _request?.Cancel(); }
}
public sealed record InformationFieldRow(string Key, string Name, string Value, InformationGroup Group);
public sealed record InformationGroupRow(string Name, IReadOnlyList<InformationFieldRow> Fields);
