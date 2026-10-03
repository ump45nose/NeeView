// Copyright (c) NeeLaboratory. 原 FolderList.SetFolderOrder/ToggleFolderOrder/MoveRandomFolder 适配。
namespace NeeView;

public sealed partial class BookOperation
{
    private Task<bool>? _restoreBookshelf;
    /// <summary>原StartUp.LastFolder恢复独立书架，最多申请一次，不抢走正文打开请求。</summary>
    public Task<bool> RestoreBookshelfAsync() => _restoreBookshelf ??= RestoreCoreAsync();
    private async Task<bool> RestoreCoreAsync()
    {
        if (Config.Current.StartUp is not { IsOpenLastFolder: true, LastFolder: { } last } || last.Path.Length == 0) return false;
        last.Register(saveData.FolderConfigs);
        return await Bookshelf.SetPlaceAsync(last.Path, last.Select);
    }
    private void SaveLastBookshelf()
    {
        Config.Current.StartUp.LastFolder = Config.Current.StartUp.IsOpenLastFolder && Bookshelf.Place is { } path
            ? BookshelfFolderMemento.Create(path, Bookshelf.SelectedItem?.Path, saveData.FolderConfigs) : null;
    }
    /// <summary>原普通目录排序能力及切换顺序：枚举顺序排除路径/登记时间类别。</summary>
    public static IReadOnlyList<FolderOrder> NormalFolderOrders { get; } = Enum.GetValues<FolderOrder>()
        .Where(e => !e.IsEntryCategory() && !e.IsPathCategory()).ToArray();

    /// <summary>修改当前目录原参数并可靠保存；失败恢复排序/种子/选择，不能污染全局默认。</summary>
    /// <param name="order">当前来源支持的排序。</param>
    public async Task ChangeFolderOrderAsync(FolderOrder order)
    {
        if (_disposed || _closing || Bookshelf.IsLoading || Bookshelf.Place is null || !(Bookshelf.IsBookmarkPlace ? Enum.IsDefined(order) : NormalFolderOrders.Contains(order))) return;
        await _historyGate.WaitAsync();
        try
        {
            if (_disposed || _closing || Bookshelf.IsLoading || Bookshelf.Place is null) return;
            try { await saveData.EditFolderParametersAsync(() => Bookshelf.ChangeOrder(order)); }
            catch { Bookshelf.ReloadParameter(); throw; }
            await Bookshelf.RefreshBookmarkMetadataAsync();
        }
        finally { _historyGate.Release(); }
    }
    /// <summary>沿原普通排序能力表循环，不选择当前来源不支持的类别。</summary>
    public Task ToggleFolderOrderAsync()
    { var orders = Bookshelf.IsBookmarkPlace ? Enum.GetValues<FolderOrder>() : NormalFolderOrders.ToArray(); return ChangeFolderOrderAsync(orders[(Array.IndexOf(orders, Bookshelf.FolderOrder) + 1) % orders.Length]); }

    /// <summary>原随机书籍命令从当前书架排除当前书籍；不改变排序或随机种子，打开失败保留选择。</summary>
    public async Task RandomBookAsync()
    {
        if (_disposed || _closing || IsLoading) return;
        await _historyGate.WaitAsync();
        try
        {
            if (_disposed || _closing || IsLoading || Book is null) return;
            var book = Book; var generation = _generation;
            if (Bookshelf.Place is null && !await Bookshelf.SyncAsync(book)) return;
            if (_disposed || _closing || generation != _generation || Bookshelf.IsLoading) return;
            var items = Bookshelf.Items.Where(e => e.Path != book.Path && e.Bookmark?.IsFolder != true).ToArray();
            if (items.Length == 0) return;
            var item = items[Random.Shared.Next(items.Length)];
            if (await OpenCoreAsync(item.Path, CancellationToken.None)) Bookshelf.Select(item);
        }
        finally { _historyGate.Release(); }
    }
}
