using Avalonia.Controls;
namespace NeeView.MacOS.Views;

public sealed partial class MainWindow
{
    private Task _quickAccessEditTask = Task.CompletedTask;
    private bool _quickAccessBusy;
    /// <summary>原快速访问动作宿主，文字采集与JSON事务分开，关闭后不提交弹窗结果。</summary>
    private async Task QuickAccessActionAsync(string action, FolderTreeNodeBase? node, string? path)
    {
        if (_model is null || _preparing || _closedPrepared) return;
        var collection = _model.SaveData.QuickAccess; var value = (node as QuickAccessDirectoryNode)?.Value;
        switch (action)
        {
            case "open": if (node is not null) { _model.Operation.Bookshelf.FolderTree.SelectedItem = node; await _model.Operation.Bookshelf.FolderTree.DecideAsync(); } return;
            case "reveal": if (node is not null && _platform is not null) await _platform.RevealAsync(node.Path); return;
            case "refresh": await _model.Operation.Bookshelf.FolderTree.RefreshDirectoryAsync(); return;
            case "add-current": path = node?.Path; goto case "add";
            case "add":
                path ??= _model.Operation.Bookshelf.Place ?? _model.Operation.Book?.Path;
                if (string.IsNullOrWhiteSpace(path)) return;
                var parent = value?.IsFolder == true ? value : collection.Root;
                await EditQuickAccessAsync(c => c.Add(parent, path)); return;
            case "folder":
                var folderName = await AskNameAsync("新建快速访问文件夹", "新建文件夹");
                if (folderName is not null && !_preparing && !_closedPrepared) await EditQuickAccessAsync(c => c.AddFolder(value ?? c.Root, folderName)); return;
            case "rename":
                if (value is null) return; var name = await AskNameAsync("快速访问名称", value.DisplayName);
                if (name is not null && !_preparing && !_closedPrepared) await EditQuickAccessAsync(c => { c.Rename(value, name); return value; }); return;
            case "properties":
                if (value is null || value.IsFolder) return;
                var newPath = await AskNameAsync("快速访问目标路径", value.Path ?? "");
                if (!string.IsNullOrWhiteSpace(newPath) && !_preparing && !_closedPrepared) await EditQuickAccessAsync(c => { if (!c.Root.Walk().Contains(value)) throw new InvalidOperationException("节点已不存在。"); value.Path = newPath; return value; }); return;
            case "remove": if (value is not null) await EditQuickAccessAsync(c => { c.Remove(value); return value; }); return;
            case "up": case "down":
                if (value is null || collection.ParentOf(value)?.Children is not { } children) return;
                int index = children.IndexOf(value), next = index + (action == "up" ? -1 : 1);
                if (next >= 0 && next < children.Count) await EditQuickAccessAsync(c => { c.Move(value, children[next], action == "up" ? -1 : 1); return value; }); return;
        }
    }
    /// <summary>QuickAccess内部重排/普通目录登记只编辑原集合，不移动源文件。</summary>
    private Task MoveQuickAccessAsync(FolderTreeNodeBase source, QuickAccessDirectoryNode target, int delta) => EditQuickAccessAsync(c =>
    {
        if (source is QuickAccessDirectoryNode quick) { c.Move(quick.Value, target.Value, delta); return quick.Value; }
        var parent = delta == 0 ? target.Value : c.ParentOf(target.Value) ?? c.Root;
        var added = c.Add(parent, source.Path);
        if (delta != 0) c.Move(added, target.Value, delta);
        return added;
    });
    /// <summary>跟踪唯一事务供退出等待；失败显示并保留原树，禁止重入。</summary>
    private Task EditQuickAccessAsync(Func<QuickAccessCollection, QuickAccessTreeNode> edit)
    {
        if (_model is null || _quickAccessBusy || _preparing || _closedPrepared) return Task.CompletedTask;
        return _quickAccessEditTask = RunAsync();
        async Task RunAsync()
        {
            _quickAccessBusy = true;
            try { await _model.SaveData.EditQuickAccessAsync(edit); }
            catch (Exception ex) { ShowError(ex.Message); }
            finally { _quickAccessBusy = false; }
        }
    }
}
