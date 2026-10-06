namespace NeeView.MacOS.Views;

public sealed partial class MainWindow
{
    private HelpDocumentService? _helpDocuments;
    private Task _helpAction = Task.CompletedTask;
    private CancellationTokenSource? _helpCancellation;
    /// <summary>原帮助命令只生成本地HTML并经平台打开，阅读和当前菜单的交互状态独立。</summary>
    private Task OpenManualAsync(string command) => _helpAction.IsCompleted && !_preparing && !_closedPrepared && _model is not null && _platform is not null
        ? _helpAction = OpenManualCoreAsync(command) : _helpAction;
    private async Task OpenManualCoreAsync(string command)
    {
        await Task.Yield();
        if (_preparing || _closedPrepared || _model is null || _platform is null) return;
        using var cancel = new CancellationTokenSource(); _helpCancellation = cancel;
        _helpDocuments ??= new(_platform);
        try
        {
            var kind = command == "HelpMainMenu" ? HelpDocumentKind.MainMenu : HelpDocumentKind.SearchOptions;
            var definitions = _model.Commands.Definitions;
            var migrated = definitions.Where(d => IsCommandImplemented(d.Name)).Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
            var html = await Task.Run(() => kind == HelpDocumentKind.MainMenu ? MainMenuManual.CreateMainMenuManual(definitions, migrated.Contains)
                : SearchOptionManual.CreateSearchOptionManual(), cancel.Token);
            cancel.Token.ThrowIfCancellationRequested();
            await _helpDocuments.OpenAsync(kind, html, cancel.Token);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (Exception error) { if (!_preparing && !_closedPrepared) ShowError("打开帮助失败：" + error.Message); }
        finally { if (ReferenceEquals(_helpCancellation, cancel)) _helpCancellation = null; }
    }
}
