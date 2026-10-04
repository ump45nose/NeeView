using Avalonia.Controls;
namespace NeeView.MacOS.Views;

public sealed partial class SettingsWindow
{
    /// <summary>原System设置的表单副本；打开/取消不修改业务配置。</summary>
    private void FillFiles()
    {
        this.FindControl<CheckBox>("FileWriteAccess")!.IsChecked = Config.Current.System.IsFileWriteAccessEnabled;
        this.FindControl<CheckBox>("RemoveConfirmed")!.IsChecked = Config.Current.System.IsRemoveConfirmed;
    }
    /// <summary>经唯一ApplyOptions事务提交原字段；保存失败沿既有快照回滚。</summary>
    private void ApplyFiles()
    {
        Config.Current.System.IsFileWriteAccessEnabled = this.FindControl<CheckBox>("FileWriteAccess")!.IsChecked == true;
        Config.Current.System.IsRemoveConfirmed = this.FindControl<CheckBox>("RemoveConfirmed")!.IsChecked == true;
    }
}
