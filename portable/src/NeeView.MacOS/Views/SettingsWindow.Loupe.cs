using Avalonia.Controls;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;
public sealed partial class SettingsWindow
{
    private LoupeSettingsViewModel? _loupeSettings;
    /// <summary>沿用左导航/右表单与搜索，草稿不参与原生鼠标捕获。</summary>
    private void FillLoupe()
    {
        _loupeSettings = new(Config.Current.Loupe);
        this.FindControl<ScrollViewer>("LoupeSettings")!.DataContext = _loupeSettings;
    }
}
