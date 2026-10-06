using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;

/// <summary>独立预览窗口，系统选择器只返回路径；不解析、解包或保存旧数据。</summary>
public sealed partial class ProfileImportWindow : Window
{
    private readonly ProfileImportViewModel? _model;
    private bool _closed;
    /// <summary>设计器入口，加载正式 XAML 并登记关闭取消。</summary>
    public ProfileImportWindow() { AvaloniaXamlLoader.Load(this); Closed += (_, _) => { _closed = true; _model?.Dispose(); }; }
    /// <summary>使用 Engine 服务构造表现草稿；关闭后取消只读需求。</summary>
    public ProfileImportWindow(ProfileImportViewModel model) : this() { _model = model; DataContext = model; }
    /// <summary>选择 Profile 根，关闭后返回的系统结果不再发起读取。</summary>
    private async void ChooseDirectory(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new() { Title = "选择旧 NeeView Profile 目录", AllowMultiple = false });
            if (!_closed && _model is not null && folders.FirstOrDefault()?.TryGetLocalPath() is { } path)
                await _model.SelectSourceAsync(new(path, ProfileImportSourceKind.Directory));
        }
        catch (Exception ex) { if (!_closed) _model?.ReportError(ex.Message); }
    }
    /// <summary>选择标准 ZIP 备份，内容识别不只依赖扩展名。</summary>
    private async void ChooseBackup(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "选择 NeeView 备份", AllowMultiple = false,
                FileTypeFilter = [new("NeeView 备份") { Patterns = ["*.nvzip", "*.zip"] }] });
            if (!_closed && _model is not null && files.FirstOrDefault()?.TryGetLocalPath() is { } path)
                await _model.SelectSourceAsync(new(path, ProfileImportSourceKind.Backup));
        }
        catch (Exception ex) { if (!_closed) _model?.ReportError(ex.Message); }
    }
    /// <summary>新增草稿使旧候选失效，完整填写后再由用户重新预览。</summary>
    private void AddMapping(object? sender, RoutedEventArgs e) => _model?.Mappings.Add(new());
    /// <summary>移除指定映射行，不修改旧配置。</summary>
    private void RemoveMapping(object? sender, RoutedEventArgs e) { if (sender is Button { DataContext: ProfilePathMappingEdit mapping }) _model?.Mappings.Remove(mapping); }
    /// <summary>转交本次读取，验证和错误表现由独立 VM 负责。</summary>
    private async void RefreshPreview(object? sender, RoutedEventArgs e) { if (_model is not null) await _model.RefreshAsync(); }
    /// <summary>二次展示影响范围；确认后只返回候选，实际事务由宿主在对话框释放后执行。</summary>
    private async void ApplyImport(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_model is null || await _model.CreateRequestAsync() is not { } request || _closed) return;
            var dialog = new Window { Title = "确认导入", Width = 600, Height = 340, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new StackPanel { Margin = new(20), Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = "将先关闭并保存当前阅读窗口、备份原 JSON 与实际覆盖的附属文件，再恢复选中项目并重建窗口。\n选中附属类别覆盖同名文件，其他文件保持；播放列表选中后切换到 Mac 管理的列表目录。\n主题和脚本只保留材料，不应用主题、不执行脚本；未映射路径继续保留。\n历史保留开关与数量/期限仍按导入后的设置生效。", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12 };
            var apply = new Button { Content = "备份并导入" }; var cancel = new Button { Content = "取消" };
            apply.Click += (_, _) => dialog.Close(true); cancel.Click += (_, _) => dialog.Close(false);
            buttons.Children.Add(apply); buttons.Children.Add(cancel); panel.Children.Add(buttons); dialog.Content = panel;
            if (await dialog.ShowDialog<bool>(this) && !_closed) Close(request);
        }
        catch (Exception ex) { if (!_closed) _model?.ReportError(ex.Message); }
    }
    /// <summary>正常关闭取消读取，不调用保存事务。</summary>
    private void ClosePreview(object? sender, RoutedEventArgs e) => Close();
}
