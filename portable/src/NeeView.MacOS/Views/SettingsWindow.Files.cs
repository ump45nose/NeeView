using Avalonia.Controls;
using Avalonia.Interactivity;
using NeeView;
namespace NeeView.MacOS.Views;

public sealed partial class SettingsWindow
{
    private ExternalAppCollection? _externalApplicationsDraft;
    private async void ExternalApplications_Click(object? sender, RoutedEventArgs e)
    {
        if (_saving) return;
        var source = _externalApplicationsDraft ?? Config.Current.System.ExternalAppCollection;
        var result = await new ExternalAppDialog(source).ShowDialog<ExternalAppCollection?>(this);
        if (result is not null && IsVisible && !_saving) _externalApplicationsDraft = result;
    }
    /// <summary>原System设置的表单副本；打开/取消不修改业务配置。</summary>
    private void FillFiles()
    {
        this.FindControl<CheckBox>("FileWriteAccess")!.IsChecked = Config.Current.System.IsFileWriteAccessEnabled;
        this.FindControl<CheckBox>("ZipWriteAccess")!.IsChecked = Config.Current.Archive.Zip.IsFileWriteAccessEnabled;
        this.FindControl<CheckBox>("RemoveConfirmed")!.IsChecked = Config.Current.System.IsRemoveConfirmed;
        this.FindControl<CheckBox>("OpenNextBookWhenRemove")!.IsChecked = Config.Current.Bookshelf.IsOpenNextBookWhenRemove;
        var textPolicy = this.FindControl<ComboBox>("TextCopyPolicy")!;
        textPolicy.SelectedIndex = Enum.IsDefined(Config.Current.System.TextCopyPolicy) ? (int)Config.Current.System.TextCopyPolicy : -1;
        this.FindControl<ComboBox>("ArchiveCopyPolicy")!.SelectedIndex = Enum.IsDefined(Config.Current.System.ArchiveCopyPolicy) ? (int)Config.Current.System.ArchiveCopyPolicy : -1;
    }
    /// <summary>经唯一ApplyOptions事务提交原字段；保存失败沿既有快照回滚。</summary>
    private void ApplyFiles()
    {
        Config.Current.System.IsFileWriteAccessEnabled = this.FindControl<CheckBox>("FileWriteAccess")!.IsChecked == true;
        Config.Current.Archive.Zip.IsFileWriteAccessEnabled = this.FindControl<CheckBox>("ZipWriteAccess")!.IsChecked == true;
        Config.Current.System.IsRemoveConfirmed = this.FindControl<CheckBox>("RemoveConfirmed")!.IsChecked == true;
        Config.Current.Bookshelf.IsOpenNextBookWhenRemove = this.FindControl<CheckBox>("OpenNextBookWhenRemove")!.IsChecked == true;
        // 未认识的旧值保留；只有用户选择了受支持的项才提交。
        if (this.FindControl<ComboBox>("TextCopyPolicy")!.SelectedIndex is >= 0 and <= 2 and var index)
            Config.Current.System.TextCopyPolicy = (TextCopyPolicy)index;
        if (this.FindControl<ComboBox>("ArchiveCopyPolicy")!.SelectedIndex is >= 0 and <= 3 and var archiveIndex)
            Config.Current.System.ArchiveCopyPolicy = (ArchivePolicy)archiveIndex;
        if (_externalApplicationsDraft is not null)
        {
            // 提交副本，失败回滚或再编辑均不能丢失父草稿。
            Config.Current.System.ExternalAppCollection = new(_externalApplicationsDraft.Select(app => (ExternalApp)app.Clone()));
        }
    }
}
