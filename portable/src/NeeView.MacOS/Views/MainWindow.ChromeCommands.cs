using Avalonia.Controls;
namespace NeeView.MacOS.Views;

public sealed partial class MainWindow
{
    private WindowState _minimizeResumeState = WindowState.Normal;
    private Task _settingFolderAction = Task.CompletedTask;
    private CancellationTokenSource? _settingFolderCancellation;
    /// <summary>原菜单改变持久开关；快捷键按实际可见性和原自动隐藏资格临时显示。</summary>
    /// <param name="command">原地址栏或页面滑条命令。</param><param name="fromMenu">菜单忽略固定Toggle参数，改持久配置。</param>
    private void SetChromeVisible(string command, bool fromMenu)
    {
        if (_model is null) return;
        bool address = command == "ToggleVisibleAddressBar";
        bool enabled = address ? Config.Current.MenuBar.IsAddressBarEnabled : Config.Current.Slider.IsEnabled;
        bool current = address ? _model.AddressBarVisible && _model.MenuVisible : _model.SliderVisible;
        bool requested = _model.SaveData.GetCommandParameter<ToggleCommandParameter>(command).GetState(fromMenu ? enabled : current, fromMenu);
        bool autoHide = address ? _model.CanHideMenu : Config.Current.Slider.IsHidePageSlider || Config.Current.Slider.IsHidePageSliderInAutoHideMode && _model.AutoHideMode;
        bool temporary = !fromMenu && autoHide;
        if (address) Config.Current.MenuBar.IsAddressBarEnabled = temporary || requested;
        else Config.Current.Slider.IsEnabled = temporary || requested;
        _model.RefreshChromeSettings(); _autoHide?.Refresh();
        if (temporary) _autoHide?.SetChromeVisibleOnce(address, requested);
    }
    /// <summary>唯一平台契约打开独立Mac Profile目录，加载阅读期间也可使用；不创建或修改文件。</summary>
    private Task OpenSettingFolderAsync() => _settingFolderAction.IsCompleted
        ? _settingFolderAction = OpenSettingFolderCoreAsync() : _settingFolderAction;
    private async Task OpenSettingFolderCoreAsync()
    {
        await Task.Yield();
        if (_preparing || _closedPrepared || _platform is null || _model is null) return;
        using var cancel = new CancellationTokenSource(); _settingFolderCancellation = cancel;
        try { await _platform.OpenFolderAsync(_model.SaveData.DirectoryPath, cancel.Token); }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (Exception error) { if (!_preparing && !_closedPrepared) ShowError("打开设置目录失败：" + error.Message); }
        finally { if (ReferenceEquals(_settingFolderCancellation, cancel)) _settingFolderCancellation = null; }
    }
}
