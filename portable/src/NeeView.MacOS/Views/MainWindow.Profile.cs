using Avalonia.Controls;
using Avalonia.Platform.Storage;
namespace NeeView.MacOS.Views;

public sealed partial class MainWindow
{
    private bool _profileBusy;
    private Task _profileAction = Task.CompletedTask;
    private CancellationTokenSource? _profileCancellation;
    /// <summary>同窗口设置动作互斥；关闭先取消准备并等待任务，已完成导出不会误报取消。</summary>
    private Task RunProfileActionAsync(string command) => _profileAction.IsCompleted && !_preparing && !_profileImportApplying
        ? _profileAction = ProfileActionCoreAsync(command) : Task.CompletedTask;
    private async Task ProfileActionCoreAsync(string command)
    {
        await Task.Yield();
        if (_model is null || !_model.Operation.CanManageProfile || _preparing) return;
        using var cancellation = new CancellationTokenSource(); _profileCancellation = cancellation;
        _profileBusy = true; IsEnabled = false;
        try
        {
            if (command == "ExportBackup")
            {
                var filename = _model.SaveData.GetCommandParameter<ExportBackupCommandParameter>(command).FileName;
                if (string.IsNullOrWhiteSpace(filename))
                {
                    using var documents = await StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Documents);
                    cancellation.Token.ThrowIfCancellationRequested();
                    filename = await AwaitBackupPathAsync(StorageProvider.SaveFilePickerAsync(new()
                    {
                        Title = "导出 NeeView 备份", SuggestedStartLocation = documents,
                        SuggestedFileName = "NeeView46.3-" + DateTime.Now.ToString("yyyyMMdd"), DefaultExtension = "nvzip", ShowOverwritePrompt = true,
                        FileTypeChoices = [new("NeeView BackupFile") { Patterns = ["*.nvzip"] }]
                    }), cancellation.Token);
                }
                cancellation.Token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(filename)) return;
                CaptureProfileWindowLayout();
                var result = await _model.Operation.ExportBackupAsync(filename, cancellation.Token);
                ShowError("备份已导出：" + result.Path);
            }
            else if (command == "SaveSetting")
            {
                CaptureProfileWindowLayout(); await _model.Operation.SaveAllAsync(cancellation.Token);
                ShowError("设置及阅读状态已保存。");
            }
            else
            {
                await _model.Operation.ReloadSettingAsync(cancellation.Token);
                if (_preparing || _closedPrepared) return;
                _model.RestorePanelLayout(); _sidePanels?.RestoreLayout();
                _leftWidth = Config.Current.Panels.LeftWidth; _rightWidth = Config.Current.Panels.RightWidth;
                RefreshFonts(); if (_themePresenter is not null) await _themePresenter.RefreshAsync();
                _model.Refresh(); _model.RefreshSelection(); _model.RefreshPanels(); _model.RefreshNavigationPanel();
                UpdatePanelColumns(); RefreshPageTreeLayout(); RefreshFolderTreeLayout();
                _pagePresentation?.RefreshCovers(); await FilmStrip.RefreshAsync(); await Viewer.RefreshAsync();
                BuildMenus(); BeginSlideShowAutoScroll();
                ShowError("设置已重载，当前书籍保持打开。");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { ShowError("设置操作失败：" + error.Message); }
        finally
        {
            if (ReferenceEquals(_profileCancellation, cancellation)) _profileCancellation = null;
            _profileBusy = false; if (!_closedPrepared) IsEnabled = true;
            RefreshHistoryCommandStates();
        }
    }
    /// <summary>原生选择器无取消入口，关闭只停止等待；晚到结果只释放，不能续写Profile或访问窗口。</summary>
    /// <param name="picker">已经发起的系统保存选择器。</param><param name="token">关闭准备令牌。</param>
    /// <returns>当次有效选择的本地路径；取消传播，原生UI关闭仍由系统/窗口管理。</returns>
    internal static async Task<string?> AwaitBackupPathAsync(Task<IStorageFile?> picker, CancellationToken token)
    {
        try
        {
            using var selected = await picker.WaitAsync(token);
            token.ThrowIfCancellationRequested();
            return selected?.TryGetLocalPath();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _ = DisposeLatePickerAsync(picker);
            throw;
        }
    }
    private static async Task DisposeLatePickerAsync(Task<IStorageFile?> picker)
    {
        try { using var selected = await picker; }
        catch { /* 原生取消/失败的晚到结果已观察，不再回报关闭的窗口。 */ }
    }
    /// <summary>保存实际侧栏比例到原配置；不在视图写文件或处理JSON。</summary>
    private void CaptureProfileWindowLayout()
    {
        var left = this.FindControl<Border>("LeftPanel")!.Bounds.Width;
        var right = this.FindControl<Border>("RightPanel")!.Bounds.Width;
        if (left > 0) Config.Current.Panels.LeftWidth = left;
        if (right > 0) Config.Current.Panels.RightWidth = right;
        _sidePanels?.SaveWeights();
    }
}
