using Avalonia.Controls;
using Avalonia.Interactivity;
namespace NeeView.MacOS.Views;

/// <summary>设置关闭后由宿主转交现有功能；不包含存储、解码或窗口实例。</summary>
public enum SettingsAction { None, ImageEffects, ProfileImport }
public sealed partial class SettingsWindow
{
    /// <summary>仅明确保存成功或放弃按钮设置；普通取消不触发后续动作。</summary>
    public SettingsAction RequestedAction { get; private set; }
    private async void SaveAndOpenIntegration_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name } && Enum.TryParse<SettingsAction>(name, out var action))
            await SaveAndCloseAsync(action);
    }
    private void DiscardAndOpenIntegration_Click(object? sender, RoutedEventArgs e)
    {
        if (_saving || sender is not Button { Tag: string name } || !Enum.TryParse<SettingsAction>(name, out var action)) return;
        RequestedAction = action; Close();
    }
}
