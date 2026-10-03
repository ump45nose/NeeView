using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Threading;
using NeeView;
namespace NeeView.MacOS.ViewModels;

/// <summary>原播放列表面板的独立表现模型；不读文件、不持有解码或窗口。</summary>
public sealed class PlaylistViewModel(BookOperation operation) : ObservableObject, IDisposable
{
    public BookOperation Operation { get; } = operation;
    public PlaylistHub Hub => Operation.Playlists;
    public IReadOnlyList<PlaylistFileChoice> Files { get; private set; } = [];
    public PlaylistFileChoice? CurrentFile => Files.FirstOrDefault(file => file.Path == Hub.Current?.Path);
    public IReadOnlyList<PlaylistRow> Rows { get; private set; } = [];
    public string FilterMessage => Hub.Error ?? (Hub.Config.IsCurrentBookFilterEnabled ? "仅显示当前书籍的登记项" : "");
    public bool CanEdit => Hub.Current is not null && !Operation.IsLoading;
    public bool CanRestore => CanEdit && Hub.Current!.CanRestore;
    public bool CanAdd => CanEdit && Operation.Book?.CurrentPage is not null;
    public event EventHandler? Refreshed;
    private bool _disposed;

    /// <summary>订阅原阅读和全局列表，初始源加载错误在面板中显示。</summary>
    public async Task AttachAsync()
    {
        Operation.Changed += Changed; Operation.MarkersChanged += Changed;
        await Hub.InitializeAsync(); if (!_disposed) Refresh();
    }
    /// <summary>业务回报只更新列表表现，不能触发正文资源申请。</summary>
    private void Changed(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() => { if (!_disposed) Refresh(); });
    /// <summary>保留行引用与原分组顺序，组标题不成为可打开的伪条目。</summary>
    public void Refresh()
    {
        var old = Rows.ToDictionary(row => row.Item);
        string? lastPlace = null;
        Rows = Hub.GetViewItems(Operation.Book).Select(item =>
        {
            string? header = Hub.Config.IsGroupBy && item.Place != lastPlace ? item.Place : null;
            lastPlace = item.Place;
            return old.TryGetValue(item, out var row) && row.GroupHeader == header && row.Name == item.Name ? row : new PlaylistRow(item, header);
        }).ToArray();
        Files = Hub.PlaylistFiles.Select(path => new PlaylistFileChoice(path)).ToArray();
        OnPropertyChanged(""); Refreshed?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>窗口关闭必须解绑，旧表现不再处理列表事件。</summary>
    public void Dispose() { if (_disposed) return; _disposed = true; Operation.Changed -= Changed; Operation.MarkersChanged -= Changed; }
}
/// <summary>文件名是表现投影，选择保存原完整路径。</summary>
public sealed record PlaylistFileChoice(string Path) { public string Name => System.IO.Path.GetFileNameWithoutExtension(Path); }
/// <summary>原条目投影，分组和别名更新不改变业务对象。</summary>
public sealed record PlaylistRow(PlaylistItem Item, string? GroupHeader)
{
    public string Name { get; } = Item.Name;
    public string Path => Item.Path;
    public bool HasGroupHeader => GroupHeader is not null;
}
