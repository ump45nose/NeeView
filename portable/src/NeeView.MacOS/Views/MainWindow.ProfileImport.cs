using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;

public sealed partial class MainWindow
{
    private bool _profileImportApplying;
    private ProfileImportService? _profileImport;
    private Func<ProfileImportRequest, Task>? _applyProfileImport;
    private ProfileImportWindow? _profileImportWindow;
    /// <summary>启动层接入只读后端；使用同一原命令表和实际入口能力生成报告。</summary>
    /// <param name="reader">Engine 读取替换点，不暴露 ZIP 或系统对象。</param>
    /// <param name="apply">唯一启动层的实际应用委托；null 保持只读装配。</param>
    public void AttachProfileImport(IProfileImportReader reader, Func<ProfileImportRequest, Task>? apply = null)
    {
        if (_model is null) throw new InvalidOperationException("请先绑定阅读模型。");
        _applyProfileImport = apply;
        _profileImport = new(reader, _model.Commands.Definitions, _model.Commands.Definitions.Where(d => IsCommandImplemented(d.Name)).Select(d => d.Name).ToHashSet());
        // Bind 时尚无读取能力；重新创建菜单同时更新禁用说明和状态，不能残留占位 tooltip。
        BuildMenus();
    }
    /// <summary>宿主重建或回滚后报告实际结果，不由视图解析备份。</summary>
    public void ReportProfileImport(string message) => ShowError(message);
    /// <summary>同一窗口仅一个预览；关闭对话框后才进入宿主事务，避免关闭时等待自身。</summary>
    private async Task ShowProfileImportAsync()
    {
        if (_profileImport is null || _profileImportWindow is not null || _profileImportApplying || _preparing) return;
        using var model = new ProfileImportViewModel(_profileImport);
        var dialog = new ProfileImportWindow(model); _profileImportWindow = dialog;
        ProfileImportRequest? request;
        try { request = await dialog.ShowDialog<ProfileImportRequest?>(this); }
        finally { _profileImportWindow = null; }
        if (request is not null && !_preparing && !_closedPrepared && _applyProfileImport is not null)
        {
            _profileImportApplying = true; IsEnabled = false;
            try { await _applyProfileImport(request); }
            finally { _profileImportApplying = false; if (!_closedPrepared) IsEnabled = true; }
        }
    }
}
