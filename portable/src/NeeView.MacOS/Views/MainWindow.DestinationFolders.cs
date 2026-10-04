using Avalonia.Controls;
using Avalonia.Threading;
namespace NeeView.MacOS.Views;

public sealed partial class MainWindow
{
    private Task _destinationAction = Task.CompletedTask;
    private void AttachDestinationFolders()
    {
        if (_model is null) return;
        if (_platform is not null && _images is not null) _model.Operation.AttachFileDeletion(_platform, _images);
        _model.Operation.ConfirmDeleteAsync = async path => !_preparing && !_closedPrepared
            && await ConfirmAsync("删除当前图片", "将以下图片移至系统废纸篓？\n" + path, "移至废纸篓") && !_preparing && !_closedPrepared;
        _model.Operation.ConfirmDeleteBookAsync = async path => !_preparing && !_closedPrepared
            && await ConfirmAsync("删除当前书籍", "将整本书籍移至系统废纸篓？目录书籍将包含目录中的全部文件。\n" + path, "移至废纸篓") && !_preparing && !_closedPrepared;
        _model.Operation.AskBookNameAsync = async target =>
        {
            if (_preparing || _closedPrepared) return null;
            var name = await AskNameAsync("重命名书籍", System.IO.Path.GetFileName(target.Path), selectStem: !target.IsDirectory);
            return _preparing || _closedPrepared ? null : name;
        };
        _model.Operation.ConfirmBookRenameAsync = async plan =>
        {
            if (_preparing || _closedPrepared) return false;
            if (plan.ExtensionChanged && !await ConfirmAsync("更改书籍扩展名", "更改扩展名可能导致无法打开该书籍。\n" + plan.Destination, "更改扩展名")) return false;
            if (_preparing || _closedPrepared) return false;
            if (plan.Conflict && !await ConfirmAsync("书籍名称已存在", "将使用未占用的名称：\n" + plan.Destination, "使用此名称")) return false;
            return !_preparing && !_closedPrepared;
        };
        _model.Operation.RetryBookRenameAsync = async message => !_preparing && !_closedPrepared
            && await ConfirmAsync("书籍重命名失败", message, "重试") && !_preparing && !_closedPrepared;
        _model.Operation.ConfirmBookOverwriteAsync = async plan => !_preparing && !_closedPrepared
            && await ConfirmAsync("覆盖已有书籍", "目标已存在：\n" + plan.Destination
                + (plan.Target.IsDirectory ? "\n将替换整个目标目录及其中全部内容，不合并目录。" : "\n将替换已有文件。")
                + "\n提交前保留可恢复副本；成功后清理。整书传输不进入分类撤销历史。", "覆盖") && !_preparing && !_closedPrepared;
        var panel = this.FindControl<DestinationFolderPanelView>("DestinationPanelView")!;
        panel.Attach(_model.Operation); panel.Failed += (_, message) => ShowError(message);
        panel.ManageAsync = ManageDestinationFoldersAsync; panel.CreateAsync = CreateDestinationChildAsync;
        if (_model.Operation.DestinationMoves is { } moves)
        {
            moves.ConfirmOverwriteAsync = async path => !_preparing && !_closedPrepared
                && await ConfirmAsync("覆盖已有图片", "目标已存在：\n" + path + "\n覆盖前将保留可恢复副本。", "覆盖") && !_preparing && !_closedPrepared;
            moves.StateChanged += DestinationMove_Changed;
        }
    }
    private void DestinationMove_Changed(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    { if (!_preparing && !_closedPrepared) RefreshHistoryCommandStates(); });
    private async Task ManageDestinationFoldersAsync()
    {
        if (_model is null || _preparing || _closedPrepared) return;
        var result = await new DestinationFolderDialog(Config.Current.System.DestinationFolderCollection).ShowDialog<DestinationFolderCollection?>(this);
        if (result is not null && !_preparing && !_closedPrepared)
            await _model.Operation.EditDestinationFoldersAsync(() => Config.Current.System.DestinationFolderCollection = result);
    }
    private async Task CreateDestinationChildAsync()
    {
        if (_model is null || _preparing || _closedPrepared || _model.Operation.CurrentPictureDirectory is not { } directory) return;
        var name = await AskNameAsync("新建直接子目录", "新建文件夹");
        if (name is not null && !_preparing && !_closedPrepared) await _model.Operation.CreateDestinationChildAsync(name, directory);
    }
    /// <summary>固定移动/固定复制与数字命令共用目标菜单，参数由原JSON读取，语义独立。</summary>
    private async Task OpenDestinationMoveMenuAsync(string command = "MoveToFolderAs")
    {
        if (_model is null) return;
        var parameter = _model.Operation.GetDestinationParameter(command);
        if (parameter.Index > 0)
        {
            var folders = Config.Current.System.DestinationFolderCollection;
            if (folders.IsValidIndex(parameter.Index - 1)) await RunDestinationActionAsync(() => TransferAsync(folders[parameter.Index - 1]));
            return;
        }
        var menu = new ContextMenu();
        foreach (var folder in Config.Current.System.DestinationFolderCollection)
        {
            var item = new MenuItem { Header = folder.Name, IsEnabled = IsDestinationCommandAvailable(command) }; ToolTip.SetTip(item, folder.Path);
            item.Click += async (_, _) => await RunDestinationActionAsync(() => TransferAsync(folder));
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "没有手动目标", IsEnabled = false });
        var manage = new MenuItem { Header = "管理目标文件夹…" }; manage.Click += async (_, _) => { try { await ManageDestinationFoldersAsync(); } catch (Exception ex) { ShowError(ex.Message); } }; menu.Items.Add(manage);
        Viewer.ContextMenu = menu; menu.Open(Viewer);
        Task TransferAsync(DestinationFolder folder) => command is "CopyBookToFolderAs" or "MoveBookToFolderAs"
            ? _model.Operation.TransferBookToFolderAsync(folder, move: command == "MoveBookToFolderAs")
            : command == "CopyToFolderAs"
            ? _model.Operation.CopyToFolderAsync(folder, parameter.MultiPagePolicy)
            : _model.Operation.ClassifyAsync(folder, copy: command != "MoveToFolderAs" && Config.Current.Panels.IsDestinationFolderCopyMode, policy: parameter.MultiPagePolicy);
    }
    private bool IsDestinationCommandAvailable(string command)
    {
        if (_model is null) return false;
        var parameter = _model.Operation.GetDestinationParameter(command);
        var folders = Config.Current.System.DestinationFolderCollection;
        bool capable = command switch
        {
            "CopyBookToFolderAs" => _model.Operation.CanCopyBookToFolder,
            "MoveBookToFolderAs" => _model.Operation.CanMoveBookToFolder,
            _ => _model.Operation.CanTransferFileActionPages(parameter.MultiPagePolicy, requireWriteAccess: command != "CopyToFolderAs")
        };
        return capable
            && (parameter.Index == 0 || folders.IsValidIndex(parameter.Index - 1) && folders[parameter.Index - 1].IsValid());
    }
    /// <summary>所有宿主文件动作共用可等待任务；失败已回报，退出不会重复等待故障任务。</summary>
    private Task RunDestinationActionAsync(Func<Task> action)
    {
        if (_preparing || _closedPrepared || !_destinationAction.IsCompleted) return Task.CompletedTask;
        return _destinationAction = CoreAsync();
        async Task CoreAsync()
        {
            try { await action(); } catch (Exception ex) { ShowError(ex.Message); }
        }
    }
    /// <summary>启动恢复提示不改变焦点，也不把模糊中断误报为恢复成功。</summary>
    public void ReportFileRecovery(IReadOnlyList<string> warnings) { if (warnings.Count > 0) ShowError(string.Join("\n", warnings)); }
}
