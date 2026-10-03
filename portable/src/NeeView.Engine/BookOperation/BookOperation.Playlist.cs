namespace NeeView;
public sealed partial class BookOperation
{
    private PlaylistHub? _playlistHub;
    private readonly SemaphoreSlim _playlistNavigation = new(1);
    public PlaylistHub Playlists
    {
        get { if (_playlistHub is null) { _playlistHub = saveData.Playlists; _playlistHub.Changed += Playlist_Changed; } return _playlistHub; }
    }
    public event EventHandler? MarkersChanged;
    /// <summary>仅发布标记变化，不重建帧或重新解码正文。</summary>
    private void Playlist_Changed(object? sender, EventArgs e) { RefreshMarkers(); MarkersChanged?.Invoke(this, EventArgs.Empty); }
    /// <summary>切书或重排按原 EntryFullName 映射全局登记。</summary>
    private void RefreshMarkers()
    {
        if (Book is { } book) book.Marker.SetMarkers(_playlistHub?.Current is { } list ? new BookPlaylist(book, list).Collect() : []);
    }
    /// <summary>原当前主图片切换；双页和分割页不扩为全帧批次。</summary>
    /// <param name="fromMenu">菜单沿原规则忽略固定 On/Off 参数。</param>
    public async Task TogglePlaylistItemAsync(bool fromMenu = false)
    {
        await Playlists.InitializeAsync(); await _gate.WaitAsync();
        try
        {
            if (_disposed || _closing || IsLoading || Book?.CurrentPage is not { } page || Playlists.Current is not { } list || !new BookPlaylist(Book, list).CanRegister(page)) return;
            var parameter = saveData.GetCommandParameter<ToggleCommandParameter>("TogglePlaylistItem");
            bool? enabled = fromMenu ? null : parameter.ToggleMode switch { ToggleMode.On => true, ToggleMode.Off => false, _ => null };
            await Playlists.SetMarkAsync(page.ArchiveEntry.SystemPath, enabled);
        }
        finally { _gate.Release(); }
    }
    /// <summary>原面板 AddCurrentPage 使用已生效 SelectedPages 首项，临时 PageSelector 不改变登记对象。</summary>
    public async Task AddSelectedPlaylistPageAsync()
    {
        await Playlists.InitializeAsync(); await _gate.WaitAsync();
        try
        {
            if (_disposed || _closing || IsLoading || Book is null || Playlists.Current is not { } list) return;
            var page = Frame?.Elements.Where(element => !element.IsDummy).Select(element => element.Page).MinBy(page => page.Index);
            if (page is not null && new BookPlaylist(Book, list).CanRegister(page)) Playlists.SelectedItem = await Playlists.AddAsync(page.ArchiveEntry.SystemPath);
        }
        finally { _gate.Release(); }
    }
    /// <summary>书内导航独立于列表选择，按排序后页面索引和原参数计算目标。</summary>
    public async Task MovePlaylistItemInBookAsync(int direction)
    {
        await Playlists.InitializeAsync(); var book = Book;
        if (book?.CurrentPage is not { } current || IsLoading || _closing) return;
        RefreshMarkers();
        var parameter = saveData.GetCommandParameter<MovePlaylistItemInBookCommandParameter>(direction < 0 ? "PrevPlaylistItemInBook" : "NextPlaylistItemInBook");
        var target = book.Marker.GetNearMarkedPage(current.Index, direction, parameter.IsLoop, parameter.IsIncludeTerminal);
        if (target is not null) await JumpCoreAsync(target.Index, false, true, book);
    }
    /// <summary>列表项先尝试当前书定位，其他路径共用唯一加载链；失效保持旧书。</summary>
    public async Task OpenPlaylistItemAsync(PlaylistItem item)
    {
        if (_disposed || _closing || IsLoading || !Playlists.GetViewItems(Book).Contains(item)) return;
        var list = Playlists.Current;
        Playlists.SelectedItem = item;
        var book = Book; var page = book?.Pages.FirstOrDefault(page => page.EntryFullName == item.Path);
        if (page is not null) await JumpCoreAsync(page.Index, false, true, book, list);
        else await OpenCoreAsync(item.Path, CancellationToken.None, expectedPlaylist: list);
    }
    /// <summary>原 CanPrev/NextMarkInPlace：有登记或包含首尾即可执行，不按端点隐藏命令。</summary>
    public bool CanMovePlaylistItemInBook(int direction) => !IsLoading && Book is { } book &&
        (book.Marker.Markers.Count > 0 || saveData.GetCommandParameter<MovePlaylistItemInBookCommandParameter>(direction < 0 ? "PrevPlaylistItemInBook" : "NextPlaylistItemInBook").IsIncludeTerminal);
    /// <summary>列表项按筛选/分组后的显示顺序前后移动，首尾不循环。</summary>
    public async Task MovePlaylistItemAsync(int direction)
    {
        await _playlistNavigation.WaitAsync();
        try
        {
            await Playlists.InitializeAsync(); if (_disposed || _closing || IsLoading) return;
            var items = Playlists.GetViewItems(Book); int current = items.ToList().IndexOf(Playlists.SelectedItem!);
            int next = current + direction;
            if (next < 0 || next >= items.Count) { Error = "已到播放列表首尾。"; Notify(); return; }
            await OpenPlaylistItemAsync(items[next]);
        }
        finally { _playlistNavigation.Release(); }
    }
}
