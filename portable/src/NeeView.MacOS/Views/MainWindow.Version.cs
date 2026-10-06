namespace NeeView.MacOS.Views;

public sealed partial class MainWindow
{
    private VersionWindow? _versionWindow;
    /// <summary>原owner/CenterOwner/ShowDialog；重复调用复用同一附属窗口，退出沿OwnedWindows关闭。</summary>
    private async Task ShowVersionAsync()
    {
        if (_platform is null || _preparing || _closedPrepared) return;
        if (_versionWindow is { } existing) { existing.Activate(); return; }
        var dialog = new VersionWindow(_platform); _versionWindow = dialog;
        try { await dialog.ShowDialog(this); }
        finally { if (ReferenceEquals(_versionWindow, dialog)) _versionWindow = null; }
    }
}
