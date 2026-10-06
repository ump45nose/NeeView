using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;

public sealed partial class SettingsWindow
{
    private ThemeSettingsViewModel? _themeSettings;
    private IPlatformService? _themePlatform;
    /// <summary>按钮只转交明确的目录动作；文件准备在Engine、系统打开在平台契约。</summary>
    private async void OpenThemeFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (_themeSettings is null || _themePlatform is null || _saving || _themeSettings.IsOpeningFolder) return;
        var button = this.FindControl<Button>("OpenThemeFolder")!;
        button.IsEnabled = false;
        try { await _themeSettings.OpenFolderAsync(_themePlatform); }
        finally { if (IsVisible) button.IsEnabled = true; }
    }
    /// <summary>表单初始化只读取当前配置，后台请求由独立表现模型取消。</summary>
    private void FillTheme()
    {
        _themeSettings = new(Config.Current.Theme);
        this.FindControl<ScrollViewer>("ThemeSettings")!.DataContext = _themeSettings;
        Closed += (_, _) => _themeSettings.Dispose();
        _ = _themeSettings.RefreshAsync();
    }
    private async void RefreshThemes_Click(object? sender, RoutedEventArgs e)
    { if (_themeSettings is not null) await _themeSettings.RefreshAsync(); }
    /// <summary>系统选择器只更新目录草稿；取消/关闭不保存配置或修改文件。</summary>
    private async void ChooseThemeFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (_themeSettings is null || _saving) return;
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new() { Title = "选择自定义主题目录", AllowMultiple = false });
            if (!IsVisible || folders.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
            _themeSettings.Folder = path; await _themeSettings.RefreshAsync();
        }
        catch (Exception ex) { this.FindControl<TextBlock>("Message")!.Text = ex.Message; }
    }
}
