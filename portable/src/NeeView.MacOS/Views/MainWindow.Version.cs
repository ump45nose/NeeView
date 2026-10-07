namespace NeeView.MacOS.Views;

public sealed partial class MainWindow
{
    private VersionWindow? _versionWindow;
    private IApplicationReleaseService? _releaseService;
    /// <summary>唯一启动层注入公开发布查询，窗口不创建具体HTTP后端。</summary>
    public void AttachReleaseService(IApplicationReleaseService service) => _releaseService = service;
    /// <summary>原owner/CenterOwner/ShowDialog；重复调用复用同一附属窗口，退出沿OwnedWindows关闭。</summary>
    private async Task ShowVersionAsync()
    {
        if (_platform is null || _preparing || _closedPrepared) return;
        if (_versionWindow is { } existing) { existing.Activate(); return; }
        var dialog = new VersionWindow(_platform, _releaseService); _versionWindow = dialog;
        try { await dialog.ShowDialog(this); }
        finally { if (ReferenceEquals(_versionWindow, dialog)) _versionWindow = null; }
    }
}
