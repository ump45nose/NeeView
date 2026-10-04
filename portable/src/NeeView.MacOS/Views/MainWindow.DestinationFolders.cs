using Avalonia.Controls;
using Avalonia.Threading;
namespace NeeView.MacOS.Views;

public sealed partial class MainWindow
{
    private Task _destinationAction = Task.CompletedTask;
    private void AttachDestinationFolders()
    {
        if (_model is null) return;
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
    /// <summary>原MoveToFolderAs无索引时打开完整目标菜单，始终移动，不跟随复制模式。</summary>
    private async Task OpenDestinationMoveMenuAsync(string command = "MoveToFolderAs")
    {
        if (_model is null) return;
        var parameter = _model.Operation.GetDestinationParameter(command);
        if (parameter.MultiPagePolicy != MultiPagePolicy.Once) throw new NotSupportedException("本批仅迁入单图分类策略，多页策略保留待后续迁入。");
        if (parameter.Index > 0) { await RunDestinationActionAsync(() => _model.Operation.ClassifyAsync(parameter.Index, followPanelMode: command != "MoveToFolderAs")); return; }
        var menu = new ContextMenu();
        foreach (var folder in Config.Current.System.DestinationFolderCollection)
        {
            var item = new MenuItem { Header = folder.Name, IsEnabled = _model.Operation.CanFileAction }; ToolTip.SetTip(item, folder.Path);
            item.Click += async (_, _) => await RunDestinationActionAsync(() => _model.Operation.ClassifyAsync(folder, copy: command != "MoveToFolderAs" && Config.Current.Panels.IsDestinationFolderCopyMode));
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "没有手动目标", IsEnabled = false });
        var manage = new MenuItem { Header = "管理目标文件夹…" }; manage.Click += async (_, _) => { try { await ManageDestinationFoldersAsync(); } catch (Exception ex) { ShowError(ex.Message); } }; menu.Items.Add(manage);
        Viewer.ContextMenu = menu; menu.Open(Viewer);
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
