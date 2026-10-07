using Avalonia.Controls;
namespace NeeView.MacOS.Views;
public sealed partial class SettingsWindow
{
    /// <summary>原中央浮窗设置作为独立草稿控件，取消不写原 JSON。</summary>
    private void FillMainView()
    {
        var c = Config.Current.MainView;
        foreach (var (name, value) in new[] { ("MainViewEndWhenClosed", c.IsFloatingEndWhenClosed), ("MainViewTopmost", c.IsTopmost),
            ("MainViewFront", c.IsFrontAsPossible), ("MainViewHideTitle", c.IsHideTitleBar), ("MainViewAutoStretch", c.IsAutoStretch),
            ("MainViewAutoHide", c.IsAutoHide), ("MainViewAutoShow", c.IsAutoShow) }) this.FindControl<CheckBox>(name)!.IsChecked = value;
        this.FindControl<ComboBox>("MainViewAlternative")!.SelectedIndex = (int)c.AlternativeContent;
    }
    /// <summary>在既有可回滚的 ApplyOptionsAsync 事务内应用，不创建第二配置模型。</summary>
    private void ApplyMainView()
    {
        var c = Config.Current.MainView;
        bool Checked(string name) => this.FindControl<CheckBox>(name)!.IsChecked == true;
        c.IsFloatingEndWhenClosed = Checked("MainViewEndWhenClosed"); c.IsTopmost = Checked("MainViewTopmost");
        c.IsFrontAsPossible = Checked("MainViewFront"); c.IsHideTitleBar = Checked("MainViewHideTitle"); c.IsAutoStretch = Checked("MainViewAutoStretch");
        c.IsAutoHide = Checked("MainViewAutoHide"); c.IsAutoShow = Checked("MainViewAutoShow");
        c.AlternativeContent = (AlternativeContent)Math.Max(0, this.FindControl<ComboBox>("MainViewAlternative")!.SelectedIndex);
    }
}
