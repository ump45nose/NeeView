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
        catch (Exception ex) { if (!_closed) await ShowPickerErrorAsync(ex); }
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
        catch (Exception ex) { if (!_closed) await ShowPickerErrorAsync(ex); }
    }
    /// <summary>来源选择器错误独立展示，不修改预览草稿或当前阅读状态。</summary>
    private Task ShowPickerErrorAsync(Exception ex)
    { var dialog = new Window { Title = "来源选择失败", Width = 520, Height = 180, Content = new TextBlock { Text = ex.Message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new(16) } }; return dialog.ShowDialog(this); }
    /// <summary>新增草稿使旧候选失效，完整填写后再由用户重新预览。</summary>
    private void AddMapping(object? sender, RoutedEventArgs e) => _model?.Mappings.Add(new());
    /// <summary>移除指定映射行，不修改旧配置。</summary>
    private void RemoveMapping(object? sender, RoutedEventArgs e) { if (sender is Button { DataContext: ProfilePathMappingEdit mapping }) _model?.Mappings.Remove(mapping); }
    /// <summary>转交本次读取，验证和错误表现由独立 VM 负责。</summary>
    private async void RefreshPreview(object? sender, RoutedEventArgs e) { if (_model is not null) await _model.RefreshAsync(); }
    /// <summary>正常关闭只取消只读需求，不调用保存事务。</summary>
    private void ClosePreview(object? sender, RoutedEventArgs e) => Close();
}
