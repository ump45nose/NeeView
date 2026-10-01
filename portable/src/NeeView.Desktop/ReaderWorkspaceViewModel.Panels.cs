using NeeView.Application;
using NeeView.Core;

namespace NeeView.Desktop;

/// <summary>面板表现逻辑只使用应用契约；布局与样式调整无需修改这些流程。</summary>
public sealed partial class ReaderWorkspaceViewModel
{
    /// <summary>从指定快照加载导航数据；目录根策略与视图树控件分离。</summary>
    public async Task<NavigationData> NavigationAsync(ReaderSnapshot snapshot, CancellationToken token)
    {
        var history = await _states.HistoryAsync(token);
        var bookmarks = BookmarkNode.Build(await _states.BookmarksAsync(token));
        FolderNode? root = null;
        if (snapshot.Index is { } index)
        {
            var directory = index.Capabilities.IsArchive ? Path.GetDirectoryName(index.Locator.Path)! : index.Locator.Path;
            root = new(Path.GetDirectoryName(directory) ?? directory);
            await ExpandFolderAsync(root, token);
        }
        return new(history, bookmarks, root);
    }
    /// <summary>延迟展开单一节点；失败后仍可重试，不提前标记已加载。</summary>
    public async Task ExpandFolderAsync(FolderNode node, CancellationToken token = default)
    {
        if (node.Loaded) return;
        var children = await _folders.ChildrenAsync(node.Path, token);
        node.Children.Clear(); foreach (var path in children) node.Children.Add(new(path)); node.Loaded = true;
    }
    /// <summary>刷新独立的两区数据；同目录翻页由服务避免重复扫描。</summary>
    public async Task<DestinationData> DestinationsAsync(bool force, CancellationToken token = default)
    {
        var page = _session.Snapshot.ActionTarget ?? _session.Snapshot.Current;
        var directory = page is { Locator.Entry: null } ? Path.GetDirectoryName(page.Locator.Path) : null;
        await _destinations.RefreshAsync(directory, force, token);
        return new(_destinations.Managed.ToArray(), _destinations.Children.ToArray());
    }
    /// <summary>选择手动目标后原子更新配置，不持久化直接子目录。</summary>
    public async Task AddDestinationAsync()
    {
        if (await Dialogs.PickFolderAsync() is not { } path) return;
        await UpdateSettingsAsync(s => s with { DestinationFolders = [.. s.DestinationFolders, path] }); await RefreshPanelsAsync();
    }
    /// <summary>移除或上移手动目标；数字映射随列表顺序更新。</summary>
    public async Task EditDestinationAsync(string path, bool moveUp)
    {
        await UpdateSettingsAsync(s =>
        {
            var paths = s.DestinationFolders.ToList(); var index = paths.IndexOf(path);
            if (!moveUp) paths.Remove(path);
            else if (index > 0) (paths[index - 1], paths[index]) = (paths[index], paths[index - 1]);
            return s with { DestinationFolders = paths };
        }); await RefreshPanelsAsync();
    }
    /// <summary>创建直接子目录后仅对仍有效的显式目标执行分类。</summary>
    public async Task CreateDestinationAsync()
    {
        var snapshot = _session.Snapshot;
        if (snapshot.ActionTarget is not { Locator.Entry: null } page) return;
        if (await Dialogs.TextAsync("新建直接子目录", "") is not { } name) return;
        var child = await _destinations.CreateChildAsync(Path.GetDirectoryName(page.Locator.Path)!, name);
        if (snapshot.Generation == _session.Snapshot.Generation && _session.Snapshot.ActionTarget?.Id == page.Id) await ClassifyAsync(child);
        await RefreshPanelsAsync();
    }
    /// <summary>重试恢复日志并重建当前来源；恢复结果进入状态文本。</summary>
    public async Task RecoverFilesAsync()
    {
        var report = await Task.Run(() => _files.RecoverAsync());
        Status = report.Count == 0 ? "没有待恢复操作。" : string.Join("；", report.Select(r => r.Message));
        await _session.RefreshAsync(); await RefreshPanelsAsync();
    }
    /// <summary>打开书签后按锚点或旧条目名称定位。</summary>
    public async Task OpenBookmarkAsync(Bookmark mark)
    {
        if (mark.Locator is not { } locator) return;
        await OpenAsync(locator.Path);
        if (mark.Anchor is { } anchor) await _session.LocateAsync(anchor);
        else if (mark.LegacyPage is { } legacy && _session.Snapshot.Index?.Pages.FirstOrDefault(p => p.Name.Replace('\\', '/') == legacy.Replace('\\', '/')) is { } page)
            await _session.LocateAsync(new(page.Id));
    }
    /// <summary>创建目录、重命名或删除树节点，视图只传选择项和动作。</summary>
    public async Task EditBookmarkAsync(Bookmark? selected, string action)
    {
        if (action == "folder")
        {
            if (await Dialogs.TextAsync("书签文件夹名称", "新文件夹") is not { Length: > 0 } name) return;
            await _states.SaveBookmarkAsync(new(Guid.NewGuid().ToString("N"), selected?.Locator is null ? selected?.Id : null,
                (await _states.BookmarksAsync()).Count, name, null, null, null));
        }
        else if (selected is not null && action == "rename")
        {
            if (await Dialogs.TextAsync("书签名称", selected.Name) is not { Length: > 0 } name) return;
            await _states.SaveBookmarkAsync(selected with { Name = name });
        }
        else if (selected is not null && action == "delete" && await Dialogs.ConfirmAsync("删除书签", "删除所选书签及其子节点？", "删除"))
            await _states.DeleteBookmarkAsync(selected.Id);
        await RefreshPanelsAsync();
    }
}
