using Avalonia.Controls;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;

public sealed partial class SettingsWindow
{
    private BackgroundSettingsViewModel? _backgroundSettings;
    /// <summary>仅初始化草稿；表单取消不改变正在显示的画布。</summary>
    private void FillBackground()
    {
        _backgroundSettings = new(Config.Current.Background, Config.Current.ImageDotKeep);
        this.FindControl<ScrollViewer>("BackgroundSettings")!.DataContext = _backgroundSettings;
    }
}
