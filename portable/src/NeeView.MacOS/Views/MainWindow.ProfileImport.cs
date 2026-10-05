using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;

public sealed partial class MainWindow
{
    private ProfileImportService? _profileImport;
    private ProfileImportWindow? _profileImportWindow;
    /// <summary>启动层接入只读后端；使用同一原命令表和实际入口能力生成报告。</summary>
    /// <param name="reader">Engine 读取替换点，不暴露 ZIP 或系统对象。</param>
    public void AttachProfileImport(IProfileImportReader reader)
    {
        if (_model is null) throw new InvalidOperationException("请先绑定阅读模型。");
        _profileImport = new(reader, _model.Commands.Definitions, _model.Commands.Definitions.Where(d => IsCommandImplemented(d.Name)).Select(d => d.Name).ToHashSet());
        // Bind 时尚无读取能力；重新创建菜单同时更新禁用说明和状态，不能残留占位 tooltip。
        BuildMenus();
    }
    /// <summary>同一窗口仅允许一个预览对话框；不启用尚未完成的原 ImportBackup 写入命令。</summary>
    private async Task ShowProfileImportAsync()
    {
        if (_profileImport is null || _profileImportWindow is not null || _preparing) return;
        using var model = new ProfileImportViewModel(_profileImport);
        var dialog = new ProfileImportWindow(model); _profileImportWindow = dialog;
        try { await dialog.ShowDialog(this); }
        finally { _profileImportWindow = null; }
    }
}
