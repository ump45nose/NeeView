using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;
public sealed partial class SettingsWindow
{
    private ScriptSettingsViewModel? _scriptSettings;
    private void FillScript()
    { _scriptSettings = new(Config.Current.Script); this.FindControl<ScrollViewer>("ScriptSettings")!.DataContext = _scriptSettings; Closed += (_, _) => _scriptSettings.Dispose(); }
    private async void OpenScriptFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (_saving || _scriptSettings is null || _themePlatform is null) return;
        await _scriptSettings.OpenFolderAsync(_themePlatform);
    }
    private async void ChooseScriptFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (_saving || _scriptSettings is null) return;
        var files = await StorageProvider.OpenFolderPickerAsync(new() { Title = "选择脚本目录" });
        if (IsVisible && files.FirstOrDefault()?.TryGetLocalPath() is { } path) _scriptSettings.Folder = path;
    }
}
